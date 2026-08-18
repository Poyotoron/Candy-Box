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
        DestinationNotBound,
        NoSourceBound,
        NoAffectedVertex,
    }

    internal sealed class BoneWeightSourceBoneInfo
    {
        internal Transform Bone;
        internal List<int> BoneIndices = new List<int>();
        internal float MovedWeight;
        internal int VertexCount;
        internal string RowLabel;
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
        internal int DestinationBoneIndex = -1;
        internal List<int> SourceBoneIndices = new List<int>();
        internal List<BoneWeightSourceBoneInfo> SourceBones =
            new List<BoneWeightSourceBoneInfo>();
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
        internal Transform Destination;
        internal List<Transform> SourceBones = new List<Transform>();
        internal float BlendRatio;
        internal bool Normalize;
        internal int TotalAffectedVertexCount;
        internal float TotalMovedWeight;
        internal List<string> Warnings = new List<string>();
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
