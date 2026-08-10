using System;
using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.MaBlendshapeSyncHelper.Editor
{
    internal struct MaBlendshapeSyncApplyResult
    {
        internal int ConfiguredRenderers;
        internal int AddedComponents;
        internal int AddedBindings;
        internal int RemovedBindings;
    }

    internal static class MaBlendshapeSyncHelperApplier
    {
        private const string UndoName = "Blendshape Sync Helper";

        private sealed class DesiredBinding
        {
            internal SkinnedMeshRenderer SourceRenderer;
            internal string SourceName;
            internal string LocalName;
        }

        internal static MaBlendshapeSyncApplyResult Apply(MaBlendshapeSyncPlan plan)
        {
            var result = new MaBlendshapeSyncApplyResult();
            if (plan == null || plan.SourceRenderers == null ||
                plan.SourceRenderers.Length == 0)
            {
                return result;
            }

            Dictionary<SkinnedMeshRenderer, List<DesiredBinding>> desiredByRenderer =
                BuildDesiredBindings(plan);
            Dictionary<GameObject, HashSet<string>> groupNamesBySource =
                BuildGroupNamesBySource(plan);
            var sourceObjects = new HashSet<GameObject>();
            for (int sourceIndex = 0; sourceIndex < plan.SourceRenderers.Length; sourceIndex++)
            {
                if (plan.SourceRenderers[sourceIndex] != null)
                {
                    sourceObjects.Add(plan.SourceRenderers[sourceIndex].gameObject);
                }
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            int skippedReferences = 0;
            try
            {
                SkinnedMeshRenderer[] renderers = plan.CostumeRenderers ??
                    Array.Empty<SkinnedMeshRenderer>();
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    SkinnedMeshRenderer renderer = renderers[rendererIndex];
                    if (renderer == null)
                    {
                        continue;
                    }

                    desiredByRenderer.TryGetValue(
                        renderer, out List<DesiredBinding> desiredBindings);
                    if (desiredBindings == null)
                    {
                        desiredBindings = new List<DesiredBinding>();
                    }

                    ModularAvatarBlendshapeSync sync =
                        renderer.GetComponent<ModularAvatarBlendshapeSync>();
                    if (sync == null && desiredBindings.Count == 0)
                    {
                        continue;
                    }

                    int managedExistingCount = CountManagedBindings(
                        sync, sourceObjects, groupNamesBySource);
                    if (sync != null && desiredBindings.Count == 0 && managedExistingCount == 0)
                    {
                        continue;
                    }

                    if (sync == null)
                    {
                        sync = Undo.AddComponent<ModularAvatarBlendshapeSync>(renderer.gameObject);
                        result.AddedComponents++;
                    }

                    Undo.RecordObject(sync, UndoName);
                    List<BlendshapeBinding> existingBindings = sync.Bindings ??
                        new List<BlendshapeBinding>();
                    var newBindings = new List<BlendshapeBinding>();
                    for (int bindingIndex = 0;
                         bindingIndex < existingBindings.Count;
                         bindingIndex++)
                    {
                        BlendshapeBinding binding = existingBindings[bindingIndex];
                        GameObject referenceObject = binding.ReferenceMesh != null
                            ? binding.ReferenceMesh.Get(sync)
                            : null;
                        if (!sourceObjects.Contains(referenceObject) ||
                            !ContainsGroupName(
                                groupNamesBySource,
                                referenceObject,
                                binding.Blendshape))
                        {
                            newBindings.Add(binding);
                        }
                    }

                    int maintainedExisting = 0;
                    for (int desiredIndex = 0;
                         desiredIndex < desiredBindings.Count;
                         desiredIndex++)
                    {
                        DesiredBinding desired = desiredBindings[desiredIndex];
                        var binding = new BlendshapeBinding
                        {
                            ReferenceMesh = new AvatarObjectReference(),
                            Blendshape = desired.SourceName,
                            LocalBlendshape = string.Equals(
                                desired.SourceName,
                                desired.LocalName,
                                StringComparison.Ordinal)
                                    ? string.Empty
                                    : desired.LocalName,
                        };
                        binding.ReferenceMesh.Set(desired.SourceRenderer.gameObject);
                        if (string.IsNullOrEmpty(binding.ReferenceMesh.referencePath))
                        {
                            skippedReferences++;
                            continue;
                        }

                        if (ContainsBinding(
                                newBindings,
                                sync,
                                desired.SourceRenderer.gameObject,
                                desired.SourceName,
                                desired.LocalName))
                        {
                            continue;
                        }

                        if (ContainsBinding(
                                existingBindings,
                                sync,
                                desired.SourceRenderer.gameObject,
                                desired.SourceName,
                                desired.LocalName))
                        {
                            maintainedExisting++;
                        }

                        newBindings.Add(binding);
                        result.AddedBindings++;
                    }

                    result.RemovedBindings +=
                        Mathf.Max(0, managedExistingCount - maintainedExisting);
                    sync.Bindings = newBindings;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(sync);
                    EditorUtility.SetDirty(sync);
                    result.ConfiguredRenderers++;
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            if (skippedReferences > 0)
            {
                Debug.LogWarning(
                    "Candy Box: 参照元を解決できなかったため、" + skippedReferences +
                    " 件の対応をスキップしました。");
            }

            return result;
        }

        private static Dictionary<SkinnedMeshRenderer, List<DesiredBinding>>
            BuildDesiredBindings(MaBlendshapeSyncPlan plan)
        {
            var result = new Dictionary<SkinnedMeshRenderer, List<DesiredBinding>>();
            for (int groupIndex = 0; groupIndex < plan.Groups.Count; groupIndex++)
            {
                MaBlendshapeSyncGroup group = plan.Groups[groupIndex];
                if (group.SourceRenderer == null)
                {
                    continue;
                }

                for (int candidateIndex = 0;
                     candidateIndex < group.Candidates.Count;
                     candidateIndex++)
                {
                    MaBlendshapeSyncCandidate candidate = group.Candidates[candidateIndex];
                    if (!candidate.Enabled || candidate.Renderer == null)
                    {
                        continue;
                    }

                    if (!result.TryGetValue(
                            candidate.Renderer, out List<DesiredBinding> rendererBindings))
                    {
                        rendererBindings = new List<DesiredBinding>();
                        result.Add(candidate.Renderer, rendererBindings);
                    }

                    if (ContainsDesired(
                            rendererBindings,
                            group.SourceRenderer,
                            group.SourceName,
                            candidate.LocalName))
                    {
                        continue;
                    }

                    rendererBindings.Add(new DesiredBinding
                    {
                        SourceRenderer = group.SourceRenderer,
                        SourceName = group.SourceName,
                        LocalName = candidate.LocalName,
                    });
                }
            }

            return result;
        }

        private static Dictionary<GameObject, HashSet<string>> BuildGroupNamesBySource(
            MaBlendshapeSyncPlan plan)
        {
            var result = new Dictionary<GameObject, HashSet<string>>();
            for (int groupIndex = 0; groupIndex < plan.Groups.Count; groupIndex++)
            {
                MaBlendshapeSyncGroup group = plan.Groups[groupIndex];
                if (group.SourceRenderer == null)
                {
                    continue;
                }

                GameObject sourceObject = group.SourceRenderer.gameObject;
                if (!result.TryGetValue(sourceObject, out HashSet<string> names))
                {
                    names = new HashSet<string>(StringComparer.Ordinal);
                    result.Add(sourceObject, names);
                }

                names.Add(group.SourceName);
            }

            return result;
        }

        private static int CountManagedBindings(
            ModularAvatarBlendshapeSync sync,
            HashSet<GameObject> sourceObjects,
            Dictionary<GameObject, HashSet<string>> groupNamesBySource)
        {
            if (sync == null || sync.Bindings == null)
            {
                return 0;
            }

            int count = 0;
            for (int bindingIndex = 0; bindingIndex < sync.Bindings.Count; bindingIndex++)
            {
                BlendshapeBinding binding = sync.Bindings[bindingIndex];
                GameObject referenceObject = binding.ReferenceMesh != null
                    ? binding.ReferenceMesh.Get(sync)
                    : null;
                if (sourceObjects.Contains(referenceObject) &&
                    ContainsGroupName(
                        groupNamesBySource, referenceObject, binding.Blendshape))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool ContainsGroupName(
            Dictionary<GameObject, HashSet<string>> groupNamesBySource,
            GameObject sourceObject,
            string sourceName)
        {
            return sourceObject != null &&
                groupNamesBySource.TryGetValue(sourceObject, out HashSet<string> names) &&
                names.Contains(sourceName);
        }

        private static bool ContainsDesired(
            List<DesiredBinding> bindings,
            SkinnedMeshRenderer sourceRenderer,
            string sourceName,
            string localName)
        {
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                DesiredBinding binding = bindings[bindingIndex];
                if (binding.SourceRenderer == sourceRenderer &&
                    string.Equals(binding.SourceName, sourceName, StringComparison.Ordinal) &&
                    string.Equals(binding.LocalName, localName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsBinding(
            List<BlendshapeBinding> bindings,
            ModularAvatarBlendshapeSync sync,
            GameObject sourceObject,
            string sourceName,
            string localName)
        {
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                BlendshapeBinding binding = bindings[bindingIndex];
                GameObject referenceObject = binding.ReferenceMesh != null
                    ? binding.ReferenceMesh.Get(sync)
                    : null;
                string existingLocalName = string.IsNullOrWhiteSpace(binding.LocalBlendshape)
                    ? binding.Blendshape
                    : binding.LocalBlendshape;
                if (referenceObject == sourceObject &&
                    string.Equals(binding.Blendshape, sourceName, StringComparison.Ordinal) &&
                    string.Equals(existingLocalName, localName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
