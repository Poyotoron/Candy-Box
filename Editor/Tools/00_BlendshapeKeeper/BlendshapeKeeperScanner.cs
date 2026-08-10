using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BlendshapeKeeper.Editor
{
    internal static class BlendshapeKeeperScanner
    {
        private const string BlendShapePrefix = "blendShape.";
        private const float Epsilon = 0.0001f;

        private const string SceneClipReason = "シーン内のクリップは対象外です";
        private const string ModelClipReason = "モデルファイルに含まれるクリップは編集できません";
        private const string ReadOnlyPathReason = "書き込みできない場所にあります";
        private const string OutsideAvatarReason = "アバタールートの配下にありません";
        private const string MissingMeshReason = "メッシュが設定されていません";
        private const string MissingBlendShapeReason = "メッシュにこのブレンドシェイプがありません";

        internal static BlendshapeKeeperPlan Scan(
            GameObject avatarRoot,
            IReadOnlyList<SkinnedMeshRenderer> targetMeshes,
            IReadOnlyList<AnimationClip> clips,
            BlendshapeKeeperOutputMode outputMode)
        {
            var plan = new BlendshapeKeeperPlan();
            if (avatarRoot == null || targetMeshes == null || targetMeshes.Count == 0 ||
                clips == null || clips.Count == 0)
            {
                return plan;
            }

            var skipKeys = new HashSet<string>();
            Dictionary<string, SkinnedMeshRenderer> meshesByPath =
                BuildTargetMeshMap(avatarRoot, targetMeshes, plan, skipKeys);

            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                AnimationClip clip = clips[clipIndex];
                if (clip == null)
                {
                    continue;
                }

                try
                {
                    ScanClip(clip, meshesByPath, outputMode, plan, skipKeys);
                }
                catch (Exception exception)
                {
                    AddSkip(
                        plan,
                        skipKeys,
                        GetClipTarget(clip),
                        "走査中に予期しないエラーが発生しました: " + exception.Message);
                }
            }

            return plan;
        }

        private static Dictionary<string, SkinnedMeshRenderer> BuildTargetMeshMap(
            GameObject avatarRoot,
            IReadOnlyList<SkinnedMeshRenderer> targetMeshes,
            BlendshapeKeeperPlan plan,
            HashSet<string> skipKeys)
        {
            var meshesByPath = new Dictionary<string, SkinnedMeshRenderer>();
            Transform rootTransform = avatarRoot.transform;
            for (int meshIndex = 0; meshIndex < targetMeshes.Count; meshIndex++)
            {
                SkinnedMeshRenderer renderer = targetMeshes[meshIndex];
                if (renderer == null)
                {
                    continue;
                }

                try
                {
                    Transform rendererTransform = renderer.transform;
                    if (rendererTransform != rootTransform &&
                        !rendererTransform.IsChildOf(rootTransform))
                    {
                        AddSkip(plan, skipKeys, renderer.name, OutsideAvatarReason);
                        continue;
                    }

                    string path = AnimationUtility.CalculateTransformPath(
                        rendererTransform, rootTransform);
                    meshesByPath[path] = renderer;
                }
                catch (Exception)
                {
                    AddSkip(plan, skipKeys, renderer.name, OutsideAvatarReason);
                }
            }

            return meshesByPath;
        }

        private static void ScanClip(
            AnimationClip clip,
            Dictionary<string, SkinnedMeshRenderer> meshesByPath,
            BlendshapeKeeperOutputMode outputMode,
            BlendshapeKeeperPlan plan,
            HashSet<string> skipKeys)
        {
            string assetPath = AssetDatabase.GetAssetPath(clip);
            string clipTarget = string.IsNullOrEmpty(assetPath) ? clip.name : assetPath;
            if (string.IsNullOrEmpty(assetPath))
            {
                AddSkip(plan, skipKeys, clipTarget, SceneClipReason);
                return;
            }

            if (outputMode == BlendshapeKeeperOutputMode.Overwrite &&
                !string.Equals(
                    Path.GetExtension(assetPath), ".anim", StringComparison.OrdinalIgnoreCase))
            {
                AddSkip(plan, skipKeys, clipTarget, ModelClipReason);
                return;
            }

            if (outputMode == BlendshapeKeeperOutputMode.Overwrite &&
                assetPath.StartsWith("Packages/", StringComparison.Ordinal))
            {
                AddSkip(plan, skipKeys, clipTarget, ReadOnlyPathReason);
                return;
            }

            var clipPlan = new BlendshapeKeeperClipPlan
            {
                Clip = clip,
                ClipLabel = clip.name,
            };

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                EditorCurveBinding binding = bindings[bindingIndex];
                if (binding.type != typeof(SkinnedMeshRenderer) ||
                    !binding.propertyName.StartsWith(
                        BlendShapePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                // NOTE: 明示指定されていないメッシュは、変更候補にもスキップにも出さない。
                if (!meshesByPath.TryGetValue(
                        binding.path, out SkinnedMeshRenderer renderer))
                {
                    continue;
                }

                ScanBinding(clip, binding, renderer, clipPlan, plan, skipKeys);
            }

            if (clipPlan.Changes.Count > 0)
            {
                plan.Clips.Add(clipPlan);
            }
        }

        private static void ScanBinding(
            AnimationClip clip,
            EditorCurveBinding binding,
            SkinnedMeshRenderer renderer,
            BlendshapeKeeperClipPlan clipPlan,
            BlendshapeKeeperPlan plan,
            HashSet<string> skipKeys)
        {
            string blendShapeName = binding.propertyName.Substring(BlendShapePrefix.Length);
            string displayPath = string.IsNullOrEmpty(binding.path) ? "<root>" : binding.path;
            string target = displayPath + " / " + blendShapeName;

            Mesh mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                AddSkip(plan, skipKeys, target, MissingMeshReason);
                return;
            }

            int blendShapeIndex = mesh.GetBlendShapeIndex(blendShapeName);
            if (blendShapeIndex < 0)
            {
                AddSkip(plan, skipKeys, target, MissingBlendShapeReason);
                return;
            }

            float currentValue = renderer.GetBlendShapeWeight(blendShapeIndex);
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null)
            {
                return;
            }

            // NOTE: keys は取得のたびに配列を複製するため、一度だけ取り出して走査する。
            Keyframe[] keys = curve.keys;
            for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                Keyframe key = keys[keyIndex];
                if (key.value >= currentValue - Epsilon)
                {
                    continue;
                }

                clipPlan.Changes.Add(new BlendshapeKeeperChange
                {
                    Binding = binding,
                    KeyIndex = keyIndex,
                    Time = key.time,
                    OldValue = key.value,
                    NewValue = currentValue,
                    Label = displayPath + " / " + blendShapeName +
                        "  t=" + key.time.ToString("0.###") +
                        "  " + key.value.ToString("0.#") +
                        " → " + currentValue.ToString("0.#"),
                });
            }
        }

        private static string GetClipTarget(AnimationClip clip)
        {
            string assetPath = AssetDatabase.GetAssetPath(clip);
            return string.IsNullOrEmpty(assetPath) ? clip.name : assetPath;
        }

        private static void AddSkip(
            BlendshapeKeeperPlan plan,
            HashSet<string> skipKeys,
            string target,
            string reason)
        {
            string key = target + "\u001f" + reason;
            if (!skipKeys.Add(key))
            {
                return;
            }

            plan.Skips.Add(new BlendshapeKeeperSkip
            {
                Target = target,
                Reason = reason,
                Label = target + " — " + reason,
            });
        }
    }
}
