using System.Collections.Generic;
using UnityEngine;
using VRC.Dynamics;

namespace Poyo.CandyBox.PartsSetAdjuster.Editor
{
    internal enum PartsSetKind
    {
        Hair,
        Other
    }

    internal enum PartsSetMoveRange
    {
        Whole,
        Bone,
        Mesh
    }

    internal sealed class PartsSetTarget
    {
        internal PartsSetKind Kind;
        internal HumanBodyBones ReferencePart;
        internal string ReferencePartName;
        internal PartsSetMoveRange MoveRange;
        internal Renderer MoveMesh;
        internal string CoMovingLabel;
        internal SkinnedMeshRenderer ReferenceMesh;
        internal GameObject Part;
        internal GameObject AvatarRoot;
        internal Animator Animator;
        internal Transform ReferenceBone;
        internal Transform PartReferenceBone;
        internal Transform AdjustTarget;
        internal string AdjustTargetLabel;
        internal int[] ReferenceVertexIndices;
        internal Vector3 ReferenceCenter;
        internal HashSet<string> HumanoidBoneNames;
        internal string BlockedReason;
        internal bool IsValid => BlockedReason == null;
    }

    [System.Serializable]
    internal struct PartsSetOffset
    {
        // アバタールートの軸で右・上・前を表す（m）。
        [SerializeField] internal Vector3 Position;
        [SerializeField] internal Vector3 Angles;
        [SerializeField] internal float Scale;
        [SerializeField] internal Vector3 AxisScale;
        internal static PartsSetOffset Identity => new PartsSetOffset
        {
            Position = Vector3.zero,
            Angles = Vector3.zero,
            Scale = 1f,
            AxisScale = Vector3.one,
        };
    }

    internal struct PartsSetBasePose
    {
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal Vector3 LocalScale;
        internal Vector3 RootPosition;
        internal Quaternion RootRotation;
        internal bool IsValid;
    }

    internal sealed class PartsSetPenetrationResult
    {
        internal Mesh FaceMesh;
        internal int FaceTriangleCount;
        internal int Inside;
        internal int Total;
        internal List<Vector3> InsidePoints = new List<Vector3>();
        internal string Label;
        internal int PoseVersion;
    }

    internal sealed class PartsSetFitResult
    {
        internal bool Succeeded;
        internal string FailureReason;
        internal bool FromHeadBone;
        internal Vector3 WorldDelta;
        internal float Scale;
    }

    internal sealed class PartsSetColliderRow
    {
        internal VRCPhysBoneColliderBase PartCollider;
        internal VRCPhysBoneColliderBase AvatarCollider;
        internal bool Selected;
        internal string Label;
    }

    internal sealed class PartsSetColliderPlan
    {
        internal List<PartsSetColliderRow> Rows = new List<PartsSetColliderRow>();
        internal int PoseVersion;
        internal string SummaryLabel;
    }
}
