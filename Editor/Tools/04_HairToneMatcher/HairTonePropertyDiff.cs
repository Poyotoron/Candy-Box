using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal sealed class HairTonePropertyDiffEntry
    {
        internal string Name;
        internal string DisplayName;
        internal ShaderPropertyType Type;
        internal string SourceValueLabel;
        internal string DestinationValueLabel;
        internal string SearchText;
        internal bool IsSelected;
        internal bool IsVisible = true;
    }

    internal sealed class HairTonePropertyDiffGroup
    {
        internal string DisplayName;
        internal string Header;
        internal List<HairTonePropertyDiffEntry> Entries;
        internal bool IsExpanded;
    }

    internal static class HairTonePropertyDiff
    {
        internal static List<HairTonePropertyDiffGroup> Collect(Material source,
            Material destination, HairToneShaderProfile profile, out int identicalCount)
        {
            var result = new List<HairTonePropertyDiffGroup>();
            HairTonePropertyGroup[] definitions = profile != null
                ? HairTonePropertyGroup.GetGroups(profile.Id)
                : new HairTonePropertyGroup[0];
            var entriesByGroup = new List<HairTonePropertyDiffEntry>[definitions.Length];
            for (int i = 0; i < entriesByGroup.Length; i++)
            {
                entriesByGroup[i] = new List<HairTonePropertyDiffEntry>();
            }
            identicalCount = 0;
            if (source == null || destination == null || destination.shader == null || profile == null)
            {
                return result;
            }

            Shader shader = destination.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                ShaderPropertyType type = shader.GetPropertyType(i);
                if (!source.HasProperty(name) || IsExcluded(name, profile) ||
                    !IsSupported(type, name, profile))
                {
                    continue;
                }

                string destinationValue;
                string sourceValue;
                bool equal = ValuesEqual(source, destination, name, type,
                    out sourceValue, out destinationValue);
                if (equal)
                {
                    identicalCount++;
                    continue;
                }

                string description = shader.GetPropertyDescription(i);
                if (string.IsNullOrEmpty(description))
                {
                    description = name;
                }

                var entry = new HairTonePropertyDiffEntry
                {
                    Name = name,
                    DisplayName = description,
                    Type = type,
                    SourceValueLabel = sourceValue,
                    DestinationValueLabel = destinationValue,
                    SearchText = (name + "\n" + description).ToLowerInvariant(),
                    IsSelected = false,
                };
                for (int groupIndex = 0; groupIndex < definitions.Length; groupIndex++)
                {
                    if (definitions[groupIndex].MatchesProperty(name))
                    {
                        entriesByGroup[groupIndex].Add(entry);
                        break;
                    }
                }
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                if (entriesByGroup[i].Count == 0)
                {
                    continue;
                }

                result.Add(new HairTonePropertyDiffGroup
                {
                    DisplayName = definitions[i].DisplayName,
                    Header = string.Format("{0}  差分 {1} 件",
                        definitions[i].DisplayName, entriesByGroup[i].Count),
                    Entries = entriesByGroup[i],
                    IsExpanded = false,
                });
            }

            return result;
        }

        internal static int CopySelected(Material source, Material destination,
            List<HairTonePropertyDiffGroup> groups)
        {
            if (source == null || destination == null || groups == null)
            {
                return 0;
            }

            int copied = 0;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                List<HairTonePropertyDiffEntry> entries = groups[groupIndex].Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    HairTonePropertyDiffEntry entry = entries[i];
                    if (!entry.IsSelected || !source.HasProperty(entry.Name) ||
                        !destination.HasProperty(entry.Name))
                    {
                        continue;
                    }

                    switch (entry.Type)
                    {
                        case ShaderPropertyType.Color:
                            destination.SetColor(entry.Name, source.GetColor(entry.Name));
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            destination.SetFloat(entry.Name, source.GetFloat(entry.Name));
                            break;
                        case ShaderPropertyType.Vector:
                            destination.SetVector(entry.Name, source.GetVector(entry.Name));
                            break;
                        case ShaderPropertyType.Texture:
                            destination.SetTexture(entry.Name, source.GetTexture(entry.Name));
                            break;
                        default:
                            continue;
                    }

                    copied++;
                }
            }

            return copied;
        }

        private static bool IsSupported(ShaderPropertyType type, string name,
            HairToneShaderProfile profile)
        {
            if (type == ShaderPropertyType.Color || type == ShaderPropertyType.Float ||
                type == ShaderPropertyType.Range || type == ShaderPropertyType.Vector)
            {
                return true;
            }

            return type == ShaderPropertyType.Texture &&
                Array.IndexOf(profile.CopyableTextureProperties, name) >= 0;
        }

        private static bool IsExcluded(string name, HairToneShaderProfile profile)
        {
            if (Equal(name, profile.MainTexProperty) ||
                Equal(name, profile.MainColorProperty) ||
                Equal(name, profile.AdjustVectorProperty) ||
                Equal(name, profile.HueProperty) ||
                Equal(name, profile.SaturationProperty) ||
                Equal(name, profile.BrightnessProperty) ||
                Equal(name, profile.GammaProperty) ||
                Equal(name, profile.GradationTexProperty) ||
                Equal(name, profile.GradationStrengthProperty) ||
                Equal(name, profile.RegionMaskProperty) ||
                Equal(name, profile.RegionMaskUvProperty) ||
                Equal(name, profile.LockProperty))
            {
                return true;
            }

            if (Array.IndexOf(profile.EnableProperties, name) >= 0 ||
                Array.IndexOf(profile.GradationEnableProperties, name) >= 0)
            {
                return true;
            }

            for (int i = 0; i < profile.FixedProperties.Length; i++)
            {
                if (profile.FixedProperties[i].Item1 == name)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ValuesEqual(Material source, Material destination,
            string name, ShaderPropertyType type, out string sourceLabel,
            out string destinationLabel)
        {
            switch (type)
            {
                case ShaderPropertyType.Color:
                    Color sourceColor = source.GetColor(name);
                    Color destinationColor = destination.GetColor(name);
                    sourceLabel = sourceColor.ToString("F3");
                    destinationLabel = destinationColor.ToString("F3");
                    return sourceColor == destinationColor;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    float sourceFloat = source.GetFloat(name);
                    float destinationFloat = destination.GetFloat(name);
                    sourceLabel = sourceFloat.ToString("G6");
                    destinationLabel = destinationFloat.ToString("G6");
                    return sourceFloat == destinationFloat;
                case ShaderPropertyType.Vector:
                    Vector4 sourceVector = source.GetVector(name);
                    Vector4 destinationVector = destination.GetVector(name);
                    sourceLabel = sourceVector.ToString("F3");
                    destinationLabel = destinationVector.ToString("F3");
                    return sourceVector == destinationVector;
                case ShaderPropertyType.Texture:
                    Texture sourceTexture = source.GetTexture(name);
                    Texture destinationTexture = destination.GetTexture(name);
                    sourceLabel = sourceTexture != null ? sourceTexture.name : "なし";
                    destinationLabel = destinationTexture != null ? destinationTexture.name : "なし";
                    return sourceTexture == destinationTexture;
                default:
                    sourceLabel = string.Empty;
                    destinationLabel = string.Empty;
                    return true;
            }
        }

        private static bool Equal(string left, string right)
        {
            return !string.IsNullOrEmpty(right) &&
                string.Equals(left, right, StringComparison.Ordinal);
        }
    }
}
