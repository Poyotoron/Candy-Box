using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BlendshapeKeeper.Editor
{
    internal struct BlendshapeKeeperApplyResult
    {
        internal int ChangedKeys;
        internal int ChangedClips;
        internal string FirstOutputPath;
        internal int RenamedCount;
        internal int CreatedClips;
    }

    internal static class BlendshapeKeeperApplier
    {
        private const string UndoName = "Blendshape Keeper";

        internal static BlendshapeKeeperApplyResult Apply(
            BlendshapeKeeperPlan plan,
            BlendshapeKeeperOutputMode outputMode,
            string outputFolderPath,
            string suffix,
            bool copyWithoutChanges)
        {
            var result = new BlendshapeKeeperApplyResult();
            if (plan == null ||
                (plan.EnabledChangeCount == 0 &&
                 !(outputMode == BlendshapeKeeperOutputMode.SaveAsCopy &&
                   copyWithoutChanges)))
            {
                return result;
            }

            for (int clipIndex = 0; clipIndex < plan.Clips.Count; clipIndex++)
            {
                BlendshapeKeeperClipPlan clipPlan = plan.Clips[clipIndex];
                if (outputMode == BlendshapeKeeperOutputMode.SaveAsCopy)
                {
                    ApplyAsCopy(
                        clipPlan,
                        outputFolderPath,
                        suffix,
                        copyWithoutChanges,
                        ref result);
                }
                else if (ApplyChangesToClip(
                             clipPlan.Clip, clipPlan, true, ref result.ChangedKeys))
                {
                    EditorUtility.SetDirty(clipPlan.Clip);
                    result.ChangedClips++;
                }
            }

            if (result.ChangedKeys > 0 || result.CreatedClips > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return result;
        }

        private static void ApplyAsCopy(
            BlendshapeKeeperClipPlan clipPlan,
            string outputFolderPath,
            string suffix,
            bool copyWithoutChanges,
            ref BlendshapeKeeperApplyResult result)
        {
            if (clipPlan == null || clipPlan.Clip == null)
            {
                return;
            }

            bool hasEnabledChange = false;
            for (int changeIndex = 0; changeIndex < clipPlan.Changes.Count; changeIndex++)
            {
                if (clipPlan.Changes[changeIndex].Enabled)
                {
                    hasEnabledChange = true;
                    break;
                }
            }

            if (!hasEnabledChange && !copyWithoutChanges)
            {
                return;
            }

            AnimationClip copy = Object.Instantiate(clipPlan.Clip);
            copy.name = clipPlan.Clip.name + SanitizeSuffix(suffix);
            string desiredPath = outputFolderPath.TrimEnd('/') + "/" + copy.name + ".anim";
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(desiredPath);
            if (!string.Equals(uniquePath, desiredPath, System.StringComparison.Ordinal))
            {
                result.RenamedCount++;
            }

            AssetDatabase.CreateAsset(copy, uniquePath);
            result.CreatedClips++;
            if (string.IsNullOrEmpty(result.FirstOutputPath))
            {
                result.FirstOutputPath = uniquePath;
            }

            if (!ApplyChangesToClip(copy, clipPlan, false, ref result.ChangedKeys))
            {
                return;
            }

            EditorUtility.SetDirty(copy);
            result.ChangedClips++;
        }

        private static bool ApplyChangesToClip(
            AnimationClip target,
            BlendshapeKeeperClipPlan clipPlan,
            bool recordUndo,
            ref int changedKeys)
        {
            if (target == null || clipPlan == null)
            {
                return false;
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
                AnimationCurve curve = AnimationUtility.GetEditorCurve(target, group.Key);
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

                    if (recordUndo && !undoRecorded)
                    {
                        Undo.RecordObject(target, UndoName);
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
                    AnimationUtility.SetEditorCurve(target, group.Key, curve);
                }
            }

            return clipChanged;
        }

        private static string SanitizeSuffix(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                return string.Empty;
            }

            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            char[] characters = suffix.ToCharArray();
            for (int characterIndex = 0; characterIndex < characters.Length; characterIndex++)
            {
                if (System.Array.IndexOf(invalidCharacters, characters[characterIndex]) >= 0)
                {
                    characters[characterIndex] = '_';
                }
            }

            return new string(characters);
        }
    }
}
