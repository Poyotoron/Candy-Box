using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BlendshapeKeeper.Editor
{
    /// <summary>プレビュー用クリップを作る。生成物の破棄は呼び出し側が行う。</summary>
    internal static class BlendshapeKeeperPreviewClip
    {
        private const float TimeEpsilon = 0.0001f;

        internal static float[] CollectTimes(BlendshapeKeeperClipPlan clipPlan)
        {
            if (clipPlan == null || clipPlan.Changes.Count == 0)
            {
                return Array.Empty<float>();
            }

            var times = new List<float>(clipPlan.Changes.Count);
            for (int changeIndex = 0; changeIndex < clipPlan.Changes.Count; changeIndex++)
            {
                times.Add(clipPlan.Changes[changeIndex].Time);
            }

            times.Sort();
            int destinationIndex = 1;
            for (int sourceIndex = 1; sourceIndex < times.Count; sourceIndex++)
            {
                if (Mathf.Abs(times[sourceIndex] - times[destinationIndex - 1]) < TimeEpsilon)
                {
                    continue;
                }

                times[destinationIndex] = times[sourceIndex];
                destinationIndex++;
            }

            if (destinationIndex < times.Count)
            {
                times.RemoveRange(destinationIndex, times.Count - destinationIndex);
            }

            return times.ToArray();
        }

        internal static AnimationClip CreateModifiedClip(BlendshapeKeeperClipPlan clipPlan)
        {
            if (clipPlan == null || clipPlan.Clip == null)
            {
                return null;
            }

            AnimationClip modified = UnityEngine.Object.Instantiate(clipPlan.Clip);
            modified.name = clipPlan.Clip.name + " (preview)";
            modified.hideFlags = HideFlags.HideAndDontSave;

            var changesByBinding =
                new Dictionary<EditorCurveBinding, List<BlendshapeKeeperChange>>();
            for (int changeIndex = 0; changeIndex < clipPlan.Changes.Count; changeIndex++)
            {
                BlendshapeKeeperChange change = clipPlan.Changes[changeIndex];
                if (!change.Enabled)
                {
                    continue;
                }

                if (!changesByBinding.TryGetValue(
                        change.Binding, out List<BlendshapeKeeperChange> changes))
                {
                    changes = new List<BlendshapeKeeperChange>();
                    changesByBinding.Add(change.Binding, changes);
                }

                changes.Add(change);
            }

            foreach (KeyValuePair<EditorCurveBinding, List<BlendshapeKeeperChange>> group in
                     changesByBinding)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(modified, group.Key);
                if (curve == null)
                {
                    continue;
                }

                Keyframe[] keys = curve.keys;
                bool changed = false;
                for (int changeIndex = 0; changeIndex < group.Value.Count; changeIndex++)
                {
                    BlendshapeKeeperChange change = group.Value[changeIndex];
                    if (change.KeyIndex < 0 || change.KeyIndex >= keys.Length)
                    {
                        continue;
                    }

                    Keyframe key = keys[change.KeyIndex];
                    key.value = change.NewValue;
                    curve.MoveKey(change.KeyIndex, key);
                    changed = true;
                }

                if (changed)
                {
                    AnimationUtility.SetEditorCurve(modified, group.Key, curve);
                }
            }

            return modified;
        }
    }
}
