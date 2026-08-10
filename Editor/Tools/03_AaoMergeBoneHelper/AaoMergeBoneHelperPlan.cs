using System.Collections.Generic;
using UnityEngine;

namespace Poyo.CandyBox.AaoMergeBoneHelper.Editor
{
    internal enum AaoMergeBoneBlockReason
    {
        None,
        AvatarRoot,
        HumanoidBone,
        EditorOnly,
    }

    [System.Flags]
    internal enum AaoMergeBoneWarning
    {
        None = 0,
        HasComponents = 1 << 0,
        UnevenScale = 1 << 1,
        Animated = 1 << 2,
    }

    internal sealed class AaoMergeBoneNode
    {
        internal Transform Transform;
        internal int Depth;
        internal List<AaoMergeBoneNode> Children = new List<AaoMergeBoneNode>();
        internal AaoMergeBoneNode Parent;
        internal bool HasComponentInitially;
        internal bool Checked;
        internal AaoMergeBoneBlockReason BlockReason;
        internal AaoMergeBoneWarning Warnings;
        internal bool AvoidNameConflict = true;
        internal bool Expanded = true;
        internal string Label;
        internal GUIContent LabelContent;
        internal string StatusText;
        internal bool MatchesFilter = true;
        internal string AnimationPath;
        internal string TargetRelativePath;
    }

    internal sealed class AaoMergeBoneHelperPlan
    {
        internal AaoMergeBoneNode Root;
        internal List<AaoMergeBoneNode> AllNodes = new List<AaoMergeBoneNode>();
        internal GameObject AvatarRoot;
        internal int MergeableCount;
        internal bool AnimationScanned;
        internal int AddCount;
        internal int RemoveCount;
        internal string SummaryText;
        internal string ApplyText;
        internal string CountText;
        internal bool IsFiltering;
        internal int FilterMatchCount;
        internal string[] StartChoicePaths;
    }
}
