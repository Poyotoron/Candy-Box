using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BlendshapeKeeper.Editor
{
    internal static class BlendshapeKeeperApplier
    {
        private const string UndoName = "Blendshape Keeper";

        internal static void Apply(
            BlendshapeKeeperPlan plan, out int changedKeys, out int changedClips)
        {
            changedKeys = 0;
            changedClips = 0;
            if (plan == null || plan.EnabledChangeCount == 0)
            {
                return;
            }

            for (int clipIndex = 0; clipIndex < plan.Clips.Count; clipIndex++)
            {
                ApplyClip(plan.Clips[clipIndex], ref changedKeys, ref changedClips);
            }

            if (changedKeys > 0)
            {
                AssetDatabase.SaveAssets();
            }
        }

        private static void ApplyClip(
            BlendshapeKeeperClipPlan clipPlan,
            ref int changedKeys,
            ref int changedClips)
        {
            AnimationClip clip = clipPlan.Clip;
            if (clip == null)
            {
                return;
            }

            var changesByBinding =
                new Dictionary<EditorCurveBinding, List<BlendshapeKeeperChange>>();
            for (int changeIndex = 0; changeIndex < clipPlan.Changes.Count; changeIndex++)
            {
                BlendshapeKeeperChange change = clipPlan.Changes[changeIndex];
                if (!change.Enabled)
                {
                    continue;
                }

                if (!changesByBinding.TryGetValue(change.Binding, out List<BlendshapeKeeperChange> changes))
                {
                    changes = new List<BlendshapeKeeperChange>();
                    changesByBinding.Add(change.Binding, changes);
                }

                changes.Add(change);
            }

            bool undoRecorded = false;
            bool clipChanged = false;
            foreach (KeyValuePair<EditorCurveBinding, List<BlendshapeKeeperChange>> group in
                     changesByBinding)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, group.Key);
                if (curve == null)
                {
                    continue;
                }

                bool curveChanged = false;
                List<BlendshapeKeeperChange> changes = group.Value;
                for (int changeIndex = 0; changeIndex < changes.Count; changeIndex++)
                {
                    BlendshapeKeeperChange change = changes[changeIndex];
                    if (change.KeyIndex < 0 || change.KeyIndex >= curve.length)
                    {
                        continue;
                    }

                    if (!undoRecorded)
                    {
                        Undo.RecordObject(clip, UndoName);
                        undoRecorded = true;
                    }

                    Keyframe key = curve[change.KeyIndex];
                    key.value = change.NewValue;
                    curve.MoveKey(change.KeyIndex, key);
                    curveChanged = true;
                    clipChanged = true;
                    changedKeys++;
                }

                if (curveChanged)
                {
                    AnimationUtility.SetEditorCurve(clip, group.Key, curve);
                }
            }

            if (clipChanged)
            {
                EditorUtility.SetDirty(clip);
                changedClips++;
            }
        }
    }
}
