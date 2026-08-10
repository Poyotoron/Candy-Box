using System;
using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.MaBlendshapeSyncHelper.Editor
{
    internal static class MaBlendshapeSyncHelperScanner
    {
        private const string MissingSourceError =
            "素体メッシュを 1 つ以上指定してください。";
        private const string MissingCostumeError = "衣装ルートを指定してください。";
        private const string SourceOutsideAvatarError =
            "素体メッシュがアバターの中にありません。アバターの配下に置いてから実行してください。";
        private const string DifferentSourceAvatarError =
            "指定した素体メッシュが別々のアバターにあります。同じアバターの中で実行してください。";
        private const string DifferentCostumeAvatarError =
            "素体メッシュと衣装ルートが別のアバターにあります。同じアバターの中で実行してください。";

        internal static MaBlendshapeSyncPlan Scan(
            IReadOnlyList<SkinnedMeshRenderer> sourceRenderers,
            GameObject costumeRoot,
            bool includeInactive,
            out string error)
        {
            error = null;
            try
            {
                return ScanInternal(sourceRenderers, costumeRoot, includeInactive, out error);
            }
            catch (Exception exception)
            {
                error = "走査中に予期しないエラーが発生しました: " + exception.Message;
                return null;
            }
        }

        private static MaBlendshapeSyncPlan ScanInternal(
            IReadOnlyList<SkinnedMeshRenderer> sourceRenderers,
            GameObject costumeRoot,
            bool includeInactive,
            out string error)
        {
            error = null;
            var validSources = new List<SkinnedMeshRenderer>();
            if (sourceRenderers != null)
            {
                for (int sourceIndex = 0; sourceIndex < sourceRenderers.Count; sourceIndex++)
                {
                    SkinnedMeshRenderer source = sourceRenderers[sourceIndex];
                    if (source != null && source.sharedMesh != null && !validSources.Contains(source))
                    {
                        validSources.Add(source);
                    }
                }
            }

            if (validSources.Count == 0)
            {
                error = MissingSourceError;
                return null;
            }

            if (costumeRoot == null)
            {
                error = MissingCostumeError;
                return null;
            }

            Transform avatarTransform = null;
            for (int sourceIndex = 0; sourceIndex < validSources.Count; sourceIndex++)
            {
                Transform sourceAvatar = FindAvatarTransformInParents(
                    validSources[sourceIndex].transform);
                if (sourceAvatar == null)
                {
                    error = SourceOutsideAvatarError;
                    return null;
                }

                if (avatarTransform == null)
                {
                    avatarTransform = sourceAvatar;
                }
                else if (avatarTransform != sourceAvatar)
                {
                    error = DifferentSourceAvatarError;
                    return null;
                }
            }

            Transform costumeAvatar = FindAvatarTransformInParents(costumeRoot.transform);
            if (costumeAvatar == null || costumeAvatar != avatarTransform)
            {
                error = DifferentCostumeAvatarError;
                return null;
            }

            var plan = new MaBlendshapeSyncPlan
            {
                SourceRenderers = validSources.ToArray(),
                AvatarRoot = avatarTransform.gameObject,
            };

            string[][] sourceShapeNames = new string[validSources.Count][];
            for (int sourceIndex = 0; sourceIndex < validSources.Count; sourceIndex++)
            {
                Mesh mesh = validSources[sourceIndex].sharedMesh;
                sourceShapeNames[sourceIndex] = CollectShapeNames(mesh);
            }

            SkinnedMeshRenderer[] foundRenderers =
                costumeRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive);
            var costumeRenderers = new List<SkinnedMeshRenderer>();
            var costumePaths = new List<string>();
            var costumeShapeNames = new List<string[]>();
            for (int rendererIndex = 0; rendererIndex < foundRenderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = foundRenderers[rendererIndex];
                if (renderer.sharedMesh == null || validSources.Contains(renderer))
                {
                    continue;
                }

                costumeRenderers.Add(renderer);
                costumePaths.Add(AnimationUtility.CalculateTransformPath(
                    renderer.transform, avatarTransform));
                costumeShapeNames.Add(CollectShapeNames(renderer.sharedMesh));
            }

            plan.CostumeRenderers = costumeRenderers.ToArray();
            plan.CostumeRendererPaths = costumePaths.ToArray();
            BuildCostumeShapeLookup(plan, costumeShapeNames);

            var groupsBySource =
                new Dictionary<
                    SkinnedMeshRenderer,
                    Dictionary<string, List<MaBlendshapeSyncGroup>>>();
            var allGroups = new List<MaBlendshapeSyncGroup>();
            for (int sourceIndex = 0; sourceIndex < validSources.Count; sourceIndex++)
            {
                SkinnedMeshRenderer sourceRenderer = validSources[sourceIndex];
                var groupsByName =
                    new Dictionary<string, List<MaBlendshapeSyncGroup>>(
                        StringComparer.Ordinal);
                groupsBySource.Add(sourceRenderer, groupsByName);
                string[] names = sourceShapeNames[sourceIndex];
                for (int shapeIndex = 0; shapeIndex < names.Length; shapeIndex++)
                {
                    string sourceName = names[shapeIndex];
                    var group = new MaBlendshapeSyncGroup
                    {
                        SourceRenderer = sourceRenderer,
                        SourceName = sourceName,
                        SourceIndex = shapeIndex,
                        HeaderLabel = sourceRenderer.gameObject.name + " / " + sourceName,
                        SearchName = sourceName.ToLowerInvariant(),
                    };
                    if (!groupsByName.TryGetValue(
                            sourceName, out List<MaBlendshapeSyncGroup> namedGroups))
                    {
                        namedGroups = new List<MaBlendshapeSyncGroup>();
                        groupsByName.Add(sourceName, namedGroups);
                    }

                    namedGroups.Add(group);

                    for (int rendererIndex = 0; rendererIndex < costumeRenderers.Count; rendererIndex++)
                    {
                        if (Array.IndexOf(costumeShapeNames[rendererIndex], sourceName) < 0)
                        {
                            continue;
                        }

                        group.Candidates.Add(CreateCandidate(
                            costumeRenderers[rendererIndex],
                            costumePaths[rendererIndex],
                            sourceName,
                            true,
                            false));
                    }

                    group.IsVisible = group.Candidates.Count > 0;
                    group.Foldout = group.IsVisible;

                    allGroups.Add(group);
                }
            }

            ImportExistingBindings(
                costumeRenderers, costumePaths, groupsBySource);

            plan.Groups.AddRange(allGroups);

            return plan;
        }

        private static void ImportExistingBindings(
            List<SkinnedMeshRenderer> costumeRenderers,
            List<string> costumePaths,
            Dictionary<
                SkinnedMeshRenderer,
                Dictionary<string, List<MaBlendshapeSyncGroup>>> groupsBySource)
        {
            for (int rendererIndex = 0; rendererIndex < costumeRenderers.Count; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = costumeRenderers[rendererIndex];
                ModularAvatarBlendshapeSync sync =
                    renderer.GetComponent<ModularAvatarBlendshapeSync>();
                if (sync == null || sync.Bindings == null)
                {
                    continue;
                }

                for (int bindingIndex = 0; bindingIndex < sync.Bindings.Count; bindingIndex++)
                {
                    BlendshapeBinding binding = sync.Bindings[bindingIndex];
                    if (binding.ReferenceMesh == null)
                    {
                        continue;
                    }

                    GameObject referenceObject = binding.ReferenceMesh.Get(sync);
                    SkinnedMeshRenderer sourceRenderer =
                        referenceObject != null
                            ? referenceObject.GetComponent<SkinnedMeshRenderer>()
                            : null;
                    if (sourceRenderer == null ||
                        !groupsBySource.TryGetValue(
                            sourceRenderer,
                            out Dictionary<string, List<MaBlendshapeSyncGroup>> groupsByName) ||
                        !groupsByName.TryGetValue(
                            binding.Blendshape,
                            out List<MaBlendshapeSyncGroup> groups))
                    {
                        continue;
                    }

                    string localName = string.IsNullOrWhiteSpace(binding.LocalBlendshape)
                        ? binding.Blendshape
                        : binding.LocalBlendshape;
                    for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                    {
                        MaBlendshapeSyncGroup group = groups[groupIndex];
                        MaBlendshapeSyncCandidate candidate = FindCandidate(
                            group, renderer, localName);
                        if (candidate == null)
                        {
                            candidate = CreateCandidate(
                                renderer,
                                costumePaths[rendererIndex],
                                localName,
                                true,
                                true);
                            group.Candidates.Add(candidate);
                        }
                        else
                        {
                            candidate.AlreadyConfigured = true;
                            candidate.Enabled = true;
                        }

                        group.IsVisible = true;
                        group.Foldout = true;
                    }
                }
            }
        }

        private static void BuildCostumeShapeLookup(
            MaBlendshapeSyncPlan plan, List<string[]> shapeNamesByRenderer)
        {
            var renderersByName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int rendererIndex = 0; rendererIndex < shapeNamesByRenderer.Count; rendererIndex++)
            {
                string[] names = shapeNamesByRenderer[rendererIndex];
                for (int shapeIndex = 0; shapeIndex < names.Length; shapeIndex++)
                {
                    string shapeName = names[shapeIndex];
                    if (!renderersByName.TryGetValue(shapeName, out List<int> rendererIndices))
                    {
                        rendererIndices = new List<int>();
                        renderersByName.Add(shapeName, rendererIndices);
                    }

                    if (!rendererIndices.Contains(rendererIndex))
                    {
                        rendererIndices.Add(rendererIndex);
                    }
                }
            }

            var namesList = new List<string>(renderersByName.Keys);
            namesList.Sort(StringComparer.Ordinal);
            plan.CostumeShapeNames = namesList.ToArray();
            plan.CostumeShapeContents = new GUIContent[plan.CostumeShapeNames.Length];
            for (int nameIndex = 0; nameIndex < plan.CostumeShapeNames.Length; nameIndex++)
            {
                plan.CostumeShapeContents[nameIndex] =
                    new GUIContent(plan.CostumeShapeNames[nameIndex]);
            }

            plan.CostumeRenderersByShapeName = renderersByName;
        }

        private static string[] CollectShapeNames(Mesh mesh)
        {
            var names = new string[mesh.blendShapeCount];
            for (int shapeIndex = 0; shapeIndex < names.Length; shapeIndex++)
            {
                names[shapeIndex] = mesh.GetBlendShapeName(shapeIndex);
            }

            return names;
        }

        private static Transform FindAvatarTransformInParents(Transform target)
        {
            // NOTE: 連携先のアバタールート判定は公開参照型へ集約されているため、
            //       独自のコンポーネント判定を持たず、その判定結果を使って親を特定する。
            Transform current = target;
            while (current != null)
            {
                var reference = new AvatarObjectReference(current.gameObject);
                if (string.Equals(
                        reference.referencePath,
                        AvatarObjectReference.AVATAR_ROOT,
                        StringComparison.Ordinal))
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }

        private static MaBlendshapeSyncCandidate CreateCandidate(
            SkinnedMeshRenderer renderer,
            string rendererPath,
            string localName,
            bool enabled,
            bool alreadyConfigured)
        {
            return new MaBlendshapeSyncCandidate
            {
                Renderer = renderer,
                RendererPath = rendererPath,
                LocalName = localName,
                Label = rendererPath + " : " + localName,
                Enabled = enabled,
                AlreadyConfigured = alreadyConfigured,
            };
        }

        private static MaBlendshapeSyncCandidate FindCandidate(
            MaBlendshapeSyncGroup group,
            SkinnedMeshRenderer renderer,
            string localName)
        {
            for (int candidateIndex = 0; candidateIndex < group.Candidates.Count; candidateIndex++)
            {
                MaBlendshapeSyncCandidate candidate = group.Candidates[candidateIndex];
                if (candidate.Renderer == renderer && string.Equals(
                        candidate.LocalName, localName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
