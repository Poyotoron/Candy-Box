using UnityEngine;
using UnityEngine.Rendering;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal struct HairToneAdjustment
    {
        internal float Hue;
        internal float Saturation;
        internal float Value;
        internal float Gamma;

        internal static HairToneAdjustment Neutral
        {
            get
            {
                return new HairToneAdjustment
                {
                    Hue = 0f,
                    Saturation = 1f,
                    Value = 1f,
                    Gamma = 1f,
                };
            }
        }
    }

    internal sealed class HairToneShaderProfile
    {
        private const string LilToonId = "liltoon";
        private const string PoiyomiId = "poiyomi";
        private const string LilAdjustVector = "_MainTexHSVG";
        private const string PoiHue = "_MainHueShift";
        private const string PoiSaturation = "_Saturation";
        private const string PoiBrightness = "_MainBrightness";
        private const string PoiGamma = "_MainGamma";

        private static readonly string[] EmptyStrings = new string[0];
        private static readonly (string, float)[] EmptyFixed = new (string, float)[0];

        private static readonly HairToneShaderProfile[] Profiles =
        {
            new HairToneShaderProfile(
                LilToonId, "lilToon", "_lilToonVersion", "_MainTex", "_Color",
                "_MainGradationTex", "_MainGradationStrength",
                "_MainColorAdjustMask", null, null,
                LilAdjustVector, null, null, null, null,
                EmptyStrings, EmptyStrings, EmptyStrings, EmptyFixed,
                new[] { "_MatCapTex", "_MatCap2ndTex", "_ReflectionCubeTex", "_EmissionGradTex", "_Ramp" },
                new[] { "_UseMain2ndTex", "_UseMain3rdTex" }),
            new HairToneShaderProfile(
                PoiyomiId, "Poiyomi", "_MainColorAdjustToggle", "_MainTex", "_Color",
                "_MainGradationTex", "_MainGradationStrength",
                "_MainColorAdjustTexture", "_MainColorAdjustTextureUV", "_ShaderOptimizerEnabled",
                null, PoiHue, PoiSaturation, PoiBrightness, PoiGamma,
                new[] { "_MainColorAdjustToggle", "_MainHueShiftToggle" },
                new[] { "_ColorGradingToggle" },
                new[] { "COLOR_GRADING_HDR" },
                new[]
                {
                    ("_MainHueShiftColorSpace", 1f),
                    ("_MainHueShiftSelectOrShift", 1f),
                    ("_MainHueShiftReplace", 1f),
                },
                new[] { "_Matcap", "_Matcap2", "_Matcap3", "_Matcap4", "_CubeMap", "_ToonRamp" },
                EmptyStrings),
        };

        internal readonly string Id;
        internal readonly string DisplayName;
        internal readonly string IdentifyProperty;
        internal readonly string MainTexProperty;
        internal readonly string MainColorProperty;
        internal readonly string GradationTexProperty;
        internal readonly string GradationStrengthProperty;
        internal readonly string RegionMaskProperty;
        internal readonly string RegionMaskUvProperty;
        internal readonly string LockProperty;
        internal readonly string AdjustVectorProperty;
        internal readonly string HueProperty;
        internal readonly string SaturationProperty;
        internal readonly string BrightnessProperty;
        internal readonly string GammaProperty;
        internal readonly string[] EnableProperties;
        internal readonly string[] GradationEnableProperties;
        internal readonly string[] EnableKeywords;
        internal readonly (string, float)[] FixedProperties;
        internal readonly string[] CopyableTextureProperties;
        internal readonly string[] LayeredTexProperties;

        private HairToneShaderProfile(
            string id, string displayName, string identifyProperty,
            string mainTexProperty, string mainColorProperty,
            string gradationTexProperty, string gradationStrengthProperty,
            string regionMaskProperty, string regionMaskUvProperty, string lockProperty,
            string adjustVectorProperty, string hueProperty, string saturationProperty,
            string brightnessProperty, string gammaProperty,
            string[] enableProperties, string[] gradationEnableProperties,
            string[] enableKeywords, (string, float)[] fixedProperties,
            string[] copyableTextureProperties, string[] layeredTexProperties)
        {
            Id = id;
            DisplayName = displayName;
            IdentifyProperty = identifyProperty;
            MainTexProperty = mainTexProperty;
            MainColorProperty = mainColorProperty;
            GradationTexProperty = gradationTexProperty;
            GradationStrengthProperty = gradationStrengthProperty;
            RegionMaskProperty = regionMaskProperty;
            RegionMaskUvProperty = regionMaskUvProperty;
            LockProperty = lockProperty;
            AdjustVectorProperty = adjustVectorProperty;
            HueProperty = hueProperty;
            SaturationProperty = saturationProperty;
            BrightnessProperty = brightnessProperty;
            GammaProperty = gammaProperty;
            EnableProperties = enableProperties;
            GradationEnableProperties = gradationEnableProperties;
            EnableKeywords = enableKeywords;
            FixedProperties = fixedProperties;
            CopyableTextureProperties = copyableTextureProperties;
            LayeredTexProperties = layeredTexProperties;
        }

        internal static HairToneShaderProfile Resolve(Material material)
        {
            if (material == null)
            {
                return null;
            }

            for (int i = 0; i < Profiles.Length; i++)
            {
                if (material.HasProperty(Profiles[i].IdentifyProperty))
                {
                    return Profiles[i];
                }
            }

            return null;
        }

        internal static string ResolveMainTexPropertyName(Material material)
        {
            if (material == null)
            {
                return null;
            }

            HairToneShaderProfile profile = Resolve(material);
            if (profile != null && material.HasProperty(profile.MainTexProperty))
            {
                return profile.MainTexProperty;
            }

            Shader shader = material.shader;
            if (shader != null)
            {
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    if (shader.GetPropertyType(i) == ShaderPropertyType.Texture &&
                        (shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainTexture) != 0)
                    {
                        return shader.GetPropertyName(i);
                    }
                }
            }

            return material.HasProperty("_MainTex") ? "_MainTex" : null;
        }

        internal static bool IsLocked(Material material, HairToneShaderProfile profile)
        {
            return material != null && profile != null &&
                !string.IsNullOrEmpty(profile.LockProperty) &&
                material.HasProperty(profile.LockProperty) &&
                material.GetFloat(profile.LockProperty) >= 0.5f;
        }

        internal static void Write(Material material, HairToneShaderProfile profile,
            HairToneAdjustment value, bool useGradation)
        {
            if (material == null || profile == null)
            {
                return;
            }

            if (profile.Id == LilToonId)
            {
                SetVector(material, LilAdjustVector,
                    new Vector4(value.Hue, value.Saturation, value.Value, value.Gamma));
            }
            else if (profile.Id == PoiyomiId)
            {
                SetFloat(material, PoiHue, value.Hue >= 0f ? value.Hue : value.Hue + 1f);
                SetFloat(material, PoiSaturation, value.Saturation - 1f);
                SetFloat(material, PoiBrightness, value.Value - 1f);
                SetFloat(material, PoiGamma, value.Gamma);
            }

            SetProperties(material, profile.EnableProperties, 1f);
            if (useGradation)
            {
                SetProperties(material, profile.GradationEnableProperties, 1f);
            }

            for (int i = 0; i < profile.FixedProperties.Length; i++)
            {
                SetFloat(material, profile.FixedProperties[i].Item1,
                    profile.FixedProperties[i].Item2);
            }

            for (int i = 0; i < profile.EnableKeywords.Length; i++)
            {
                material.EnableKeyword(profile.EnableKeywords[i]);
            }
        }

        internal static HairToneAdjustment Read(Material material, HairToneShaderProfile profile)
        {
            HairToneAdjustment neutral = HairToneAdjustment.Neutral;
            if (material == null || profile == null || !IsEnabled(material, profile))
            {
                return neutral;
            }

            if (profile.Id == LilToonId && material.HasProperty(LilAdjustVector))
            {
                Vector4 value = material.GetVector(LilAdjustVector);
                return new HairToneAdjustment
                {
                    Hue = value.x,
                    Saturation = value.y,
                    Value = value.z,
                    Gamma = value.w,
                };
            }

            if (profile.Id == PoiyomiId)
            {
                return new HairToneAdjustment
                {
                    Hue = GetFloat(material, PoiHue, 0f),
                    Saturation = GetFloat(material, PoiSaturation, 0f) + 1f,
                    Value = GetFloat(material, PoiBrightness, 0f) + 1f,
                    Gamma = GetFloat(material, PoiGamma, 1f),
                };
            }

            return neutral;
        }

        internal static Color ReadMainColor(Material material, HairToneShaderProfile profile)
        {
            if (material == null)
            {
                return Color.white;
            }

            string property = profile != null ? profile.MainColorProperty : "_Color";
            Color color = !string.IsNullOrEmpty(property) && material.HasProperty(property)
                ? material.GetColor(property)
                : Color.white;
            color.a = 1f;
            return color;
        }

        internal static Color MultiplyMainColor(Color color, Color mainColor)
        {
            color.r *= mainColor.r;
            color.g *= mainColor.g;
            color.b *= mainColor.b;
            return color;
        }

        internal static void WriteNeutral(Material material, HairToneShaderProfile profile)
        {
            if (material == null || profile == null)
            {
                return;
            }

            HairToneAdjustment neutral = HairToneAdjustment.Neutral;
            if (profile.Id == LilToonId)
            {
                SetVector(material, LilAdjustVector,
                    new Vector4(neutral.Hue, neutral.Saturation, neutral.Value, neutral.Gamma));
            }
            else if (profile.Id == PoiyomiId)
            {
                SetFloat(material, PoiHue, 0f);
                SetFloat(material, PoiSaturation, 0f);
                SetFloat(material, PoiBrightness, 0f);
                SetFloat(material, PoiGamma, 1f);
            }

            SetProperties(material, profile.EnableProperties, 0f);
            SetProperties(material, profile.GradationEnableProperties, 0f);
            for (int i = 0; i < profile.EnableKeywords.Length; i++)
            {
                material.DisableKeyword(profile.EnableKeywords[i]);
            }
        }

        internal static Color ApplyToPixel(Color color, HairToneAdjustment value,
            HairToneShaderProfile profile)
        {
            float alpha = color.a;
            if (profile != null && profile.Id == PoiyomiId)
            {
                Color.RGBToHSV(color, out float hue, out float saturation, out float brightness);
                hue = Mathf.Repeat(hue + value.Hue, 1f);
                Color rgb = Color.HSVToRGB(hue, saturation, brightness, true);
                rgb.r = Mathf.Pow(Mathf.Abs(rgb.r), value.Gamma);
                rgb.g = Mathf.Pow(Mathf.Abs(rgb.g), value.Gamma);
                rgb.b = Mathf.Pow(Mathf.Abs(rgb.b), value.Gamma);
                float luminance = rgb.r * 0.3f + rgb.g * 0.59f + rgb.b * 0.11f;
                float saturationAmount = -(value.Saturation - 1f);
                rgb.r = Mathf.LerpUnclamped(rgb.r, luminance, saturationAmount);
                rgb.g = Mathf.LerpUnclamped(rgb.g, luminance, saturationAmount);
                rgb.b = Mathf.LerpUnclamped(rgb.b, luminance, saturationAmount);
                rgb.r = Mathf.Clamp01(rgb.r * value.Value);
                rgb.g = Mathf.Clamp01(rgb.g * value.Value);
                rgb.b = Mathf.Clamp01(rgb.b * value.Value);
                rgb.a = alpha;
                return rgb;
            }

            Color adjusted = color;
            adjusted.r = Mathf.Pow(Mathf.Abs(adjusted.r), value.Gamma);
            adjusted.g = Mathf.Pow(Mathf.Abs(adjusted.g), value.Gamma);
            adjusted.b = Mathf.Pow(Mathf.Abs(adjusted.b), value.Gamma);
            Color.RGBToHSV(adjusted, out float h, out float s, out float v);
            adjusted = Color.HSVToRGB(
                Mathf.Repeat(h + value.Hue, 1f),
                Mathf.Clamp01(s * value.Saturation),
                Mathf.Clamp01(v * value.Value), true);
            adjusted.a = alpha;
            return adjusted;
        }

        private static bool IsEnabled(Material material, HairToneShaderProfile profile)
        {
            for (int i = 0; i < profile.EnableProperties.Length; i++)
            {
                string property = profile.EnableProperties[i];
                if (!material.HasProperty(property) || material.GetFloat(property) < 0.5f)
                {
                    return false;
                }
            }

            return true;
        }

        private static void SetProperties(Material material, string[] properties, float value)
        {
            for (int i = 0; i < properties.Length; i++)
            {
                SetFloat(material, properties[i], value);
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (!string.IsNullOrEmpty(property) && material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void SetVector(Material material, string property, Vector4 value)
        {
            if (!string.IsNullOrEmpty(property) && material.HasProperty(property))
            {
                material.SetVector(property, value);
            }
        }

        private static float GetFloat(Material material, string property, float fallback)
        {
            return material.HasProperty(property) ? material.GetFloat(property) : fallback;
        }
    }
}
