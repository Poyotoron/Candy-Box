using System.Collections.Generic;
using UnityEngine;

namespace Poyo.CandyBox.BoneWeightCollapser.Editor
{
    internal enum BoneWeightSourceMode
    {
        /// <summary>並べた Transform だけを対象にする。</summary>
        [InspectorName("直接指定")]
        Explicit,

        /// <summary>起点の子孫を再帰的に対象にする。</summary>
        [InspectorName("子孫")]
        Descendants,
    }

    internal enum BoneWeightBlockReason
    {
        None,
        MissingMesh,
        NoBoneWeights,
        UnreadableMesh,
        NoApplicableRule,
        NoAffectedVertex,
    }

    internal enum BoneWeightRuleBlockReason
    {
        None,
        MissingDestination,
        NoSourceBones,
        DestinationNotBound,
        NoSourceBound,
    }

    [System.Serializable]
    internal sealed class BoneWeightCollapseRule
    {
        // NOTE: internal フィールドは属性が無いと Unity に保存されないため、
        // ウィンドウを閉じたり再読み込みしたりしても入力を保てるよう明示する。
        [SerializeField] internal BoneWeightSourceMode SourceMode =
            BoneWeightSourceMode.Explicit;
        [SerializeField] internal List<Transform> ExplicitBones = new List<Transform>();
        [SerializeField] internal Transform DescendantsRoot;
        [SerializeField] internal bool IncludeDescendantsRoot;
        [SerializeField] internal Transform Destination;
        [SerializeField] internal float BlendRatio = 1f;
        [SerializeField] internal bool Expanded = true;
        [SerializeField] internal string SummaryLabel;

        internal BoneWeightCollapseRule Duplicate()
        {
            return new BoneWeightCollapseRule
            {
                SourceMode = SourceMode,
                // NOTE: 同じリストを共有すると片方の編集が複製元にも及ぶため、
                // 要素だけをコピーした別のリストを持たせる。
                ExplicitBones = ExplicitBones == null
                    ? new List<Transform>()
                    : new List<Transform>(ExplicitBones),
                DescendantsRoot = DescendantsRoot,
                IncludeDescendantsRoot = IncludeDescendantsRoot,
                Destination = Destination,
                BlendRatio = BlendRatio,
                Expanded = true,
                SummaryLabel = null,
            };
        }
    }

    internal sealed class BoneWeightSourceBoneInfo
    {
        internal Transform Bone;
        internal List<int> BoneIndices = new List<int>();
        internal float MovedWeight;
        internal int VertexCount;
        internal string RowLabel;
    }

    internal sealed class BoneWeightResolvedRule
    {
        internal int Number;
        internal Transform Destination;
        internal List<Transform> SourceBones = new List<Transform>();
        internal float BlendRatio;
        internal BoneWeightRuleBlockReason BlockReason;
        internal string BlockedLabel;
        internal string HeaderLabel;
    }

    internal sealed class BoneWeightTargetRule
    {
        internal BoneWeightResolvedRule Rule;
        internal int DestinationBoneIndex = -1;
        internal List<int> SourceBoneIndices = new List<int>();
        internal List<BoneWeightSourceBoneInfo> SourceBones =
            new List<BoneWeightSourceBoneInfo>();
        internal Dictionary<int, int> SourceInfoByBoneIndex =
            new Dictionary<int, int>();
        internal BoneWeightRuleBlockReason BlockReason;
        internal string BlockedLabel;
        internal int AffectedVertexCount;
        internal float MovedWeight;
        internal string HeaderLabel;
        internal bool IsApplicable => BlockReason == BoneWeightRuleBlockReason.None;
    }

    internal sealed class BoneWeightCollapseTarget
    {
        internal SkinnedMeshRenderer Renderer;
        internal Mesh SourceMesh;
        internal bool IsSelected;
        internal BoneWeightBlockReason BlockReason;
        internal string BlockedLabel;
        internal int VertexCount;
        internal int AffectedVertexCount;
        internal float MovedWeightTotal;
        internal List<BoneWeightTargetRule> Rules = new List<BoneWeightTargetRule>();
        internal GUIContent RowContent;
        internal string PathLabel;
        internal Mesh PreviousMesh;
        internal string OutputPath;
        internal string ResultLabel;
        internal bool DetailsExpanded;
    }

    internal sealed class BoneWeightCollapserPlan
    {
        internal GameObject Root;
        internal List<BoneWeightCollapseTarget> Targets =
            new List<BoneWeightCollapseTarget>();
        internal List<BoneWeightResolvedRule> Rules =
            new List<BoneWeightResolvedRule>();
        internal bool Normalize;
        internal int TotalAffectedVertexCount;
        internal float TotalMovedWeight;
        internal List<string> Warnings = new List<string>();
    }

    internal struct BoneWeightCollapseStats
    {
        internal int AffectedVertexCount;
        internal float MovedWeightTotal;
    }

    internal sealed class BoneWeightCollapseResult
    {
        internal int AppliedCount;
        internal int CreatedAssetCount;
        internal int SkippedCount;
        internal List<string> Lines = new List<string>();
        internal List<string> Errors = new List<string>();
    }
}
