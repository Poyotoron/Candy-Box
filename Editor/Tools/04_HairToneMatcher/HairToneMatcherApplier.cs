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
        internal int BakedPixelCount;
        internal int BakedWidth;
        internal int BakedHeight;
        internal bool IsOverwrite;
        internal HairToneAppliedState AppliedState;
        internal string Error;
        internal bool Succeeded => string.IsNullOrEmpty(Error);
    }

    internal static class HairToneMatcherApplier
    {
        private const string UndoName = "Hair Tone Matcher";
        private const int MaxMaskSize = 2048;
        private const int DefaultMaskSize = 1024;
        private const int PreviewSize = 256;

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
                    result.LutPath = GetPngAssetPath(outputFolderPath,
                        baseName + "_Gradation.png", outputMode);
                    SavePng(workingLut, result.LutPath, true);
                    lutAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(result.LutPath);
                }

                Texture2D sourceTexture = GetMainTexture(
                    target, out string mainTextureProperty);
                Color[] previewPixels = null;
                bool[] previewMask = null;
                Color previewMainColor = HairToneShaderProfile.ReadMainColor(
                    target.Material, target.Profile);
                if (sourceTexture != null)
                {
                    previewPixels = HairTonePixelSampler.Read(
                        sourceTexture, PreviewSize, PreviewSize);
                    previewMask = BuildBakeMask(
                        target.RendererSlots, plan.UserMask, plan.UseSubmeshUv,
                        sourceTexture, PreviewSize, PreviewSize);
                }
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
                    result.MaskPath = GetPngAssetPath(outputFolderPath,
                        baseName + "_Mask.png", outputMode);
                    SavePng(workingMask, result.MaskPath, false);
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

                HairToneMaterialState previousState =
                    HairToneShaderProfile.CaptureState(material, target.Profile);
                var propertyRecords = new List<HairTonePropertyRecord>();
                result.CopiedPropertyCount = HairTonePropertyDiff.CopySelected(
                    plan.SourceMaterial, material, target.PropertyDiffGroups,
                    propertyRecords);
                List<HairTonePropertyRecordGroup> propertyGroups =
                    BuildPropertyRecordGroups(propertyRecords, target.Profile);

                bool useGradation = method == HairToneMethod.GradationMatch;
                if (outputMode == HairToneOutputMode.BakeTexture)
                {
                    if (sourceTexture == null)
                    {
                        throw new InvalidOperationException(
                            "メインテクスチャが見つかりません。");
                    }

                    result.TexturePath = AssetDatabase.GenerateUniqueAssetPath(
                        CombineAssetPath(outputFolderPath,
                            SanitizeFileName(sourceTexture.name) + "_Matched.png"));
                    BakeTexture(plan, target, sourceTexture,
                        result.TexturePath, method, out int bakedPixelCount,
                        out int bakedWidth, out int bakedHeight);
                    result.BakedPixelCount = bakedPixelCount;
                    result.BakedWidth = bakedWidth;
                    result.BakedHeight = bakedHeight;
                    Texture2D baked = AssetDatabase.LoadAssetAtPath<Texture2D>(result.TexturePath);
                    SetTexture(material, mainTextureProperty, baked);
                    HairToneShaderProfile.WriteNeutral(material, target.Profile);
                }
                else
                {
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

                HairToneAdjustment appliedAdjustment =
                    useGradation ? HairToneAdjustment.Neutral : target.Adjustment;
                bool isBaked = outputMode == HairToneOutputMode.BakeTexture;
                result.AppliedState = new HairToneAppliedState
                {
                    Material = material,
                    Profile = target.Profile,
                    Header = string.Format("対象: {0}", target.Label),
                    IsExpanded = true,
                    IsBaked = isBaked,
                    IsToneApplied = true,
                    AppliedAdjustment = appliedAdjustment,
                    CurrentAdjustment = appliedAdjustment,
                    UseGradation = useGradation,
                    GradationLut = lutAsset,
                    BakeSourceTexture = isBaked ? sourceTexture : null,
                    BakedTexturePath = isBaked ? result.TexturePath : null,
                    MainTextureProperty = isBaked ? mainTextureProperty : null,
                    RendererSlots = isBaked
                        ? new List<HairToneRendererSlot>(target.RendererSlots) : null,
                    UserMask = isBaked ? plan.UserMask : null,
                    UseSubmeshUv = isBaked && plan.UseSubmeshUv,
                    IsGradationBake = isBaked && useGradation,
                    PreviewPixels = previewPixels,
                    PreviewMask = previewMask,
                    PreviewMainColor = previewMainColor,
                    PreviousState = previousState,
                    PropertyGroups = propertyGroups,
                };

                EditorUtility.SetDirty(material);
                if (outputMode != HairToneOutputMode.Overwrite)
                {
                    ReplaceMaterials(target.RendererSlots, material);
                }

                return result;
            }
            catch
            {
                if (outputMode != HairToneOutputMode.Overwrite)
                {
                    DeleteGeneratedAsset(result.MaterialPath);
                    DeleteGeneratedAsset(result.TexturePath);
                    DeleteGeneratedAsset(result.LutPath);
                    DeleteGeneratedAsset(result.MaskPath);
                }

                throw;
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

        private static void BakeTexture(HairToneMatcherPlan plan,
            HairToneTarget target, Texture2D sourceTexture, string outputPath,
            HairToneMethod method, out int bakedPixelCount,
            out int bakedWidth, out int bakedHeight)
        {
            if (sourceTexture == null)
            {
                throw new InvalidOperationException("メインテクスチャが見つかりません。");
            }

            bool[] mask = BuildBakeMask(
                target.RendererSlots, plan.UserMask, plan.UseSubmeshUv,
                sourceTexture, sourceTexture.width, sourceTexture.height);
            Texture2D lut = null;
            try
            {
                if (method == HairToneMethod.GradationMatch)
                {
                    lut = HairToneGradationLut.Build(
                        plan.SourceCdf.R, plan.SourceCdf.G, plan.SourceCdf.B,
                        target.Cdf.R, target.Cdf.G, target.Cdf.B);
                }

                WriteBakedTexture(sourceTexture, mask, target.Adjustment,
                    target.Profile, lut, outputPath, out bakedPixelCount,
                    out bakedWidth, out bakedHeight);
                CopyTextureImporterSettings(
                    AssetDatabase.GetAssetPath(sourceTexture), outputPath);
            }
            finally
            {
                if (lut != null)
                {
                    UnityEngine.Object.DestroyImmediate(lut);
                }
            }
        }

        internal static void Rebake(HairToneAppliedState state,
            HairToneAdjustment adjustment, out int bakedPixelCount,
            out int width, out int height)
        {
            if (state == null || state.BakeSourceTexture == null)
            {
                throw new InvalidOperationException("元のテクスチャが見つかりません。");
            }

            if (state.IsGradationBake)
            {
                throw new InvalidOperationException(
                    "階調マッチで焼き込んだテクスチャは焼き直せません。");
            }

            if (string.IsNullOrEmpty(state.BakedTexturePath))
            {
                throw new InvalidOperationException("焼き込んだテクスチャが見つかりません。");
            }

            Texture2D sourceTexture = state.BakeSourceTexture;
            bool[] mask = BuildBakeMask(
                state.RendererSlots, state.UserMask, state.UseSubmeshUv,
                sourceTexture, sourceTexture.width, sourceTexture.height);
            WriteBakedTexture(sourceTexture, mask, adjustment, state.Profile,
                null, state.BakedTexturePath, out bakedPixelCount,
                out width, out height);
        }

        private static void WriteBakedTexture(Texture2D sourceTexture,
            bool[] mask, HairToneAdjustment adjustment,
            HairToneShaderProfile profile, Texture2D lut, string outputPath,
            out int bakedPixelCount, out int width, out int height)
        {
            width = sourceTexture.width;
            height = sourceTexture.height;
            bakedPixelCount = 0;
            Color[] pixels = HairTonePixelSampler.Read(sourceTexture, width, height);
            Texture2D output = null;
            try
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (mask == null || i >= mask.Length || !mask[i])
                    {
                        continue;
                    }

                    float alpha = pixels[i].a;
                    pixels[i] = lut != null
                        ? HairToneGradationLut.ApplyToPixel(pixels[i], lut)
                        : HairToneShaderProfile.ApplyToPixel(
                            pixels[i], adjustment, profile);
                    pixels[i].a = alpha;
                    bakedPixelCount++;
                }

                if (bakedPixelCount == 0)
                {
                    throw new InvalidOperationException(
                        "適用範囲に画素がありません。UV の絞り込みと除外マスクを確認してください。");
                }

                output = new Texture2D(width, height, TextureFormat.RGBA32, false);
                output.SetPixels(pixels);
                output.Apply(false, false);
                File.WriteAllBytes(outputPath, output.EncodeToPNG());
                AssetDatabase.ImportAsset(
                    outputPath, ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
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
            return HairToneRegionMask.Build(
                target.RendererSlots, mainPixels, existingMaskPixels, userMaskPixels,
                plan.AlphaThreshold, plan.UseSubmeshUv, width, height, out _);
        }

        private static List<HairTonePropertyRecordGroup> BuildPropertyRecordGroups(
            List<HairTonePropertyRecord> records, HairToneShaderProfile profile)
        {
            var result = new List<HairTonePropertyRecordGroup>();
            if (records == null || records.Count == 0 || profile == null)
            {
                return result;
            }

            HairTonePropertyGroup[] definitions =
                HairTonePropertyGroup.GetGroups(profile.Id);
            var entriesByGroup =
                new List<HairTonePropertyRecord>[definitions.Length];
            for (int i = 0; i < entriesByGroup.Length; i++)
            {
                entriesByGroup[i] = new List<HairTonePropertyRecord>();
            }

            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                HairTonePropertyRecord record = records[recordIndex];
                for (int groupIndex = 0; groupIndex < definitions.Length; groupIndex++)
                {
                    if (definitions[groupIndex].MatchesProperty(record.Name))
                    {
                        entriesByGroup[groupIndex].Add(record);
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

                result.Add(new HairTonePropertyRecordGroup
                {
                    DisplayName = definitions[i].DisplayName,
                    Header = string.Format("{0}  {1} 件",
                        definitions[i].DisplayName, entriesByGroup[i].Count),
                    IsExpanded = false,
                    Entries = entriesByGroup[i],
                });
            }

            return result;
        }

        private static bool[] BuildBakeMask(
            List<HairToneRendererSlot> rendererSlots, Texture2D userMask,
            bool useSubmeshUv, Texture2D mainTexture, int width, int height)
        {
            Color[] mainPixels = HairTonePixelSampler.Read(mainTexture, width, height);
            Color[] userMaskPixels = HairTonePixelSampler.Read(userMask, width, height);
            // NOTE: 半透明の毛先や既存マスクの境界を塗り残すと色に段差が出るため、
            //       焼き込みだけは統計用のアルファ閾値と既存マスクを適用しない。
            return HairToneRegionMask.Build(
                rendererSlots, mainPixels, null, userMaskPixels,
                0f, useSubmeshUv, width, height, out _);
        }

        private static Texture2D GetMainTexture(
            HairToneTarget target, out string mainTextureProperty)
        {
            mainTextureProperty = HairToneShaderProfile.ResolveMainTexPropertyName(
                target.Material);
            if (string.IsNullOrEmpty(mainTextureProperty) ||
                !target.Material.HasProperty(mainTextureProperty))
            {
                throw new InvalidOperationException("メインテクスチャが見つかりません。");
            }

            return target.Material.GetTexture(mainTextureProperty) as Texture2D;
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

        private static string GetPngAssetPath(string folderPath,
            string fileName, HairToneOutputMode outputMode)
        {
            string desiredPath = CombineAssetPath(folderPath, fileName);
            return outputMode == HairToneOutputMode.Overwrite
                ? desiredPath : AssetDatabase.GenerateUniqueAssetPath(desiredPath);
        }

        private static void SavePng(Texture2D texture, string assetPath, bool isLut)
        {
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
        }

        private static void DeleteGeneratedAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            try
            {
                if (!AssetDatabase.DeleteAsset(assetPath) && File.Exists(assetPath))
                {
                    Debug.LogWarning(string.Format(
                        "失敗した処理の生成物を削除できませんでした: {0}", assetPath));
                }
            }
            catch (Exception cleanupException)
            {
                Debug.LogException(cleanupException);
            }
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
