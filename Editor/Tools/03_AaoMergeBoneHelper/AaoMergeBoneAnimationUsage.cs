using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.AaoMergeBoneHelper.Editor
{
    internal static class AaoMergeBoneAnimationUsage
    {
        internal static HashSet<string> Collect(GameObject avatarRoot)
        {
            var paths = new HashSet<string>();
            if (avatarRoot == null)
            {
                return paths;
            }

            var clips = new HashSet<AnimationClip>();
            Animator[] animators = avatarRoot.GetComponentsInChildren<Animator>(true);
            for (int animatorIndex = 0; animatorIndex < animators.Length; animatorIndex++)
            {
                RuntimeAnimatorController controller =
                    animators[animatorIndex].runtimeAnimatorController;
                if (controller == null)
                {
                    continue;
                }

                AnimationClip[] controllerClips = controller.animationClips;
                for (int clipIndex = 0; clipIndex < controllerClips.Length; clipIndex++)
                {
                    if (controllerClips[clipIndex] != null)
                    {
                        clips.Add(controllerClips[clipIndex]);
                    }
                }
            }

            foreach (AnimationClip clip in clips)
            {
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                {
                    EditorCurveBinding binding = bindings[bindingIndex];
                    if (binding.type == typeof(Transform))
                    {
                        paths.Add(binding.path);
                    }
                }
            }

            return paths;
        }
    }
}
