using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.PartsSetAdjuster.Editor
{
    internal static class PartsSetAdjusterTarget
    {
        private const string PlayingReason = "再生中は使えません。再生を停止してください。";
        private const string ReferenceMeshReason = "基準のメッシュを指定してください。";
        private const string HumanReason = "基準のメッシュが人型のアバターの中にありません。";
        private const string PartReason = "パーツを指定してください。";
        private const string HierarchyReason = "パーツをアバターの中に置いてください。";
        private const string OverrideReason = "動かすボーンとメッシュは、パーツの中から選んでください。";
        private const string ContainsReferenceReason = "調整の対象に基準のメッシュが含まれています。パーツだけを含む対象を選んでください。";

        internal static readonly HumanBodyBones[] ReferenceParts =
        {
            HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.UpperChest, HumanBodyBones.Chest,
            HumanBodyBones.Spine, HumanBodyBones.Hips, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
        };
        internal static readonly string[] ReferencePartNames =
        {
            "頭", "首", "上胸", "胸", "背骨", "腰", "左肩", "右肩", "左上腕", "右上腕",
            "左前腕", "右前腕", "左手", "右手", "左太もも", "右太もも", "左すね", "右すね", "左足", "右足"
        };

        internal static PartsSetTarget Resolve(
            PartsSetKind kind, SkinnedMeshRenderer referenceMesh, int referencePartIndex,
            GameObject part, PartsSetMoveRange moveRange, Transform moveBone, Renderer moveMesh)
        {
            int index = kind == PartsSetKind.Hair ? 0 : Mathf.Clamp(referencePartIndex, 0, ReferenceParts.Length - 1);
            var target = new PartsSetTarget
            {
                Kind = kind,
                ReferencePart = ReferenceParts[index],
                ReferencePartName = ReferencePartNames[index],
                ReferenceMesh = referenceMesh,
                Part = part,
                MoveRange = moveRange,
                MoveMesh = moveMesh
            };
            if (EditorApplication.isPlaying)
            {
                return Block(target, PlayingReason);
            }

            if (referenceMesh == null || referenceMesh.sharedMesh == null)
            {
                return Block(target, ReferenceMeshReason);
            }

            Animator animator = referenceMesh.GetComponentInParent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                return Block(target, HumanReason);
            }

            target.Animator = animator;
            target.AvatarRoot = animator.gameObject;
            target.ReferenceBone = animator.GetBoneTransform(target.ReferencePart);
            if (target.ReferenceBone == null)
            {
                return Block(target, "アバターに" + target.ReferencePartName + "のボーンがありません。基準の部位を選び直してください。");
            }

            if (part == null)
            {
                return Block(target, PartReason);
            }

            if (part == target.AvatarRoot || !part.transform.IsChildOf(animator.transform))
            {
                return Block(target, HierarchyReason);
            }

            target.PartReferenceBone = FindPartReferenceBone(part.transform, target.ReferenceBone);
            if (moveRange == PartsSetMoveRange.Bone && moveBone == null)
            {
                return Block(target, "動かすボーンを指定してください。");
            }

            if (moveRange == PartsSetMoveRange.Mesh)
            {
                if (moveMesh == null)
                {
                    return Block(target, "動かすメッシュを指定してください。");
                }

                if (!moveMesh.transform.IsChildOf(part.transform))
                {
                    return Block(target, OverrideReason);
                }

                target.AdjustTarget = ResolveMeshTarget(moveMesh);
                if (target.AdjustTarget == null || !target.AdjustTarget.IsChildOf(part.transform))
                {
                    return Block(target, "このメッシュはパーツの外のボーンで動いているため、動かせません。");
                }
            }
            else
            {
                target.AdjustTarget = moveRange == PartsSetMoveRange.Bone
                    ? moveBone
                    : target.PartReferenceBone != null ? target.PartReferenceBone : part.transform;
            }

            if (!target.AdjustTarget.IsChildOf(part.transform))
            {
                return Block(target, OverrideReason);
            }

            if (referenceMesh.transform.IsChildOf(target.AdjustTarget))
            {
                return Block(target, ContainsReferenceReason);
            }

            target.HumanoidBoneNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = (int)HumanBodyBones.Hips; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null)
                {
                    target.HumanoidBoneNames.Add(bone.name);
                }
            }

            target.ReferenceVertexIndices = CollectReferenceVertices(referenceMesh, target.ReferenceBone, animator);
            target.ReferenceCenter = FindCenter(target);
            string path = AnimationUtility.CalculateTransformPath(target.AdjustTarget, part.transform);
            string reason = moveRange == PartsSetMoveRange.Bone
                ? "指定したボーン"
                : moveRange == PartsSetMoveRange.Mesh ? moveMesh.gameObject.name + " を動かしているボーン"
                : target.PartReferenceBone != null
                    ? "パーツの中の" + target.ReferencePartName + "ボーンを選びました"
                    : "パーツに" + target.ReferencePartName + "ボーンが無いため、パーツのルートを選びました";
            target.AdjustTargetLabel = "調整の対象: " + (path.Length == 0 ? part.name : path) + "（" + reason + "）";
            if (moveRange != PartsSetMoveRange.Whole)
            {
                var names = new List<string>();
                foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        (moveRange == PartsSetMoveRange.Mesh && renderer == moveMesh))
                    {
                        continue;
                    }

                    bool[] moving = MovingVertices(renderer, target.AdjustTarget);
                    if (Array.IndexOf(moving, true) >= 0)
                    {
                        names.Add(renderer.gameObject.name);
                    }
                }

                if (names.Count > 0)
                {
                    target.CoMovingLabel = "一緒に動くメッシュ: " + string.Join("、", names.GetRange(0, Math.Min(3, names.Count)));
                    if (names.Count > 3)
                    {
                        target.CoMovingLabel += " ほか " + (names.Count - 3) + " 件";
                    }
                }
            }

            return target;
        }

        internal static int[] DominantBones(Mesh mesh)
        {
            var result = new int[mesh.vertexCount];
            Array.Fill(result, -1);
            // NOTE: Mesh 所有の読み取り専用ビューなので Dispose しない。
            var counts = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            int offset = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                float best = float.NegativeInfinity;
                for (int j = 0; j < counts[i]; j++)
                {
                    BoneWeight1 weight = weights[offset++];
                    if (weight.weight > best)
                    {
                        best = weight.weight;
                        result[i] = weight.boneIndex;
                    }
                }
            }

            return result;
        }

        internal static bool[] MovingVertices(Renderer renderer, Transform adjustTarget)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned == null && !(renderer is MeshRenderer))
            {
                return Array.Empty<bool>();
            }

            Mesh mesh;
            if (skinned != null)
            {
                mesh = skinned.sharedMesh;
            }
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                // NOTE: GetComponent が返す見つからない値は ?. で止まらないため、Unity の == で確かめる。
                if (filter == null)
                {
                    return Array.Empty<bool>();
                }

                mesh = filter.sharedMesh;
            }

            if (mesh == null)
            {
                return Array.Empty<bool>();
            }

            var result = new bool[mesh.vertexCount];
            Transform[] bones = skinned != null ? skinned.bones : Array.Empty<Transform>();
            if (bones.Length == 0)
            {
                if (renderer.transform.IsChildOf(adjustTarget))
                {
                    Array.Fill(result, true);
                }

                return result;
            }

            int[] dominant = DominantBones(mesh);
            for (int i = 0; i < result.Length; i++)
            {
                int index = dominant[i];
                result[i] = index >= 0 && index < bones.Length &&
                    bones[index] != null && bones[index].IsChildOf(adjustTarget);
            }

            return result;
        }

        private static Transform ResolveMeshTarget(Renderer renderer)
        {
            if (renderer is MeshRenderer)
            {
                return renderer.transform;
            }

            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned == null)
            {
                return null;
            }

            Transform[] bones = skinned.bones;
            if (bones.Length == 0)
            {
                return renderer.transform;
            }

            if (skinned.sharedMesh == null)
            {
                return null;
            }

            // NOTE: スキンメッシュは Renderer の移動では変形しないため、最も重い骨の共通祖先を動かす。
            var used = new HashSet<Transform>();
            foreach (int index in DominantBones(skinned.sharedMesh))
            {
                if (index >= 0 && index < bones.Length && bones[index] != null)
                {
                    used.Add(bones[index]);
                }
            }

            foreach (Transform first in used)
            {
                for (Transform candidate = first; candidate != null; candidate = candidate.parent)
                {
                    bool containsAll = true;
                    foreach (Transform bone in used)
                    {
                        if (!bone.IsChildOf(candidate))
                        {
                            containsAll = false;
                            break;
                        }
                    }

                    if (containsAll)
                    {
                        return candidate;
                    }
                }

                break;
            }

            return null;
        }

        private static PartsSetTarget Block(PartsSetTarget target, string reason)
        {
            target.BlockedReason = reason;
            return target;
        }

        private static Transform FindPartReferenceBone(Transform part, Transform referenceBone)
        {
            Transform best = null;
            bool bestParent = false;
            int bestDepth = int.MaxValue;
            foreach (Transform candidate in part.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(candidate.name, referenceBone.name, StringComparison.Ordinal))
                {
                    continue;
                }

                // NOTE: 同名の飾り用ボーンを避け、親名・深さ・列挙順の順で選ぶ。
                bool parentMatches = candidate.parent != null &&
                    referenceBone.parent != null &&
                    string.Equals(candidate.parent.name, referenceBone.parent.name, StringComparison.Ordinal);
                int depth = 0;
                for (Transform t = candidate; t != part; t = t.parent)
                {
                    depth++;
                }

                if (best == null ||
                    (parentMatches && !bestParent) ||
                    (parentMatches == bestParent && depth < bestDepth))
                {
                    best = candidate;
                    bestParent = parentMatches;
                    bestDepth = depth;
                }
            }

            return best;
        }

        private static int[] CollectReferenceVertices(SkinnedMeshRenderer referenceMesh, Transform referenceBone, Animator animator)
        {
            var humanoid = new HashSet<Transform>();
            for (int i = (int)HumanBodyBones.Hips; i < (int)HumanBodyBones.LastBone; i++)
            {
                HumanBodyBones part = (HumanBodyBones)i;
                // NOTE: 目と顎は頭の領域に含め、髪を合わせる中心が変わらないようにする。
                if (part == HumanBodyBones.LeftEye || part == HumanBodyBones.RightEye || part == HumanBodyBones.Jaw)
                {
                    continue;
                }

                Transform bone = animator.GetBoneTransform(part);
                if (bone != null)
                {
                    humanoid.Add(bone);
                }
            }

            var region = new HashSet<Transform>();
            var stack = new Stack<Transform>();
            stack.Push(referenceBone);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                region.Add(current);
                foreach (Transform child in current)
                {
                    // NOTE: 隣の人型部位まで含めると、腰や胸の中心が全身側へずれる。
                    if (!humanoid.Contains(child))
                    {
                        stack.Push(child);
                    }
                }
            }

            var indices = new List<int>();
            // NOTE: Mesh 所有の読み取り専用ビューなので Dispose しない。
            var counts = referenceMesh.sharedMesh.GetBonesPerVertex();
            var weights = referenceMesh.sharedMesh.GetAllBoneWeights();
            Transform[] bones = referenceMesh.bones;
            int offset = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                int bestBone = -1;
                float bestWeight = float.NegativeInfinity;
                for (int j = 0; j < counts[i]; j++)
                {
                    BoneWeight1 weight = weights[offset++];
                    if (weight.weight > bestWeight)
                    {
                        bestBone = weight.boneIndex;
                        bestWeight = weight.weight;
                    }
                }

                // NOTE: 最も重い骨で分類し、首や肩に少しだけ頭のウェイトがある頂点を含めない。
                if (bestBone >= 0 &&
                    bestBone < bones.Length &&
                    bones[bestBone] != null &&
                    region.Contains(bones[bestBone]))
                {
                    indices.Add(i);
                }
            }

            return indices.ToArray();
        }

        private static Vector3 FindCenter(PartsSetTarget target)
        {
            if (target.ReferenceVertexIndices.Length == 0)
            {
                return target.ReferenceBone.position;
            }

            var mesh = new Mesh
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            try
            {
                target.ReferenceMesh.BakeMesh(mesh, true);
                // NOTE: BakeMesh に拡大を焼き込み済みなので、ワールド化では拡大を重ねない。
                Matrix4x4 matrix = Matrix4x4.TRS(
                    target.ReferenceMesh.transform.position,
                    target.ReferenceMesh.transform.rotation,
                    Vector3.one);
                Vector3[] vertices = mesh.vertices;
                var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[target.ReferenceVertexIndices[0]]), Vector3.zero);
                foreach (int index in target.ReferenceVertexIndices)
                {
                    bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[index]));
                }

                return bounds.center;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
