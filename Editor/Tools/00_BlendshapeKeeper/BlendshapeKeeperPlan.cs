using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BlendshapeKeeper.Editor
{
    internal enum BlendshapeKeeperOutputMode
    {
        Overwrite = 0,
        SaveAsCopy = 1,
    }

    internal sealed class BlendshapeKeeperChange
    {
        internal EditorCurveBinding Binding;
        internal int KeyIndex;
        internal float Time;
        internal float OldValue;
        internal float NewValue;
        internal string Label;
        internal bool Enabled = true;
    }

    internal sealed class BlendshapeKeeperClipPlan
    {
        internal AnimationClip Clip;
        internal string ClipLabel;
        internal readonly List<BlendshapeKeeperChange> Changes =
            new List<BlendshapeKeeperChange>();
        internal bool Foldout = true;
    }

    internal sealed class BlendshapeKeeperSkip
    {
        internal string Target;
        internal string Reason;
        internal string Label;
    }

    internal sealed class BlendshapeKeeperPlan
    {
        internal readonly List<BlendshapeKeeperClipPlan> Clips =
            new List<BlendshapeKeeperClipPlan>();
        internal readonly List<BlendshapeKeeperSkip> Skips =
            new List<BlendshapeKeeperSkip>();

        internal int EnabledChangeCount
        {
            get
            {
                int count = 0;
                for (int clipIndex = 0; clipIndex < Clips.Count; clipIndex++)
                {
                    List<BlendshapeKeeperChange> changes = Clips[clipIndex].Changes;
                    for (int changeIndex = 0; changeIndex < changes.Count; changeIndex++)
                    {
                        if (changes[changeIndex].Enabled)
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
