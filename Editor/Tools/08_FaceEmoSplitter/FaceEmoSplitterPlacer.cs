using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.FaceEmoSplitter.Editor
{
    internal static class FaceEmoSplitterPlacer
    {
        internal static List<FaceEmoSplitterNode> Plan(FaceEmoSplitterTree tree, FaceEmoSplitterLabels labels)
        {
            Dictionary<int, string> paths = FaceEmoSplitterGenerator.PlannedPaths(labels);
            var nodes = new List<FaceEmoSplitterNode>();
            foreach (FaceEmoSplitterNode node in tree.Nodes)
            {
                if (node.IsOrigin)
                {
                    if (!MatchesOrigin(node, paths[node.Effective]))
                    {
                        nodes.Add(node);
                    }
                }
                else if (node.CurrentLabel != (node.Parent == null ? -1 : node.Parent.CurrentLabel) ||
                    node.ExpressionCount != (node.Parent == null ? 0 : node.Parent.ExpressionCount))
                {
                    nodes.Add(node);
                }
            }

            return nodes;
        }

        internal static string ExpressionName(FaceEmoSplitterNode node)
        {
            // NOTE: 分けた起点の名前を区別し、通常の適用が親へ影響することを防ぐ。
            return node.Parent == null || node.Parent.Effective < 0
                ? FaceEmoSplitterNames.Expression : FaceEmoSplitterNames.Split;
        }

        private static bool MatchesOrigin(FaceEmoSplitterNode node, string path)
        {
            return !string.IsNullOrEmpty(path) && node.ExpressionCount == 1 &&
                node.ExpressionSource == path && node.ExpressionName == ExpressionName(node);
        }

        private static bool MatchesParent(FaceEmoSplitterNode node)
        {
            return node.ExpressionCount == (node.Parent == null ? 0 : node.Parent.ExpressionCount) &&
                node.ExpressionSource == node.Parent?.ExpressionSource;
        }

        internal static int Place(FaceEmoSplitterTree tree, FaceEmoSplitterLabels labels, out string error)
        {
            error = null;
            int saved = 0;
            foreach (FaceEmoSplitterNode node in tree.Nodes)
            {
                // NOTE: 親を保存すると子の継承内容も変わるため、ノードごとに最新の状態を読む。
                FaceEmoSplitterScanner.ReadExpression(node);
                if (node.Parent != null)
                {
                    FaceEmoSplitterScanner.ReadExpression(node.Parent);
                }

                GameObject expression = null;
                string path = null;
                if (node.IsOrigin)
                {
                    FaceEmoSplitterLabel label = labels.Find(node.Effective);
                    expression = label.Apply ? label.Star.Launchers[0].AV3Setting.MARootObjectPrefab :
                        AssetDatabase.LoadAssetAtPath<GameObject>(label.Source);
                    path = AssetDatabase.GetAssetPath(expression);
                    if (expression == null)
                    {
                        error = "ラベル " + label.Name + " の表情 Prefab が見つかりません。";
                        return saved;
                    }
                }

                if (node.IsOrigin ? MatchesOrigin(node, path) : MatchesParent(node))
                {
                    continue;
                }

                GameObject contents = PrefabUtility.LoadPrefabContents(node.Path);
                try
                {
                    for (int i = contents.transform.childCount - 1; i >= 0; i--)
                    {
                        GameObject child = contents.transform.GetChild(i).gameObject;
                        if (FaceEmoSplitterNames.IsExpression(child.name) &&
                            (node.IsOrigin || PrefabUtility.IsAddedGameObjectOverride(child) ||
                                PrefabUtility.GetCorrespondingObjectFromSource(child) == null))
                        {
                            UnityEngine.Object.DestroyImmediate(child);
                        }
                    }

                    if (node.IsOrigin)
                    {
                        var placed = (GameObject)PrefabUtility.InstantiatePrefab(expression, contents.transform);
                        placed.name = ExpressionName(node);
                    }
                    else
                    {
                        foreach (var removed in PrefabUtility.GetRemovedGameObjects(contents))
                        {
                            if (removed.assetGameObject != null &&
                                FaceEmoSplitterNames.IsExpression(removed.assetGameObject.name))
                            {
                                removed.Revert();
                            }
                        }
                    }

                    PrefabUtility.SaveAsPrefabAsset(contents, node.Path, out bool success);
                    if (!success)
                    {
                        error = node.Path + " の表情オブジェクトを直せませんでした。";
                        return saved;
                    }

                    saved++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                FaceEmoSplitterScanner.ReadExpression(node);
                if (!(node.IsOrigin ? MatchesOrigin(node, path) : MatchesParent(node)))
                {
                    error = node.Path + " の表情オブジェクトを直せませんでした。";
                    return saved;
                }
            }

            return saved;
        }
    }
}
