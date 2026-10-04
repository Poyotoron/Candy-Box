using System.Collections.Generic;
using UnityEngine;
using VRC.Dynamics;

namespace Poyo.CandyBox.HairSetAdjuster.Editor
{
    internal sealed class HairSetTarget
    {
        internal SkinnedMeshRenderer Face;
        internal GameObject Hair;
        internal GameObject AvatarRoot;
        internal Animator Animator;
        internal Transform HeadBone;
        internal Transform HairHeadBone;
        internal Transform AdjustTarget;
        internal string AdjustTargetLabel;
        internal int[] HeadVertexIndices;
        internal Vector3 HeadCenter;
        internal HashSet<string> HumanoidBoneNames;
        internal string BlockedReason;
        internal bool IsValid => BlockedReason == null;
    }

    [System.Serializable]
    internal struct HairSetOffset
    {
        // アバタールートの軸で右・上・前を表す（m）。
        [SerializeField] internal Vector3 Position;
        [SerializeField] internal Vector3 Angles;
        [SerializeField] internal float Scale;
        [SerializeField] internal Vector3 AxisScale;
        internal static HairSetOffset Identity => new HairSetOffset
        {
            Position = Vector3.zero, Angles = Vector3.zero, Scale = 1f, AxisScale = Vector3.one,
        };
    }

    internal struct HairSetBasePose
    {
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal Vector3 LocalScale;
        internal Vector3 RootPosition;
        internal Quaternion RootRotation;
        internal bool IsValid;
    }

    internal sealed class HairSetPenetrationResult
    {
        internal int Inside;
        internal int Total;
        internal List<Vector3> InsidePoints = new List<Vector3>();
        internal string Label;
        internal int PoseVersion;
    }

    internal sealed class HairSetColliderRow
    {
        internal VRCPhysBoneColliderBase HairCollider;
        internal VRCPhysBoneColliderBase AvatarCollider;
        internal bool Selected;
        internal string Label;
    }

    internal sealed class HairSetColliderPlan
    {
        internal List<HairSetColliderRow> Rows = new List<HairSetColliderRow>();
        internal int PoseVersion;
        internal string SummaryLabel;
    }
}
