using System;
using System.Collections.Generic;
using System.IO;
using Suzuryg.FaceEmo.Components;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.FaceEmoSplitter.Editor
{
    internal static class FaceEmoSplitterScanner
    {
        internal static FaceEmoSplitterTree Scan(GameObject input, out string error)
        {
            error = null;
            if (EditorApplication.isPlaying)
            {
                error = "再生中は使えません。再生を停止してください。";
                return null;
            }

            string path = input == null ? null : PrefabUtility.IsPartOfPrefabInstance(input) &&
                !EditorUtility.IsPersistent(input)
                ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(input)
                : AssetDatabase.GetAssetPath(input);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                error = "アバターの Prefab を指定してください。";
                return null;
            }

            if (!IsWritablePrefab(path))
            {
                error = "Packages の中の Prefab は書き換えられません。Assets の中の Prefab を指定してください。";
                return null;
            }

            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            while (root != null && PrefabUtility.GetPrefabAssetType(root) == PrefabAssetType.Variant)
            {
                GameObject parent = PrefabUtility.GetCorrespondingObjectFromSource(root);
                string parentPath = AssetDatabase.GetAssetPath(parent);
                if (!IsWritablePrefab(parentPath))
                {
                    break;
                }

                root = parent;
                path = parentPath;
            }

            var tree = new FaceEmoSplitterTree { Root = CreateNode(path, null) };
            var byPath = new Dictionary<string, FaceEmoSplitterNode>(StringComparer.Ordinal)
            {
                { path, tree.Root }
            };
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
                var pending = new HashSet<string>(StringComparer.Ordinal);
                var dependencies = new Dictionary<string, string[]>(StringComparer.Ordinal);
                foreach (string guid in guids)
                {
                    string candidate = AssetDatabase.GUIDToAssetPath(guid);
                    if (candidate != path && IsWritablePrefab(candidate))
                    {
                        pending.Add(candidate);
                        dependencies.Add(candidate, AssetDatabase.GetDependencies(candidate, false));
                    }
                }

                bool added;
                do
                {
                    added = false;
                    var resolved = new List<string>();
                    int index = 0;
                    foreach (string candidate in pending)
                    {
                        EditorUtility.DisplayProgressBar("Candy Box", "Prefab の系統を調べています…",
                            (float)index++ / Math.Max(1, pending.Count));
                        bool related = false;
                        foreach (string dependency in dependencies[candidate])
                        {
                            if (byPath.ContainsKey(dependency))
                            {
                                related = true;
                                break;
                            }
                        }

                        // NOTE: 全 Prefab の読み込みを避け、直接の依存先が系統に入ったものだけ調べる。
                        if (!related)
                        {
                            continue;
                        }

                        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(candidate);
                        if (asset == null || PrefabUtility.GetPrefabAssetType(asset) != PrefabAssetType.Variant)
                        {
                            resolved.Add(candidate);
                            continue;
                        }

                        string parentPath = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(asset));
                        if (byPath.TryGetValue(parentPath, out FaceEmoSplitterNode parent))
                        {
                            FaceEmoSplitterNode node = CreateNode(candidate, parent);
                            parent.Children.Add(node);
                            byPath.Add(candidate, node);
                            resolved.Add(candidate);
                            added = true;
                        }
                    }

                    pending.ExceptWith(resolved);
                }
                while (added);

                AddDepthFirst(tree, tree.Root);
                foreach (FaceEmoSplitterNode node in tree.Nodes)
                {
                    ReadExpression(node);
                    if (node.ExpressionCount > 0)
                    {
                        int label = tree.LabelSources.IndexOf(node.ExpressionSource);
                        if (label < 0)
                        {
                            label = tree.LabelSources.Count;
                            tree.LabelSources.Add(node.ExpressionSource);
                        }

                        node.CurrentLabel = label;
                    }
                }

                foreach (FaceEmoLauncherComponent launcher in UnityEngine.Object.FindObjectsOfType<FaceEmoLauncherComponent>(true))
                {
                    if (launcher.AV3Setting == null || launcher.AV3Setting.TargetAvatar == null)
                    {
                        continue;
                    }

                    GameObject instance = launcher.AV3Setting.TargetAvatar.gameObject;
                    if (!PrefabUtility.IsOutermostPrefabInstanceRoot(instance))
                    {
                        continue;
                    }

                    string instancePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
                    if (byPath.TryGetValue(instancePath, out FaceEmoSplitterNode node))
                    {
                        node.Launchers.Add(launcher);
                        node.Instances.Add(instance);
                        node.HasSceneAddedExpression |= HasAddedExpression(instance);
                    }
                }

                foreach (FaceEmoSplitterNode node in tree.Nodes)
                {
                    var status = new List<string>();
                    foreach (FaceEmoLauncherComponent launcher in node.Launchers)
                    {
                        status.Add("ランチャー: " + launcher.gameObject.name);
                    }

                    if (node.HasSceneAddedExpression)
                    {
                        status.Add("シーンに追加された FaceEmoPrefab があります");
                    }

                    if (node.ExpressionCount > 1)
                    {
                        status.Add("表情オブジェクトが複数あります");
                    }

                    node.StatusLabel = string.Join("、", status);
                }

                return tree;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool IsWritablePrefab(string path)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal) &&
                path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        private static FaceEmoSplitterNode CreateNode(string path, FaceEmoSplitterNode parent)
        {
            // NOTE: 読み込みだけでシーンや Prefab が変更済みになることを避ける。
            return new FaceEmoSplitterNode
            {
                Path = path,
                Name = Path.GetFileNameWithoutExtension(path),
                NameContent = new GUIContent(Path.GetFileNameWithoutExtension(path)),
                Asset = AssetDatabase.LoadAssetAtPath<GameObject>(path),
                Parent = parent,
                Depth = parent == null ? 0 : parent.Depth + 1
            };
        }

        private static void AddDepthFirst(FaceEmoSplitterTree tree, FaceEmoSplitterNode node)
        {
            node.IsLastChild = node.Parent == null ||
                node.Parent.Children[node.Parent.Children.Count - 1] == node;
            node.Continues = new bool[node.Depth];
            if (node.Parent != null)
            {
                System.Array.Copy(node.Parent.Continues, node.Continues, node.Parent.Continues.Length);
                if (node.Depth >= 2)
                {
                    // NOTE: 末尾の要素は自分の枝用に空け、親の枝が続くかをその一つ手前に写す。
                    node.Continues[node.Depth - 2] = !node.Parent.IsLastChild;
                }
            }

            tree.Nodes.Add(node);
            node.Children.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            foreach (FaceEmoSplitterNode child in node.Children)
            {
                AddDepthFirst(tree, child);
            }
        }

        internal static bool HasAddedExpression(GameObject instance)
        {
            for (int i = 0; i < instance.transform.childCount; i++)
            {
                GameObject child = instance.transform.GetChild(i).gameObject;
                if (child.name == FaceEmoSplitterNames.Expression && PrefabUtility.IsAddedGameObjectOverride(child))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void ReadExpression(FaceEmoSplitterNode node)
        {
            node.Asset = AssetDatabase.LoadAssetAtPath<GameObject>(node.Path);
            node.ExpressionCount = 0;
            node.ExpressionName = null;
            node.ExpressionSource = null;
            if (node.Asset == null)
            {
                throw new InvalidOperationException(node.Path + " が見つかりません。読み込み直してください。");
            }

            for (int i = 0; i < node.Asset.transform.childCount; i++)
            {
                GameObject child = node.Asset.transform.GetChild(i).gameObject;
                if (!FaceEmoSplitterNames.IsExpression(child.name))
                {
                    continue;
                }

                if (node.ExpressionCount++ == 0)
                {
                    node.ExpressionName = child.name;
                    node.ExpressionSource = AssetDatabase.GetAssetPath(
                        PrefabUtility.GetCorrespondingObjectFromOriginalSource(child));
                }
            }
        }
    }
}
