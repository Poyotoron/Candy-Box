using System;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal sealed class HairTonePropertyGroup
    {
        internal readonly string DisplayName;
        internal readonly string[] Matches;

        private HairTonePropertyGroup(string displayName, params string[] matches)
        {
            DisplayName = displayName;
            Matches = matches;
        }

        private static readonly HairTonePropertyGroup[] LilToonGroups =
        {
            new HairTonePropertyGroup("影", "_Shadow"),
            new HairTonePropertyGroup("発光", "_Emission"),
            new HairTonePropertyGroup("ノーマルマップ", "_Bump", "_NormalMap"),
            new HairTonePropertyGroup("反射", "_Reflection", "_Smoothness", "_Metallic", "_GSAA", "_Applied"),
            new HairTonePropertyGroup("マットキャップ", "_MatCap"),
            new HairTonePropertyGroup("リムライト", "_Rim"),
            new HairTonePropertyGroup("逆光", "_Backlight"),
            new HairTonePropertyGroup("異方性反射", "_Anisotropy"),
            new HairTonePropertyGroup("ラメ", "_Glitter"),
            new HairTonePropertyGroup("アウトライン", "_Outline"),
            new HairTonePropertyGroup("距離フェード", "_DistanceFade"),
            new HairTonePropertyGroup("ディゾルブ", "_Dissolve"),
            new HairTonePropertyGroup("視差", "_Parallax"),
            new HairTonePropertyGroup("ファー", "_Fur"),
            new HairTonePropertyGroup("メインカラー 2 / 3", "_Main2nd", "_Main3rd", "_Color2nd", "_Color3rd"),
            new HairTonePropertyGroup("ライティング", "_Light", "_AsUnlit", "_VertexLight", "_MonochromeLighting"),
            new HairTonePropertyGroup("描画設定", "_Cull", "_ZWrite", "_ZTest", "_Stencil", "_Src", "_Dst", "_BlendOp", "_AlphaToMask", "_OffsetFactor", "_OffsetUnits", "_ColorMask"),
            new HairTonePropertyGroup("その他"),
        };

        private static readonly HairTonePropertyGroup[] PoiyomiGroups =
        {
            new HairTonePropertyGroup("影・ライティング", "_Shadow", "_Lighting", "_ToonRamp"),
            new HairTonePropertyGroup("発光", "_Emission"),
            new HairTonePropertyGroup("ノーマルマップ", "_Bump", "_Normal", "_Detail"),
            new HairTonePropertyGroup("反射", "_Reflection", "_CubeMap", "_Smoothness", "_Metallic"),
            new HairTonePropertyGroup("マットキャップ", "_Matcap"),
            new HairTonePropertyGroup("リムライト", "_Rim"),
            new HairTonePropertyGroup("アウトライン", "_Outline"),
            new HairTonePropertyGroup("ラメ", "_Glitter"),
            new HairTonePropertyGroup("ディゾルブ", "_Dissolve"),
            new HairTonePropertyGroup("視差", "_Parallax", "_Height"),
            new HairTonePropertyGroup("AudioLink", "_AudioLink", "_AL"),
            new HairTonePropertyGroup("描画設定", "_Cull", "_ZWrite", "_ZTest", "_Stencil", "_Src", "_Dst", "_BlendOp", "_AlphaToMask", "_RenderQueue"),
            new HairTonePropertyGroup("その他"),
        };

        internal static HairTonePropertyGroup[] GetGroups(string profileId)
        {
            return string.Equals(profileId, "poiyomi", StringComparison.Ordinal)
                ? PoiyomiGroups
                : LilToonGroups;
        }

        internal bool MatchesProperty(string name)
        {
            for (int i = 0; i < Matches.Length; i++)
            {
                if (name.Contains(Matches[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return Matches.Length == 0;
        }
    }
}
