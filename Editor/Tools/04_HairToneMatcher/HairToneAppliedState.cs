using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal sealed class HairToneMaterialState
    {
        internal List<(string, float)> Floats;
        internal List<(string, Vector4)> Vectors;
        internal List<(string, Texture)> Textures;
        internal List<(string, bool)> Keywords;
    }

    internal sealed class HairTonePropertyRecord
    {
        internal string Name;
        internal ShaderPropertyType Type;
        internal bool IsApplied;
        internal Color SourceColor;
        internal Color PreviousColor;
        internal float SourceFloat;
        internal float PreviousFloat;
        internal Vector4 SourceVector;
        internal Vector4 PreviousVector;
        internal Texture SourceTexture;
        internal Texture PreviousTexture;
        internal Color CurrentColor;
        internal float CurrentFloat;
        internal Vector4 CurrentVector;
        internal bool IsEdited;
        internal GUIContent RowContent;
        internal string PreviousValueLabel;
        internal string SourceValueLabel;
    }

    internal sealed class HairTonePropertyRecordGroup
    {
        internal string DisplayName;
        internal string Header;
        internal bool IsExpanded;
        internal List<HairTonePropertyRecord> Entries;
    }

    internal sealed class HairToneAppliedState
    {
        internal Material Material;
        internal HairToneShaderProfile Profile;
        internal string Header;
        internal bool IsExpanded;
        internal bool IsBaked;
        internal bool IsToneApplied;
        internal HairToneAdjustment AppliedAdjustment;
        internal HairToneAdjustment CurrentAdjustment;
        internal bool IsAdjustmentEdited;
        internal bool UseGradation;
        internal Texture GradationLut;
        internal Texture2D BakeSourceTexture;
        internal string BakedTexturePath;
        internal string MainTextureProperty;
        internal List<HairToneRendererSlot> RendererSlots;
        internal Texture2D UserMask;
        internal bool UseSubmeshUv;
        internal bool IsGradationBake;
        internal Color[] PreviewPixels;
        internal bool[] PreviewMask;
        internal Color PreviewMainColor;
        internal Texture2D AppliedPreview;
        internal Texture2D CurrentPreview;
        internal Color[] PreviewBuffer;
        internal HairToneMaterialState PreviousState;
        internal List<HairTonePropertyRecordGroup> PropertyGroups;
    }
}
