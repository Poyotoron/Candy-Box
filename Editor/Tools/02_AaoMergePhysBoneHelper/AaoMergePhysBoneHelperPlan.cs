using System.Collections.Generic;
using Anatawa12.AvatarOptimizer;
using UnityEngine;
using VRC.Dynamics;

namespace Poyo.CandyBox.AaoMergePhysBoneHelper.Editor
{
    internal sealed class AaoMergePhysBoneSource
    {
        internal VRCPhysBoneBase PhysBone;
        internal GameObject GameObject;
        internal string Label;
        internal GUIContent LabelContent;
        internal int ChainLength;
        internal string ChainLengthText;
    }

    internal sealed class AaoMergePhysBoneValue
    {
        internal int SourceIndex;
        internal float Float;
        internal Vector3 Vector;
        internal int Int;
        internal int FilterInt;
        internal AnimationCurve Curve;
        internal AnimationCurve CurveY;
        internal AnimationCurve CurveZ;
        internal Object[] ObjectReferences;
        internal string DisplayText;
    }

    internal sealed class AaoMergePhysBoneSuggestion
    {
        internal AaoMergePhysBoneMetric Metric = AaoMergePhysBoneMetric.Mode;
        internal float Float;
        internal Vector3 Vector;
        internal int Int;
        internal int FilterInt;
        internal AnimationCurve Curve;
        internal AnimationCurve CurveY;
        internal AnimationCurve CurveZ;
        internal string Warning;
        internal string DisplayText;
        internal bool NormalizePending;
    }

    internal sealed class AaoMergePhysBonePropertyPlan
    {
        internal AaoMergePhysBoneProperty Property;
        internal List<AaoMergePhysBoneValue> Values = new List<AaoMergePhysBoneValue>();
        internal bool HasDifference;
        internal int OutlierSourceIndex = -1;
        internal string CurrentOverrideText;
        internal bool Blocked;
        internal string BlockedReason;
        internal string BlockedDisplayText;
        internal AaoMergePhysBoneSuggestion Suggestion;
        internal bool Selected = true;
        internal bool Expanded;
        internal bool Edited;
        internal bool ChainLengthDiffers;
        internal string[] EnumNames;
        internal string[] EnumDisplayNames;
        internal string StatisticsText;
        internal string HeaderText;
        internal string OutlierText;
    }

    internal sealed class AaoMergePhysBoneHelperPlan
    {
        internal MergePhysBone MergePhysBone;
        internal List<AaoMergePhysBoneSource> Sources = new List<AaoMergePhysBoneSource>();
        internal List<AaoMergePhysBonePropertyPlan> Differing =
            new List<AaoMergePhysBonePropertyPlan>();
        internal List<AaoMergePhysBonePropertyPlan> Identical =
            new List<AaoMergePhysBonePropertyPlan>();
        internal List<AaoMergePhysBonePropertyPlan> Blocked =
            new List<AaoMergePhysBonePropertyPlan>();
        internal int MissingPropertyCount;
        internal bool ChainLengthDiffers;
        internal bool DifferingExpanded = true;
        internal bool BlockedExpanded = true;
        internal bool IdenticalExpanded;
        internal string SourcesHeaderText;
        internal string DifferingHeaderText;
        internal string BlockedHeaderText;
        internal string IdenticalHeaderText;
        internal string MissingPropertyText;
        internal string ApplyText;
    }
}
