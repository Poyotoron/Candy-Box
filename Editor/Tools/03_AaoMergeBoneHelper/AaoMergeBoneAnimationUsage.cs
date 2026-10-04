using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.AaoMergeBoneHelper.Editor
{
    internal static class AaoMergeBoneAnimationUsage
    {
        internal static HashSet<Transform> Collect(GameObject avatarRoot)
        {
            var bones = new HashSet<Transform>();
            if (avatarRoot == null) return bones;
            // NOTE: 同じクリップでも持ち主が違えば相対パスの起点が違うため、組で重複を除く。
            var ownedClips = new Dictionary<Transform, HashSet<AnimationClip>>();
            foreach (Component component in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;
                try
                {
                    using (var serialized = new SerializedObject(component))
                    {
                        SerializedProperty iterator = serialized.GetIterator();
                        // NOTE: 非表示のフィールドや配列内のアニメーション参照も収集する。
                        while (iterator.Next(true))
                        {
                            if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                            UnityEngine.Object reference = iterator.objectReferenceValue;
                            if (reference is RuntimeAnimatorController controller)
                            {
                                foreach (AnimationClip clip in controller.animationClips)
                                    AddClip(ownedClips, component.transform, clip);
                            }
                            else if (reference is AnimationClip clip)
                                AddClip(ownedClips, component.transform, clip);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Candy Box: " + component.GetType().Name +
                        " のアニメーション参照を読めませんでした。\n" + exception);
                }
            }

            foreach (KeyValuePair<Transform, HashSet<AnimationClip>> pair in ownedClips)
            {
                foreach (AnimationClip clip in pair.Value)
                {
                    foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != typeof(Transform)) continue;
                        // NOTE: 連携先によって起点が異なるため、見落としを避けて両方から解決する。
                        AddResolved(bones, avatarRoot.transform, binding.path);
                        if (pair.Key != avatarRoot.transform) AddResolved(bones, pair.Key, binding.path);
                    }
                }
            }
            return bones;
        }

        private static void AddClip(Dictionary<Transform, HashSet<AnimationClip>> ownedClips,
            Transform owner, AnimationClip clip)
        {
            if (clip == null) return;
            if (!ownedClips.TryGetValue(owner, out HashSet<AnimationClip> clips))
            {
                clips = new HashSet<AnimationClip>();
                ownedClips.Add(owner, clips);
            }
            clips.Add(clip);
        }

        private static void AddResolved(HashSet<Transform> bones, Transform origin, string path)
        {
            // NOTE: パスが空のカーブは起点そのものを動かす。
            Transform resolved = string.IsNullOrEmpty(path) ? origin : origin.Find(path);
            if (resolved != null) bones.Add(resolved);
        }
    }
}
