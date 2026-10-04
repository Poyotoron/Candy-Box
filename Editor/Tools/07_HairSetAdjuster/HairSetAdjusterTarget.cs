using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.HairSetAdjuster.Editor
{
    internal static class HairSetAdjusterTarget
    {
        private const string PlayingReason = "再生中は使えません。再生を停止してください。";
        private const string FaceReason = "顔メッシュを指定してください。";
        private const string HumanReason = "顔メッシュが人型のアバターの中にありません。";
        private const string HairReason = "髪を指定してください。";
        private const string HierarchyReason = "髪をアバターの中に置いてください。";
        private const string OverrideReason = "調整の対象は髪の中から選んでください。";
        private const string ContainsFaceReason = "調整の対象に顔が含まれています。髪だけを含む対象を選んでください。";

        internal static HairSetTarget Resolve(SkinnedMeshRenderer face, GameObject hair,
            Transform adjustTargetOverride)
        {
            var target = new HairSetTarget { Face = face, Hair = hair };
            if (EditorApplication.isPlaying) return Block(target, PlayingReason);
            if (face == null || face.sharedMesh == null) return Block(target, FaceReason);
            Animator animator = face.GetComponentInParent<Animator>();
            if (animator == null || !animator.isHuman) return Block(target, HumanReason);
            target.Animator = animator;
            target.AvatarRoot = animator.gameObject;
            target.HeadBone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (target.HeadBone == null) return Block(target, HumanReason);
            if (hair == null) return Block(target, HairReason);
            if (hair == target.AvatarRoot || !hair.transform.IsChildOf(animator.transform))
                return Block(target, HierarchyReason);
            target.HairHeadBone = FindHairHead(hair.transform, target.HeadBone);
            target.AdjustTarget = adjustTargetOverride != null ? adjustTargetOverride :
                target.HairHeadBone != null ? target.HairHeadBone : hair.transform;
            if (!target.AdjustTarget.IsChildOf(hair.transform)) return Block(target, OverrideReason);
            if (face.transform.IsChildOf(target.AdjustTarget)) return Block(target, ContainsFaceReason);
            target.HumanoidBoneNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = (int)HumanBodyBones.Hips; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null) target.HumanoidBoneNames.Add(bone.name);
            }
            target.HeadVertexIndices = CollectHeadVertices(face, target.HeadBone);
            target.HeadCenter = FindCenter(target);
            string path = AnimationUtility.CalculateTransformPath(target.AdjustTarget, hair.transform);
            string reason = adjustTargetOverride != null ? "指定した対象" : target.HairHeadBone != null ?
                "髪の中の頭ボーンを選びました" : "髪に頭ボーンが無いため、髪のルートを選びました";
            target.AdjustTargetLabel = "調整の対象: " + (path.Length == 0 ? hair.name : path) + "（" + reason + "）";
            return target;
        }

        private static HairSetTarget Block(HairSetTarget target, string reason)
        {
            target.BlockedReason = reason;
            return target;
        }

        private static Transform FindHairHead(Transform hair, Transform head)
        {
            Transform best = null;
            bool bestParent = false;
            int bestDepth = int.MaxValue;
            foreach (Transform candidate in hair.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(candidate.name, head.name, StringComparison.Ordinal)) continue;
                // NOTE: 同名の飾り用ボーンを避け、親名・深さ・列挙順の順で選ぶ。
                bool parentMatches = candidate.parent != null && head.parent != null &&
                    string.Equals(candidate.parent.name, head.parent.name, StringComparison.Ordinal);
                int depth = 0;
                for (Transform t = candidate; t != hair; t = t.parent) depth++;
                if (best == null || (parentMatches && !bestParent) ||
                    (parentMatches == bestParent && depth < bestDepth))
                {
                    best = candidate;
                    bestParent = parentMatches;
                    bestDepth = depth;
                }
            }
            return best;
        }

        private static int[] CollectHeadVertices(SkinnedMeshRenderer face, Transform head)
        {
            var indices = new List<int>();
            // NOTE: Mesh 所有の読み取り専用ビューなので Dispose しない。
            var counts = face.sharedMesh.GetBonesPerVertex();
            var weights = face.sharedMesh.GetAllBoneWeights();
            Transform[] bones = face.bones;
            int offset = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                int bestBone = -1;
                float bestWeight = float.NegativeInfinity;
                for (int j = 0; j < counts[i]; j++)
                {
                    BoneWeight1 weight = weights[offset++];
                    if (weight.weight > bestWeight) { bestBone = weight.boneIndex; bestWeight = weight.weight; }
                }
                // NOTE: 最も重い骨で分類し、首や肩に少しだけ頭のウェイトがある頂点を含めない。
                if (bestBone >= 0 && bestBone < bones.Length && bones[bestBone] != null &&
                    bones[bestBone].IsChildOf(head)) indices.Add(i);
            }
            return indices.ToArray();
        }

        private static Vector3 FindCenter(HairSetTarget target)
        {
            if (target.HeadVertexIndices.Length == 0) return target.HeadBone.position;
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                target.Face.BakeMesh(mesh, true);
                // NOTE: BakeMesh に拡大を焼き込み済みなので、ワールド化では拡大を重ねない。
                Matrix4x4 matrix = Matrix4x4.TRS(target.Face.transform.position, target.Face.transform.rotation, Vector3.one);
                Vector3[] vertices = mesh.vertices;
                var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[target.HeadVertexIndices[0]]), Vector3.zero);
                foreach (int index in target.HeadVertexIndices) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[index]));
                return bounds.center;
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
