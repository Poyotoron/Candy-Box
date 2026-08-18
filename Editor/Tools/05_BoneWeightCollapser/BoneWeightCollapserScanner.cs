using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BoneWeightCollapser.Editor
{
    internal static class BoneWeightCollapserScanner
    {
        private static readonly List<BoneWeightTargetRule> ApplicableRules =
            new List<BoneWeightTargetRule>();

        private const string SelectTooltip = "クリックすると選択します";
        private const string DestinationRemovedWarningFormat =
            "ルール {0}: 移動先ボーンが移動元に含まれていたため、移動元から除きました。";
        private const string MissingDestinationLabel =
            "移動先ボーンが指定されていません。";
        private const string NoSourceBonesLabel =
            "移動元ボーンが指定されていません。";
        private const string DestinationNotBoundLabel =
            "移動先ボーンがこのメッシュのボーン一覧にありません。";
        private const string NoSourceBoundLabel =
            "移動元ボーンがこのメッシュのボーン一覧にありません。";

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

        internal static List<BoneWeightResolvedRule> ResolveRules(
            IList<BoneWeightCollapseRule> rules,
            List<string> warnings)
        {
            var result = new List<BoneWeightResolvedRule>();
            if (rules == null)
            {
                return result;
            }

            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                BoneWeightCollapseRule input = rules[ruleIndex];
                var resolved = new BoneWeightResolvedRule
                {
                    Number = ruleIndex + 1,
                    Destination = input == null ? null : input.Destination,
                    BlendRatio = input == null ? 1f : input.BlendRatio,
                };
                ResolveRuleSourceBones(input, resolved, warnings);
                if (resolved.Destination == null)
                {
                    resolved.BlockReason = BoneWeightRuleBlockReason.MissingDestination;
                    resolved.BlockedLabel = MissingDestinationLabel;
                }
                else if (resolved.SourceBones.Count == 0)
                {
                    resolved.BlockReason = BoneWeightRuleBlockReason.NoSourceBones;
                    resolved.BlockedLabel = NoSourceBonesLabel;
                }

                if (resolved.BlockReason != BoneWeightRuleBlockReason.None)
                {
                    resolved.HeaderLabel = string.Format(
                        "ルール {0}  無効: {1}",
                        resolved.Number,
                        resolved.BlockedLabel);
                }

                result.Add(resolved);
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

                    BuildTargetRules(target, plan.Rules, rendererBones);
                    ApplicableRules.Clear();
                    for (int ruleIndex = 0;
                         ruleIndex < target.Rules.Count;
                         ruleIndex++)
                    {
                        BoneWeightTargetRule targetRule = target.Rules[ruleIndex];
                        if (targetRule.IsApplicable)
                        {
                            // NOTE: リストへ走査順に足し、表示順と処理順を一致させる。
                            ApplicableRules.Add(targetRule);
                        }
                    }

                    if (ApplicableRules.Count == 0)
                    {
                        BuildRuleRows(target);
                        SetBlocked(target, BoneWeightBlockReason.NoApplicableRule);
                        continue;
                    }

                    bool succeeded = BoneWeightCollapserBaker.Collapse(
                        mesh,
                        ApplicableRules,
                        plan.Normalize,
                        false,
                        out Mesh ignoredMesh,
                        out BoneWeightCollapseStats stats);
                    if (!succeeded)
                    {
                        BuildRuleRows(target);
                        SetBlocked(target, BoneWeightBlockReason.UnreadableMesh);
                        continue;
                    }

                    target.AffectedVertexCount = stats.AffectedVertexCount;
                    target.MovedWeightTotal = stats.MovedWeightTotal;
                    BuildRuleRows(target);
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
                    BuildRuleRows(target);
                    SetBlocked(target, BoneWeightBlockReason.UnreadableMesh);
                }
            }
        }

        private static void ResolveRuleSourceBones(
            BoneWeightCollapseRule input,
            BoneWeightResolvedRule resolved,
            List<string> warnings)
        {
            var seen = new HashSet<Transform>();
            bool removedDestination = false;
            if (input != null && input.SourceMode == BoneWeightSourceMode.Explicit)
            {
                if (input.ExplicitBones != null)
                {
                    for (int boneIndex = 0;
                         boneIndex < input.ExplicitBones.Count;
                         boneIndex++)
                    {
                        AddSourceBone(
                            input.ExplicitBones[boneIndex],
                            resolved.Destination,
                            seen,
                            resolved.SourceBones,
                            ref removedDestination);
                    }
                }
            }
            else if (input != null && input.DescendantsRoot != null)
            {
                Transform[] descendants =
                    input.DescendantsRoot.GetComponentsInChildren<Transform>(true);
                for (int boneIndex = 0; boneIndex < descendants.Length; boneIndex++)
                {
                    Transform bone = descendants[boneIndex];
                    if (!input.IncludeDescendantsRoot &&
                        bone == input.DescendantsRoot)
                    {
                        continue;
                    }

                    AddSourceBone(
                        bone,
                        resolved.Destination,
                        seen,
                        resolved.SourceBones,
                        ref removedDestination);
                }
            }

            if (removedDestination && warnings != null)
            {
                warnings.Add(string.Format(
                    DestinationRemovedWarningFormat, resolved.Number));
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

        private static void BuildTargetRules(
            BoneWeightCollapseTarget target,
            IList<BoneWeightResolvedRule> rules,
            Transform[] rendererBones)
        {
            if (rules == null)
            {
                return;
            }

            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                BoneWeightResolvedRule resolved = rules[ruleIndex];
                var targetRule = new BoneWeightTargetRule
                {
                    Rule = resolved,
                    BlockReason = resolved.BlockReason,
                    BlockedLabel = resolved.BlockedLabel,
                };
                target.Rules.Add(targetRule);
                if (targetRule.BlockReason != BoneWeightRuleBlockReason.None)
                {
                    continue;
                }

                targetRule.DestinationBoneIndex = FindFirstBoneIndex(
                    rendererBones, resolved.Destination);
                if (targetRule.DestinationBoneIndex < 0)
                {
                    SetRuleBlocked(
                        targetRule,
                        BoneWeightRuleBlockReason.DestinationNotBound,
                        DestinationNotBoundLabel);
                    continue;
                }

                BuildSourceBoneInformation(targetRule, resolved.SourceBones, rendererBones);
                if (targetRule.SourceBoneIndices.Count == 0)
                {
                    SetRuleBlocked(
                        targetRule,
                        BoneWeightRuleBlockReason.NoSourceBound,
                        NoSourceBoundLabel);
                }
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
            BoneWeightTargetRule targetRule,
            IList<Transform> sourceBones,
            Transform[] rendererBones)
        {
            for (int sourceIndex = 0; sourceIndex < sourceBones.Count; sourceIndex++)
            {
                Transform sourceBone = sourceBones[sourceIndex];
                var sourceInfo = new BoneWeightSourceBoneInfo { Bone = sourceBone };
                targetRule.SourceBones.Add(sourceInfo);
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
                    targetRule.SourceBoneIndices.Add(rendererBoneIndex);
                    targetRule.SourceInfoByBoneIndex.Add(
                        rendererBoneIndex, sourceIndex);
                }
            }
        }

        private static void BuildRuleRows(BoneWeightCollapseTarget target)
        {
            for (int ruleIndex = 0; ruleIndex < target.Rules.Count; ruleIndex++)
            {
                BoneWeightTargetRule targetRule = target.Rules[ruleIndex];
                if (!targetRule.IsApplicable)
                {
                    targetRule.HeaderLabel = string.Format(
                        "ルール {0}  無効: {1}",
                        targetRule.Rule.Number,
                        targetRule.BlockedLabel);
                    continue;
                }

                targetRule.HeaderLabel = targetRule.AffectedVertexCount > 0
                    ? string.Format(
                        "ルール {0}  影響 {1} 頂点 / ウェイト {2:0.00}",
                        targetRule.Rule.Number,
                        targetRule.AffectedVertexCount,
                        targetRule.MovedWeight)
                    : string.Format(
                        "ルール {0}  影響なし", targetRule.Rule.Number);
                for (int sourceIndex = 0;
                     sourceIndex < targetRule.SourceBones.Count;
                     sourceIndex++)
                {
                    BoneWeightSourceBoneInfo source =
                        targetRule.SourceBones[sourceIndex];
                    string boneName = source.Bone == null
                        ? "(Missing)"
                        : source.Bone.name;
                    source.RowLabel = source.VertexCount > 0
                        ? string.Format(
                            "{0}  頂点 {1} / ウェイト {2:0.00}",
                            boneName,
                            source.VertexCount,
                            source.MovedWeight)
                        : boneName + "  該当なし";
                }
            }
        }

        private static void SetRuleBlocked(
            BoneWeightTargetRule targetRule,
            BoneWeightRuleBlockReason reason,
            string label)
        {
            targetRule.BlockReason = reason;
            targetRule.BlockedLabel = label;
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
            if (target.Rules == null)
            {
                // NOTE: 直列化などで失われたリストを使う側でも復元し、走査を継続する。
                target.Rules = new List<BoneWeightTargetRule>();
            }
            else
            {
                target.Rules.Clear();
            }
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
                case BoneWeightBlockReason.NoApplicableRule:
                    target.BlockedLabel = "このメッシュに適用できるルールがありません。";
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
