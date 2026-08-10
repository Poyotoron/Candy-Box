using System.Collections.Generic;
using UnityEngine;

namespace Poyo.CandyBox.MaBlendshapeSyncHelper.Editor
{
    internal sealed class MaBlendshapeSyncCandidate
    {
        internal SkinnedMeshRenderer Renderer;
        internal string RendererPath;
        internal string LocalName;
        internal string Label;
        internal bool Enabled;
        internal bool AlreadyConfigured;
    }

    internal sealed class MaBlendshapeSyncGroup
    {
        internal SkinnedMeshRenderer SourceRenderer;
        internal string SourceName;
        internal int SourceIndex;
        internal string HeaderLabel;
        internal bool IsVisible;
        internal string SearchName;
        internal readonly List<MaBlendshapeSyncCandidate> Candidates =
            new List<MaBlendshapeSyncCandidate>();
        internal bool Foldout = true;
        internal int ManualShapeIndex = -1;
    }

    internal sealed class MaBlendshapeSyncPlan
    {
        internal SkinnedMeshRenderer[] SourceRenderers;
        internal GameObject AvatarRoot;
        internal readonly List<MaBlendshapeSyncGroup> Groups =
            new List<MaBlendshapeSyncGroup>();
        internal SkinnedMeshRenderer[] CostumeRenderers;
        internal string[] CostumeRendererPaths;
        internal string[] CostumeShapeNames;
        internal GUIContent[] CostumeShapeContents;
        internal Dictionary<string, List<int>> CostumeRenderersByShapeName;

        internal int EnabledCount
        {
            get
            {
                int count = 0;
                for (int groupIndex = 0; groupIndex < Groups.Count; groupIndex++)
                {
                    List<MaBlendshapeSyncCandidate> candidates = Groups[groupIndex].Candidates;
                    for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                    {
                        if (candidates[candidateIndex].Enabled)
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }

        internal int RemovalCount
        {
            get
            {
                int count = 0;
                for (int groupIndex = 0; groupIndex < Groups.Count; groupIndex++)
                {
                    List<MaBlendshapeSyncCandidate> candidates = Groups[groupIndex].Candidates;
                    for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                    {
                        MaBlendshapeSyncCandidate candidate = candidates[candidateIndex];
                        if (candidate.AlreadyConfigured && !candidate.Enabled)
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }
    }
}
