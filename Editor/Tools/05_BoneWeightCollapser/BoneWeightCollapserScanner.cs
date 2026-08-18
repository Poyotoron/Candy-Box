using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BoneWeightCollapser.Editor
{
    internal static class BoneWeightCollapserScanner
    {
        private const string SelectTooltip = "クリックすると選択します";
        private const string DestinationRemovedWarning =
            "移動先ボーンが移動元に含まれていたため、移動元から除きました。";

        internal static List<BoneWeightCollapseTarget> CollectTargets(GameObject root)
        {
            var targets = new List<BoneWeightCollapseTarget>();
            if (root == null)
            {
                return targets;
            }

            SkinnedMeshRenderer[] renderers =
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                string relativePath = AnimationUtility.CalculateTransformPath(
                    renderer.transform, root.transform);
                string pathLabel = string.IsNullOrEmpty(relativePath)
                    ? root.name
                    : relativePath;
                targets.Add(new BoneWeightCollapseTarget
                {
                    Renderer = renderer,
                    PathLabel = pathLabel,
                    RowContent = new GUIContent(pathLabel + "  —", SelectTooltip),
                });
            }

            return targets;
        }

        internal static List<Transform> ResolveSourceBones(
            BoneWeightSourceMode mode,
            IList<Transform> explicitBones,
            Transform descendantsRoot,
            bool includeDescendantsRoot,
            Transform destination,
            List<string> warnings)
        {
            var result = new List<Transform>();
            var seen = new HashSet<Transform>();
            bool removedDestination = false;

            if (mode == BoneWeightSourceMode.Explicit)
            {
                if (explicitBones != null)
                {
                    for (int boneIndex = 0; boneIndex < explicitBones.Count; boneIndex++)
                    {
                        AddSourceBone(
                            explicitBones[boneIndex],
                            destination,
                            seen,
                            result,
                            ref removedDestination);
                    }
                }
            }
            else if (descendantsRoot != null)
            {
                Transform[] descendants =
                    descendantsRoot.GetComponentsInChildren<Transform>(true);
                for (int boneIndex = 0; boneIndex < descendants.Length; boneIndex++)
                {
                    Transform bone = descendants[boneIndex];
                    if (!includeDescendantsRoot && bone == descendantsRoot)
                    {
                        continue;
                    }

                    AddSourceBone(
                        bone,
                        destination,
                        seen,
                        result,
                        ref removedDestination);
                }
            }

            if (removedDestination && warnings != null)
            {
                warnings.Add(DestinationRemovedWarning);
            }

            return result;
        }

        internal static void Scan(BoneWeightCollapserPlan plan)
        {
            if (plan == null)
            {
                return;
            }

            plan.TotalAffectedVertexCount = 0;
            plan.TotalMovedWeight = 0f;
            float blendRatio = Mathf.Clamp01(plan.BlendRatio);
            for (int targetIndex = 0; targetIndex < plan.Targets.Count; targetIndex++)
            {
                BoneWeightCollapseTarget target = plan.Targets[targetIndex];
                ResetScanState(target);
                try
                {
                    if (target.Renderer == null || target.Renderer.sharedMesh == null)
                    {
                        SetBlocked(target, BoneWeightBlockReason.MissingMesh);
                        continue;
                    }

                    SkinnedMeshRenderer renderer = target.Renderer;
                    Mesh mesh = renderer.sharedMesh;
                    target.SourceMesh = mesh;
                    target.VertexCount = mesh.vertexCount;
                    Transform[] rendererBones = renderer.bones;
                    if (rendererBones == null || rendererBones.Length == 0)
                    {
                        SetBlocked(target, BoneWeightBlockReason.NoBoneWeights);
                        continue;
                    }

                    // NOTE: 返される配列は Mesh が所有する読み取り専用ビューなので、
                    // 呼び出し側では Dispose しない。
                    NativeArray<byte> bonesPerVertex = mesh.GetBonesPerVertex();
                    if (bonesPerVertex.Length == 0)
                    {
                        SetBlocked(target, BoneWeightBlockReason.NoBoneWeights);
                        continue;
                    }

                    target.DestinationBoneIndex = FindFirstBoneIndex(
                        rendererBones, plan.Destination);
                    if (target.DestinationBoneIndex < 0)
                    {
                        SetBlocked(target, BoneWeightBlockReason.DestinationNotBound);
                        continue;
                    }

                    var sourceInfoByBoneIndex = new Dictionary<int, int>();
                    BuildSourceBoneInformation(
                        target, plan.SourceBones, rendererBones, sourceInfoByBoneIndex);
                    if (target.SourceBoneIndices.Count == 0)
                    {
                        BuildSourceRows(target);
                        SetBlocked(target, BoneWeightBlockReason.NoSourceBound);
                        continue;
                    }

                    NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
                    ScanWeights(
                        target,
                        bonesPerVertex,
                        weights,
                        sourceInfoByBoneIndex,
                        blendRatio);
                    BuildSourceRows(target);
                    if (target.AffectedVertexCount == 0)
                    {
                        SetBlocked(target, BoneWeightBlockReason.NoAffectedVertex);
                        continue;
                    }

                    target.RowContent = new GUIContent(
                        string.Format(
                            "{0}  頂点 {1} / 影響 {2}",
                            target.PathLabel,
                            target.VertexCount,
                            target.AffectedVertexCount),
                        SelectTooltip);
                    target.DetailsExpanded = true;
                    if (target.IsSelected)
                    {
                        plan.TotalAffectedVertexCount += target.AffectedVertexCount;
                        plan.TotalMovedWeight += target.MovedWeightTotal;
                    }
                }
                catch (Exception)
                {
                    BuildSourceRows(target);
                    SetBlocked(target, BoneWeightBlockReason.UnreadableMesh);
                }
            }
        }

        private static void AddSourceBone(
            Transform bone,
            Transform destination,
            HashSet<Transform> seen,
            List<Transform> result,
            ref bool removedDestination)
        {
            if (bone == null)
            {
                return;
            }

            if (bone == destination)
            {
                removedDestination = true;
                return;
            }

            if (seen.Add(bone))
            {
                result.Add(bone);
            }
        }

        private static int FindFirstBoneIndex(Transform[] bones, Transform bone)
        {
            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
            {
                if (bones[boneIndex] != null && bones[boneIndex] == bone)
                {
                    return boneIndex;
                }
            }

            return -1;
        }

        private static void BuildSourceBoneInformation(
            BoneWeightCollapseTarget target,
            List<Transform> sourceBones,
            Transform[] rendererBones,
            Dictionary<int, int> sourceInfoByBoneIndex)
        {
            for (int sourceIndex = 0; sourceIndex < sourceBones.Count; sourceIndex++)
            {
                Transform sourceBone = sourceBones[sourceIndex];
                var sourceInfo = new BoneWeightSourceBoneInfo { Bone = sourceBone };
                target.SourceBones.Add(sourceInfo);
                for (int rendererBoneIndex = 0;
                     rendererBoneIndex < rendererBones.Length;
                     rendererBoneIndex++)
                {
                    if (rendererBones[rendererBoneIndex] == null ||
                        rendererBones[rendererBoneIndex] != sourceBone)
                    {
                        continue;
                    }

                    sourceInfo.BoneIndices.Add(rendererBoneIndex);
                    target.SourceBoneIndices.Add(rendererBoneIndex);
                    sourceInfoByBoneIndex.Add(rendererBoneIndex, sourceIndex);
                }
            }
        }

        private static void ScanWeights(
            BoneWeightCollapseTarget target,
            NativeArray<byte> bonesPerVertex,
            NativeArray<BoneWeight1> weights,
            Dictionary<int, int> sourceInfoByBoneIndex,
            float blendRatio)
        {
            var countedSourceIndices = new HashSet<int>();
            // NOTE: 頂点ごとの開始位置を積算しないと、毎回先頭から数えることになり
            // 頂点数の二乗に比例して遅くなる。
            int offset = 0;
            for (int vertexIndex = 0; vertexIndex < bonesPerVertex.Length; vertexIndex++)
            {
                int count = bonesPerVertex[vertexIndex];
                bool affected = false;
                countedSourceIndices.Clear();
                for (int weightIndex = 0; weightIndex < count; weightIndex++)
                {
                    BoneWeight1 entry = weights[offset + weightIndex];
                    if (entry.weight <= 0f ||
                        !sourceInfoByBoneIndex.TryGetValue(
                            entry.boneIndex, out int sourceInfoIndex))
                    {
                        continue;
                    }

                    float moved = entry.weight * blendRatio;
                    if (moved <= 0f)
                    {
                        continue;
                    }

                    affected = true;
                    target.MovedWeightTotal += moved;
                    BoneWeightSourceBoneInfo sourceInfo =
                        target.SourceBones[sourceInfoIndex];
                    sourceInfo.MovedWeight += moved;
                    if (countedSourceIndices.Add(sourceInfoIndex))
                    {
                        sourceInfo.VertexCount++;
                    }
                }

                if (affected)
                {
                    target.AffectedVertexCount++;
                }

                offset += count;
            }
        }

        private static void BuildSourceRows(BoneWeightCollapseTarget target)
        {
            for (int sourceIndex = 0; sourceIndex < target.SourceBones.Count; sourceIndex++)
            {
                BoneWeightSourceBoneInfo source = target.SourceBones[sourceIndex];
                string boneName = source.Bone == null ? "(Missing)" : source.Bone.name;
                source.RowLabel = source.VertexCount > 0
                    ? string.Format(
                        "{0}  頂点 {1} / ウェイト {2:0.00}",
                        boneName,
                        source.VertexCount,
                        source.MovedWeight)
                    : boneName + "  該当なし";
            }
        }

        private static void ResetScanState(BoneWeightCollapseTarget target)
        {
            // NOTE: 影響が無い対象の内訳は「該当なし」が中心で縦を使うため、
            // 走査ごとに閉じた状態から結果に応じて開き直す。
            target.DetailsExpanded = false;
            target.SourceMesh = null;
            target.BlockReason = BoneWeightBlockReason.None;
            target.BlockedLabel = null;
            target.VertexCount = 0;
            target.AffectedVertexCount = 0;
            target.MovedWeightTotal = 0f;
            target.DestinationBoneIndex = -1;
            target.SourceBoneIndices.Clear();
            target.SourceBones.Clear();
        }

        private static void SetBlocked(
            BoneWeightCollapseTarget target, BoneWeightBlockReason reason)
        {
            target.DetailsExpanded = false;
            target.BlockReason = reason;
            switch (reason)
            {
                case BoneWeightBlockReason.MissingMesh:
                    target.BlockedLabel = "メッシュが設定されていません。";
                    break;
                case BoneWeightBlockReason.NoBoneWeights:
                    target.BlockedLabel = "ボーンウェイトを持っていません。";
                    break;
                case BoneWeightBlockReason.UnreadableMesh:
                    target.BlockedLabel = "メッシュのデータを読み取れませんでした。";
                    break;
                case BoneWeightBlockReason.DestinationNotBound:
                    target.BlockedLabel =
                        "移動先ボーンがこのメッシュのボーン一覧にありません。";
                    break;
                case BoneWeightBlockReason.NoSourceBound:
                    target.BlockedLabel =
                        "移動元ボーンがこのメッシュのボーン一覧にありません。";
                    break;
                case BoneWeightBlockReason.NoAffectedVertex:
                    target.BlockedLabel = "影響を受ける頂点がありません。";
                    break;
            }

            target.RowContent = new GUIContent(
                target.PathLabel + "  —", SelectTooltip);
        }
    }
}
