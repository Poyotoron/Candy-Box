using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Poyo.CandyBox.BoneWeightCollapser.Editor
{
    internal static class BoneWeightCollapserBaker
    {
        private const int MaximumInfluenceCount = byte.MaxValue;

        internal static bool Collapse(
            Mesh source,
            IList<BoneWeightTargetRule> rules,
            bool normalize,
            bool buildMesh,
            out Mesh baked,
            out BoneWeightCollapseStats stats)
        {
            baked = null;
            stats = default;
            ResetRuleStatistics(rules);
            if (source == null || rules == null || rules.Count == 0)
            {
                return false;
            }

            try
            {
                // NOTE: 返される配列は Mesh が所有する読み取り専用ビューなので、
                // 呼び出し側では Dispose しない。
                NativeArray<byte> sourceCounts = source.GetBonesPerVertex();
                NativeArray<BoneWeight1> sourceWeights = source.GetAllBoneWeights();
                var vertexEntries = new List<BoneWeight1>(16);
                var countedSourceInfoIndices = new HashSet<int>();
                // NOTE: 走査では結果配列もメッシュも不要なため、生成時だけ確保する。
                List<BoneWeight1> resultWeights = buildMesh
                    ? new List<BoneWeight1>(sourceWeights.Length)
                    : null;
                byte[] resultCounts = buildMesh ? new byte[sourceCounts.Length] : null;

                // NOTE: 頂点ごとの開始位置を積算しないと、毎回先頭から数えることになり
                // 頂点数の二乗に比例して遅くなる。
                int offset = 0;
                for (int vertexIndex = 0;
                     vertexIndex < sourceCounts.Length;
                     vertexIndex++)
                {
                    int count = sourceCounts[vertexIndex];
                    vertexEntries.Clear();
                    for (int weightIndex = 0; weightIndex < count; weightIndex++)
                    {
                        vertexEntries.Add(sourceWeights[offset + weightIndex]);
                    }

                    bool vertexAffected = false;
                    int lastDestinationBoneIndex = -1;
                    for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                    {
                        BoneWeightTargetRule rule = rules[ruleIndex];
                        float ratio = Mathf.Clamp01(rule.Rule.BlendRatio);
                        float ruleMoved = 0f;
                        countedSourceInfoIndices.Clear();

                        // NOTE: 移動先の要素は走査中に追加せず、添字が変わらない状態で
                        // 移動元をすべて処理してから加える。
                        for (int entryIndex = 0;
                             entryIndex < vertexEntries.Count;
                             entryIndex++)
                        {
                            BoneWeight1 entry = vertexEntries[entryIndex];
                            if (entry.weight <= 0f ||
                                !rule.SourceInfoByBoneIndex.TryGetValue(
                                    entry.boneIndex, out int sourceInfoIndex))
                            {
                                continue;
                            }

                            float moved = entry.weight * ratio;
                            if (moved <= 0f)
                            {
                                continue;
                            }

                            entry.weight -= moved;
                            vertexEntries[entryIndex] = entry;
                            ruleMoved += moved;
                            BoneWeightSourceBoneInfo sourceInfo =
                                rule.SourceBones[sourceInfoIndex];
                            sourceInfo.MovedWeight += moved;
                            if (countedSourceInfoIndices.Add(sourceInfoIndex))
                            {
                                sourceInfo.VertexCount++;
                            }
                        }

                        if (ruleMoved <= 0f)
                        {
                            continue;
                        }

                        int destinationEntryIndex = FindBoneIndex(
                            vertexEntries, rule.DestinationBoneIndex);
                        if (destinationEntryIndex >= 0)
                        {
                            BoneWeight1 destinationEntry =
                                vertexEntries[destinationEntryIndex];
                            destinationEntry.weight += ruleMoved;
                            vertexEntries[destinationEntryIndex] = destinationEntry;
                        }
                        else
                        {
                            vertexEntries.Add(new BoneWeight1
                            {
                                boneIndex = rule.DestinationBoneIndex,
                                weight = ruleMoved,
                            });
                        }

                        rule.MovedWeight += ruleMoved;
                        rule.AffectedVertexCount++;
                        stats.MovedWeightTotal += ruleMoved;
                        lastDestinationBoneIndex = rule.DestinationBoneIndex;
                        vertexAffected = true;
                    }

                    if (!vertexAffected)
                    {
                        // NOTE: 触れていない頂点の値や並びを変えないため、元の列をそのまま使う。
                        if (buildMesh)
                        {
                            for (int weightIndex = 0;
                                 weightIndex < count;
                                 weightIndex++)
                            {
                                resultWeights.Add(sourceWeights[offset + weightIndex]);
                            }

                            resultCounts[vertexIndex] = (byte)count;
                        }

                        offset += count;
                        continue;
                    }

                    stats.AffectedVertexCount++;
                    if (!buildMesh)
                    {
                        offset += count;
                        continue;
                    }

                    RemoveNonPositiveEntries(vertexEntries);
                    if (vertexEntries.Count == 0)
                    {
                        vertexEntries.Add(new BoneWeight1
                        {
                            boneIndex = lastDestinationBoneIndex,
                            weight = 1f,
                        });
                    }

                    // NOTE: スキンウェイトの品質設定は先頭から順に採用するため、
                    // 大きいウェイトが捨てられないよう全ルールの処理後に降順へ並べる。
                    StableSortByDescendingWeight(vertexEntries);
                    bool influenceLimitApplied = false;
                    while (vertexEntries.Count > MaximumInfluenceCount)
                    {
                        // NOTE: 影響数は byte で保持されるため 255 件が上限になる。
                        // 切り詰めで合計が崩れるので、正規化の指定にかかわらず正規化する。
                        vertexEntries.RemoveAt(vertexEntries.Count - 1);
                        influenceLimitApplied = true;
                    }

                    // NOTE: ルールごとに正規化すると後のルールが見る量が変わるため、
                    // 全ルールを適用し終えたあとに 1 回だけ行う。
                    if (normalize || influenceLimitApplied)
                    {
                        Normalize(vertexEntries);
                    }

                    resultCounts[vertexIndex] = (byte)vertexEntries.Count;
                    resultWeights.AddRange(vertexEntries);
                    offset += count;
                }

                if (!buildMesh)
                {
                    return true;
                }

                // NOTE: Mesh の複製なら BlendShape・法線・タンジェント・UV・
                // bindposes が引き継がれるため、それぞれをコピーし直さない。
                baked = UnityEngine.Object.Instantiate(source);
                // NOTE: バインド姿勢を書き換えると静止時の頂点位置まで変わるため、
                // 複製元から引き継いだ bindposes を保つ。
                BoneWeight1[] flatWeights = resultWeights.ToArray();
                using (var counts = new NativeArray<byte>(resultCounts, Allocator.Temp))
                using (var weights = new NativeArray<BoneWeight1>(
                           flatWeights, Allocator.Temp))
                {
                    baked.SetBoneWeights(counts, weights);
                }

                return true;
            }
            catch (Exception)
            {
                if (baked != null)
                {
                    // NOTE: 例外時にシーンにもアセットにも属さない複製を残さない。
                    UnityEngine.Object.DestroyImmediate(baked);
                }

                baked = null;
                stats = default;
                ResetRuleStatistics(rules);
                return false;
            }
        }

        private static void ResetRuleStatistics(IList<BoneWeightTargetRule> rules)
        {
            if (rules == null)
            {
                return;
            }

            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                BoneWeightTargetRule rule = rules[ruleIndex];
                if (rule == null)
                {
                    continue;
                }

                rule.AffectedVertexCount = 0;
                rule.MovedWeight = 0f;
                if (rule.SourceBones == null)
                {
                    continue;
                }

                for (int sourceIndex = 0;
                     sourceIndex < rule.SourceBones.Count;
                     sourceIndex++)
                {
                    BoneWeightSourceBoneInfo sourceInfo = rule.SourceBones[sourceIndex];
                    sourceInfo.MovedWeight = 0f;
                    sourceInfo.VertexCount = 0;
                }
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
