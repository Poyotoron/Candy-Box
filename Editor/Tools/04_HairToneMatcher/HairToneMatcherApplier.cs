using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal enum HairToneOutputMode
    {
        DuplicateAndReplace,
        Overwrite,
        BakeTexture,
    }

    internal sealed class HairToneApplyResult
    {
        internal string TargetLabel;
        internal string MaterialPath;
        internal string TexturePath;
        internal string LutPath;
        internal string MaskPath;
        internal int CopiedPropertyCount;
        internal bool IsOverwrite;
        internal string Error;
        internal bool Succeeded => string.IsNullOrEmpty(Error);
    }

    internal static class HairToneMatcherApplier
    {
        private const string UndoName = "Hair Tone Matcher";
        private const int MaxMaskSize = 2048;
        private const int DefaultMaskSize = 1024;

        internal static List<HairToneApplyResult> Apply(HairToneMatcherPlan plan,
            HairToneMethod method, HairToneOutputMode outputMode,
            string outputFolderPath, bool writeMask)
        {
            var results = new List<HairToneApplyResult>();
            if (plan == null || plan.Targets == null)
            {
                return results;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                for (int i = 0; i < plan.Targets.Count; i++)
                {
                    HairToneTarget target = plan.Targets[i];
                    if (!target.IsSelected || !string.IsNullOrEmpty(target.BlockedReason))
                    {
                        continue;
                    }

                    try
                    {
                        results.Add(ApplyTarget(plan, target, method, outputMode,
                            outputFolderPath, writeMask));
                    }
                    catch (Exception exception)
                    {
                        results.Add(new HairToneApplyResult
                        {
                            TargetLabel = target.Label,
                            IsOverwrite = outputMode == HairToneOutputMode.Overwrite,
                            Error = exception.Message,
                        });
                        Debug.LogException(exception);
                    }
                }

                AssetDatabase.SaveAssets();
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            return results;
        }

        private static HairToneApplyResult ApplyTarget(HairToneMatcherPlan plan,
            HairToneTarget target, HairToneMethod method, HairToneOutputMode outputMode,
            string outputFolderPath, bool writeMask)
        {
            if (target == null || target.Material == null || target.Profile == null)
            {
                throw new InvalidOperationException("改変先のマテリアルが見つかりません。");
            }

            var result = new HairToneApplyResult
            {
                TargetLabel = target.Label,
                IsOverwrite = outputMode == HairToneOutputMode.Overwrite,
            };
            string baseName = SanitizeFileName(target.Material.name);
            Texture2D workingLut = null;
            Texture2D workingMask = null;
            try
            {
                Texture2D lutAsset = null;
                if (method == HairToneMethod.GradationMatch &&
                    outputMode != HairToneOutputMode.BakeTexture)
                {
                    workingLut = HairToneGradationLut.Build(
                        plan.SourceCdf.R, plan.SourceCdf.G, plan.SourceCdf.B,
                        target.Cdf.R, target.Cdf.G, target.Cdf.B);
                    result.LutPath = SavePng(workingLut, outputFolderPath,
                        baseName + "_Gradation.png", outputMode, true);
                    lutAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(result.LutPath);
                }

                Texture2D sourceTexture = GetMainTexture(target);
                bool[] highResolutionMask = null;
                int maskWidth = sourceTexture != null
                    ? Mathf.Min(sourceTexture.width, MaxMaskSize) : DefaultMaskSize;
                int maskHeight = sourceTexture != null
                    ? Mathf.Min(sourceTexture.height, MaxMaskSize) : DefaultMaskSize;
                if (writeMask)
                {
                    highResolutionMask = BuildMask(plan, target, sourceTexture,
                        maskWidth, maskHeight);
                    workingMask = HairToneRegionMask.CreateMaskTexture(
                        highResolutionMask, maskWidth, maskHeight);
                    result.MaskPath = SavePng(workingMask, outputFolderPath,
                        baseName + "_Mask.png", outputMode, false);
                }

                Material material;
                if (outputMode == HairToneOutputMode.Overwrite)
                {
                    material = target.Material;
                    result.MaterialPath = AssetDatabase.GetAssetPath(material);
                    Undo.RecordObject(material, UndoName);
                }
                else
                {
                    material = UnityEngine.Object.Instantiate(target.Material);
                    material.name = baseName + "_Matched";
                    result.MaterialPath = AssetDatabase.GenerateUniqueAssetPath(
                        CombineAssetPath(outputFolderPath, material.name + ".mat"));
                    AssetDatabase.CreateAsset(material, result.MaterialPath);
                }

                result.CopiedPropertyCount = HairTonePropertyDiff.CopySelected(
                    plan.SourceMaterial, material, target.PropertyDiffGroups);

                if (outputMode == HairToneOutputMode.BakeTexture)
                {
                    result.TexturePath = BakeTexture(plan, target, sourceTexture,
                        outputFolderPath, method);
                    Texture2D baked = AssetDatabase.LoadAssetAtPath<Texture2D>(result.TexturePath);
                    SetTexture(material, target.Profile.MainTexProperty, baked);
                    HairToneShaderProfile.WriteNeutral(material, target.Profile);
                }
                else
                {
                    bool useGradation = method == HairToneMethod.GradationMatch;
                    HairToneShaderProfile.Write(material, target.Profile,
                        useGradation ? HairToneAdjustment.Neutral : target.Adjustment,
                        useGradation);
                    if (useGradation && lutAsset != null)
                    {
                        SetTexture(material, target.Profile.GradationTexProperty, lutAsset);
                        SetFloat(material, target.Profile.GradationStrengthProperty, 1f);
                    }
                }

                if (!string.IsNullOrEmpty(result.MaskPath))
                {
                    Texture2D maskAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(result.MaskPath);
                    SetRegionMask(material, target.Profile, maskAsset);
                }

                EditorUtility.SetDirty(material);
                if (outputMode != HairToneOutputMode.Overwrite)
                {
                    ReplaceMaterials(target.RendererSlots, material);
                }

                return result;
            }
            finally
            {
                if (workingLut != null)
                {
                    UnityEngine.Object.DestroyImmediate(workingLut);
                }

                if (workingMask != null)
                {
                    UnityEngine.Object.DestroyImmediate(workingMask);
                }
            }
        }

        private static string BakeTexture(HairToneMatcherPlan plan,
            HairToneTarget target, Texture2D sourceTexture, string outputFolderPath,
            HairToneMethod method)
        {
            if (sourceTexture == null)
            {
                throw new InvalidOperationException("メインテクスチャが見つかりません。");
            }

            int width = sourceTexture.width;
            int height = sourceTexture.height;
            Color[] pixels = HairTonePixelSampler.Read(sourceTexture, width, height);
            bool[] mask = BuildMask(plan, target, sourceTexture, width, height);
            Texture2D lut = null;
            Texture2D output = null;
            try
            {
                if (method == HairToneMethod.GradationMatch)
                {
                    lut = HairToneGradationLut.Build(
                        plan.SourceCdf.R, plan.SourceCdf.G, plan.SourceCdf.B,
                        target.Cdf.R, target.Cdf.G, target.Cdf.B);
                }

                for (int i = 0; i < pixels.Length; i++)
                {
                    if (mask == null || i >= mask.Length || !mask[i])
                    {
                        continue;
                    }

                    float alpha = pixels[i].a;
                    pixels[i] = method == HairToneMethod.GradationMatch
                        ? HairToneGradationLut.ApplyToPixel(pixels[i], lut)
                        : HairToneShaderProfile.ApplyToPixel(
                            pixels[i], target.Adjustment, target.Profile);
                    pixels[i].a = alpha;
                }

                output = new Texture2D(width, height, TextureFormat.RGBA32, false);
                output.SetPixels(pixels);
                output.Apply(false, false);
                string path = AssetDatabase.GenerateUniqueAssetPath(
                    CombineAssetPath(outputFolderPath,
                        SanitizeFileName(sourceTexture.name) + "_Matched.png"));
                File.WriteAllBytes(path, output.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                CopyTextureImporterSettings(AssetDatabase.GetAssetPath(sourceTexture), path);
                return path;
            }
            finally
            {
                if (lut != null)
                {
                    UnityEngine.Object.DestroyImmediate(lut);
                }

                if (output != null)
                {
                    UnityEngine.Object.DestroyImmediate(output);
                }
            }
        }

        private static bool[] BuildMask(HairToneMatcherPlan plan, HairToneTarget target,
            Texture2D mainTexture, int width, int height)
        {
            Color[] mainPixels = HairTonePixelSampler.Read(mainTexture, width, height);
            Color[] existingMaskPixels = null;
            if (!string.IsNullOrEmpty(target.Profile.RegionMaskProperty) &&
                target.Material.HasProperty(target.Profile.RegionMaskProperty))
            {
                existingMaskPixels = HairTonePixelSampler.Read(
                    target.Material.GetTexture(target.Profile.RegionMaskProperty), width, height);
            }

            Color[] userMaskPixels = HairTonePixelSampler.Read(plan.UserMask, width, height);
            HairToneRendererSlot representative = target.RendererSlots != null &&
                target.RendererSlots.Count > 0 ? target.RendererSlots[0] : null;
            return HairToneRegionMask.Build(
                representative != null ? representative.Renderer : null,
                representative != null ? representative.MaterialSlot : 0,
                mainPixels, existingMaskPixels, userMaskPixels,
                plan.AlphaThreshold, plan.UseSubmeshUv, width, height, out _);
        }

        private static Texture2D GetMainTexture(HairToneTarget target)
        {
            return target.Material.GetTexture(target.Profile.MainTexProperty) as Texture2D;
        }

        private static void CopyTextureImporterSettings(string fromPath, string toPath)
        {
            var from = AssetImporter.GetAtPath(fromPath) as TextureImporter;
            var to = AssetImporter.GetAtPath(toPath) as TextureImporter;
            if (from == null || to == null)
            {
                return;
            }

            var settings = new TextureImporterSettings();
            from.ReadTextureSettings(settings);
            to.SetTextureSettings(settings);
            to.SetPlatformTextureSettings(from.GetDefaultPlatformTextureSettings());
            to.SaveAndReimport();
        }

        private static void ReplaceMaterials(List<HairToneRendererSlot> rendererSlots,
            Material material)
        {
            if (rendererSlots == null || rendererSlots.Count == 0)
            {
                throw new InvalidOperationException("改変先の Renderer が見つかりません。");
            }

            for (int i = 0; i < rendererSlots.Count; i++)
            {
                HairToneRendererSlot pair = rendererSlots[i];
                if (pair == null || pair.Renderer == null)
                {
                    throw new InvalidOperationException("改変先の Renderer が見つかりません。");
                }

                Material[] materials = pair.Renderer.sharedMaterials;
                if (pair.MaterialSlot < 0 || pair.MaterialSlot >= materials.Length)
                {
                    throw new InvalidOperationException("改変先のマテリアルスロットが見つかりません。");
                }

                Undo.RecordObject(pair.Renderer, UndoName);
                materials[pair.MaterialSlot] = material;
                pair.Renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(pair.Renderer);
                EditorUtility.SetDirty(pair.Renderer);
            }
        }

        private static string SavePng(Texture2D texture, string folderPath,
            string fileName, HairToneOutputMode outputMode, bool isLut)
        {
            string desiredPath = CombineAssetPath(folderPath, fileName);
            string assetPath = outputMode == HairToneOutputMode.Overwrite
                ? desiredPath : AssetDatabase.GenerateUniqueAssetPath(desiredPath);
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = isLut;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return assetPath;
        }

        private static void SetRegionMask(Material material,
            HairToneShaderProfile profile, Texture2D mask)
        {
            if (mask == null || string.IsNullOrEmpty(profile.RegionMaskProperty) ||
                !material.HasProperty(profile.RegionMaskProperty))
            {
                return;
            }

            material.SetTexture(profile.RegionMaskProperty, mask);
            material.SetTextureScale(profile.RegionMaskProperty, Vector2.one);
            material.SetTextureOffset(profile.RegionMaskProperty, Vector2.zero);
            SetFloat(material, profile.RegionMaskUvProperty, 0f);
        }

        private static string CombineAssetPath(string folder, string fileName)
        {
            return folder.TrimEnd('/', '\\') + "/" + fileName;
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (!string.IsNullOrEmpty(property) && material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (!string.IsNullOrEmpty(property) && material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static string SanitizeFileName(string value)
        {
            char[] characters = value.ToCharArray();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < characters.Length; i++)
            {
                if (Array.IndexOf(invalid, characters[i]) >= 0)
                {
                    characters[i] = '_';
                }
            }

            return new string(characters);
        }
    }
}
