using System.Collections.Generic;
using Suzuryg.FaceEmo.Components;
using UnityEngine;

namespace Poyo.CandyBox.FaceEmoSplitter.Editor
{
    internal static class FaceEmoSplitterNames
    {
        internal const string Expression = "FaceEmoPrefab";
        internal const string Split = "FaceEmoPrefab (Split)";
        internal const string Hidden = "FaceEmoPrefab (Hidden)";
        private static readonly string[] Letters = CreateLetters();

        private static string[] CreateLetters()
        {
            var letters = new string[26];
            for (int i = 0; i < letters.Length; i++)
            {
                letters[i] = ((char)('A' + i)).ToString();
            }

            return letters;
        }

        internal static string LabelName(int index)
        {
            return index < 0 ? "なし" : index < Letters.Length ? Letters[index] : "L" + (index + 1);
        }

        internal static bool IsExpression(string name)
        {
            return name == Expression || name == Split;
        }
    }

    internal sealed class FaceEmoSplitterNode
    {
        internal string Path;
        internal string Name;
        internal GameObject Asset;
        internal FaceEmoSplitterNode Parent;
        internal readonly List<FaceEmoSplitterNode> Children = new List<FaceEmoSplitterNode>();
        internal int Depth;
        internal bool IsLastChild;
        internal bool[] Continues;
        internal GUIContent NameContent;
        internal GUIContent StatusContent;
        internal GUIContent ChipContent;
        internal bool IsStar;
        internal int ExpressionCount;
        internal string ExpressionName;
        internal string ExpressionSource;
        internal int CurrentLabel = -1;
        internal readonly List<FaceEmoLauncherComponent> Launchers = new List<FaceEmoLauncherComponent>();
        internal readonly List<GameObject> Instances = new List<GameObject>();
        internal bool HasSceneAddedExpression;
        internal string StatusLabel;
        internal int Assigned;
        internal int InitialAssigned;
        internal int Effective;
        internal bool IsOrigin;
        internal int AssignIndex;
        internal string RowStatus;
    }

    internal sealed class FaceEmoSplitterTree
    {
        internal FaceEmoSplitterNode Root;
        internal readonly List<FaceEmoSplitterNode> Nodes = new List<FaceEmoSplitterNode>();
        internal readonly List<string> LabelSources = new List<string>();
    }

    internal sealed class FaceEmoSplitterLabel
    {
        internal int Index;
        internal string Name;
        internal string Source;
        internal FaceEmoSplitterNode Star;
        internal bool Apply;
        internal int OriginCount;
        internal string OriginLabel;
        internal string StarLabel;
        internal string SourceLabel;
        internal GUIContent ChipContent;
    }
}
