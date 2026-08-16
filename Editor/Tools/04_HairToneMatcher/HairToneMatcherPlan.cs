using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal enum HairToneMethod
    {
        /// <summary>色相・彩度・明度の補正値を算出する。</summary>
        ToneAdjust,

        /// <summary>階調の分布を合わせる LUT を作る。</summary>
        GradationMatch,
    }

    internal enum HairToneSampling
    {
        /// <summary>範囲全体の統計を使う。</summary>
        Statistics,

        /// <summary>テクスチャから拾った 2 点を使う。</summary>
        Picker,
    }

    internal struct HairToneCdf
    {
        internal float[] R;
        internal float[] G;
        internal float[] B;
    }

    [Serializable]
    internal sealed class HairToneSourceInput
    {
        [SerializeField] internal Material Material;
        [SerializeField] internal Renderer Renderer;
        [SerializeField] internal int MaterialSlot;
    }

    [Serializable]
    internal sealed class HairToneRendererSlot
    {
        [SerializeField] internal Renderer Renderer;
        [SerializeField] internal int MaterialSlot;
    }

    internal sealed class HairToneTarget
    {
        internal Material Material;
        internal List<HairToneRendererSlot> RendererSlots = new List<HairToneRendererSlot>();
        internal bool IsSelected;
        internal string Label;
        internal string SelectedHeader;
        internal GUIContent RowContent;
        internal string BlockedReason;
        internal HairToneShaderProfile Profile;
        internal Color MainColor;
        internal HairToneStats Stats;
        internal Color[] Pixels;
        internal bool[] DestinationMask;
        internal HairToneMaskCounts MaskCounts;
        internal HairToneCdf Cdf;
        internal HairToneAdjustment SuggestedAdjustment;
        internal HairToneAdjustment Adjustment;
        internal bool IsAdjustmentEdited;
        internal List<HairTonePropertyDiffGroup> PropertyDiffGroups;
        internal int IdenticalPropertyCount;
        internal string PropertyDiffHeader;
        internal string AdjustmentSummary;
    }

    internal sealed class HairToneMatcherPlan
    {
        internal List<HairToneSourceInput> Sources;
        internal List<HairToneTarget> Targets;
        internal Material SourceMaterial;
        internal HairToneShaderProfile SourceProfile;
        internal HairToneStats SourceStats;
        internal HairToneMaskCounts SourceMaskCounts;
        internal bool[] SourceMask;
        internal Color[] SourcePixels;
        internal Color[] SourcePreviewPixels;
        internal bool[] SourcePreviewMask;
        internal HairToneCdf SourceCdf;
        internal string[] Warnings;
        internal string SourcePreviewLabel;
        internal Texture2D UserMask;
        internal float AlphaThreshold;
        internal bool UseSubmeshUv;
    }
}
