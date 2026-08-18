using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Poyo.CandyBox.BoneWeightCollapser.Editor
{
    internal static class BoneWeightCollapserBaker
    {
        private const int MaximumInfluenceCount = byte.MaxValue;

        internal static Mesh Bake(
            Mesh source,
            ICollection<int> sourceBoneIndices,
            int destinationBoneIndex,
            float blendRatio,
            bool normalize,
            out int affectedVertexCount,
            out float movedWeight)
        {
            affectedVertexCount = 0;
            movedWeight = 0f;
            if (source == null || sourceBoneIndices == null || destinationBoneIndex < 0)
            {
                return null;
            }

            Mesh baked = null;
            try
            {
                // NOTE: Mesh の複製なら BlendShape・法線・タンジェント・UV・
                // bindposes が引き継がれるため、それぞれをコピーし直さない。
                baked = UnityEngine.Object.Instantiate(source);
                // NOTE: バインド姿勢では各ボーンの変換が単位行列になるため、
                // ウェイトだけを移しても静止時の頂点位置は変わらない。
                // この前提を保つため、複製後の bindposes は書き換えない。
                // NOTE: 返される配列は Mesh が所有する読み取り専用ビューなので、
                // 呼び出し側では Dispose しない。
                NativeArray<byte> sourceCounts = source.GetBonesPerVertex();
                NativeArray<BoneWeight1> sourceWeights = source.GetAllBoneWeights();
                float ratio = Mathf.Clamp01(blendRatio);
                var sourceIndices = new HashSet<int>(sourceBoneIndices);
                var vertexEntries = new List<BoneWeight1>(16);
                var resultWeights = new List<BoneWeight1>(sourceWeights.Length);
                var resultCounts = new byte[sourceCounts.Length];

                int offset = 0;
                for (int vertexIndex = 0; vertexIndex < sourceCounts.Length; vertexIndex++)
                {
                    int count = sourceCounts[vertexIndex];
                    vertexEntries.Clear();
                    float vertexMovedWeight = 0f;
                    for (int weightIndex = 0; weightIndex < count; weightIndex++)
                    {
                        BoneWeight1 entry = sourceWeights[offset + weightIndex];
                        if (sourceIndices.Contains(entry.boneIndex))
                        {
                            float moved = entry.weight * ratio;
                            vertexMovedWeight += moved;
                            entry.weight *= 1f - ratio;
                        }

                        vertexEntries.Add(entry);
                    }

                    if (vertexMovedWeight <= 0f)
                    {
                        for (int weightIndex = 0; weightIndex < count; weightIndex++)
                        {
                            resultWeights.Add(sourceWeights[offset + weightIndex]);
                        }

                        resultCounts[vertexIndex] = (byte)count;
                        offset += count;
                        continue;
                    }

                    int destinationEntryIndex = FindBoneIndex(
                        vertexEntries, destinationBoneIndex);
                    if (destinationEntryIndex >= 0)
                    {
                        BoneWeight1 destinationEntry =
                            vertexEntries[destinationEntryIndex];
                        destinationEntry.weight += vertexMovedWeight;
                        vertexEntries[destinationEntryIndex] = destinationEntry;
                    }
                    else
                    {
                        vertexEntries.Add(new BoneWeight1
                        {
                            boneIndex = destinationBoneIndex,
                            weight = vertexMovedWeight,
                        });
                    }

                    RemoveNonPositiveEntries(vertexEntries);
                    if (vertexEntries.Count == 0)
                    {
                        vertexEntries.Add(new BoneWeight1
                        {
                            boneIndex = destinationBoneIndex,
                            weight = 1f,
                        });
                    }

                    // NOTE: スキンウェイトの品質設定は先頭から順に採用するため、
                    // 大きいウェイトが捨てられないよう降順に並べる。
                    StableSortByDescendingWeight(vertexEntries);
                    bool influenceLimitApplied = false;
                    if (vertexEntries.Count > MaximumInfluenceCount)
                    {
                        // NOTE: 影響数は byte で保持されるため 255 件が上限になる。
                        // 切り詰めで合計が崩れるので、正規化の指定にかかわらず正規化する。
                        vertexEntries.RemoveAt(vertexEntries.Count - 1);
                        influenceLimitApplied = true;
                    }

                    if (normalize || influenceLimitApplied)
                    {
                        Normalize(vertexEntries);
                    }

                    resultCounts[vertexIndex] = (byte)vertexEntries.Count;
                    resultWeights.AddRange(vertexEntries);
                    affectedVertexCount++;
                    movedWeight += vertexMovedWeight;
                    offset += count;
                }

                BoneWeight1[] flatWeights = resultWeights.ToArray();
                using (var counts = new NativeArray<byte>(
                           resultCounts, Allocator.Temp))
                using (var weights = new NativeArray<BoneWeight1>(
                           flatWeights, Allocator.Temp))
                {
                    baked.SetBoneWeights(counts, weights);
                }

                return baked;
            }
            catch (Exception)
            {
                if (baked != null)
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }

                affectedVertexCount = 0;
                movedWeight = 0f;
                return null;
            }
        }

        private static int FindBoneIndex(List<BoneWeight1> entries, int boneIndex)
        {
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                if (entries[entryIndex].boneIndex == boneIndex)
                {
                    return entryIndex;
                }
            }

            return -1;
        }

        private static void RemoveNonPositiveEntries(List<BoneWeight1> entries)
        {
            for (int entryIndex = entries.Count - 1; entryIndex >= 0; entryIndex--)
            {
                if (entries[entryIndex].weight <= 0f)
                {
                    entries.RemoveAt(entryIndex);
                }
            }
        }

        private static void StableSortByDescendingWeight(List<BoneWeight1> entries)
        {
            for (int entryIndex = 1; entryIndex < entries.Count; entryIndex++)
            {
                BoneWeight1 entry = entries[entryIndex];
                int insertionIndex = entryIndex;
                while (insertionIndex > 0 &&
                       entries[insertionIndex - 1].weight < entry.weight)
                {
                    entries[insertionIndex] = entries[insertionIndex - 1];
                    insertionIndex--;
                }

                entries[insertionIndex] = entry;
            }
        }

        private static void Normalize(List<BoneWeight1> entries)
        {
            float total = 0f;
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                total += entries[entryIndex].weight;
            }

            if (total <= 0f)
            {
                return;
            }

            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                BoneWeight1 entry = entries[entryIndex];
                entry.weight /= total;
                entries[entryIndex] = entry;
            }
        }
    }
}
