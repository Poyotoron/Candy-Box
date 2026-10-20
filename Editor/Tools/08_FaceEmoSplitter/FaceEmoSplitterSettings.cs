using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.FaceEmoSplitter.Editor
{
    // NOTE: ツールを無効にすると型が消えるため、ScriptableObject ではなく JSON に保存する。
    // NOTE: JsonUtility で読み書きするフィールドだけ public にする。
    [Serializable]
    internal sealed class FaceEmoSplitterSettingsData
    {
        public int version = 1;
        public string rootGuid;
        public bool keepMenuInstaller = true;
        public List<FaceEmoSplitterSavedLabel> labels = new List<FaceEmoSplitterSavedLabel>();
        public List<FaceEmoSplitterSavedNode> nodes = new List<FaceEmoSplitterSavedNode>();
    }

    [Serializable]
    internal sealed class FaceEmoSplitterSavedLabel
    {
        public int index;
        public string sourceGuid;
        public string starGuid;
        public bool apply;
    }

    [Serializable]
    internal sealed class FaceEmoSplitterSavedNode
    {
        public string guid;
        public int assigned;
    }

    internal static class FaceEmoSplitterSettings
    {
        internal static TextAsset Find(FaceEmoSplitterTree tree)
        {
            string rootGuid = AssetDatabase.AssetPathToGUID(tree.Root.Path);
            TextAsset newest = null;
            DateTime newestTime = DateTime.MinValue;
            foreach (string guid in AssetDatabase.FindAssets("FaceEmoSplitter t:TextAsset", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                FaceEmoSplitterSettingsData data = Read(asset);
                if (data == null || data.rootGuid != rootGuid)
                {
                    continue;
                }

                DateTime time = File.GetLastWriteTimeUtc(path);
                if (newest == null || time > newestTime)
                {
                    newest = asset;
                    newestTime = time;
                }
            }

            return newest;
        }

        internal static FaceEmoSplitterSettingsData Read(TextAsset asset)
        {
            if (asset == null)
            {
                return null;
            }

            try
            {
                FaceEmoSplitterSettingsData data = JsonUtility.FromJson<FaceEmoSplitterSettingsData>(asset.text);
                if (data != null && data.version == 1 && data.labels != null && data.nodes != null)
                {
                    return data;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Candy Box: 設定ファイルを読み込めませんでした: " +
                    AssetDatabase.GetAssetPath(asset) + "\n" + exception.Message);
                return null;
            }

            Debug.LogWarning("Candy Box: 設定ファイルの形式に対応していません: " + AssetDatabase.GetAssetPath(asset));
            return null;
        }

        internal static FaceEmoSplitterSettingsData Capture(
            FaceEmoSplitterTree tree, FaceEmoSplitterLabels labels, bool keepMenuInstaller, ICollection<int> generated)
        {
            var data = new FaceEmoSplitterSettingsData
            {
                rootGuid = AssetDatabase.AssetPathToGUID(tree.Root.Path),
                keepMenuInstaller = keepMenuInstaller
            };
            foreach (FaceEmoSplitterLabel label in labels.Labels)
            {
                string source = generated.Contains(label.Index)
                    ? AssetDatabase.GetAssetPath(label.Star.Launchers[0].AV3Setting.MARootObjectPrefab)
                    : label.Source;
                data.labels.Add(new FaceEmoSplitterSavedLabel
                {
                    index = label.Index,
                    sourceGuid = string.IsNullOrEmpty(source) ? "" : AssetDatabase.AssetPathToGUID(source),
                    starGuid = label.Star == null ? "" : AssetDatabase.AssetPathToGUID(label.Star.Path),
                    apply = label.Apply
                });
            }

            foreach (FaceEmoSplitterNode node in tree.Nodes)
            {
                if (node.Assigned >= 0)
                {
                    // NOTE: パスや名前は改名・移動で変わるため、同じアセットを GUID で覚える。
                    data.nodes.Add(new FaceEmoSplitterSavedNode
                    {
                        guid = AssetDatabase.AssetPathToGUID(node.Path),
                        assigned = node.Assigned
                    });
                }
            }

            return data;
        }

        internal static TextAsset Write(string path, FaceEmoSplitterSettingsData data)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.Contains(".."))
            {
                throw new ArgumentException("設定ファイルは Assets の中の JSON ファイルを選んでください。", nameof(path));
            }

            File.WriteAllText(path, ToJson(data));
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }

        internal static string ToJson(FaceEmoSplitterSettingsData data)
        {
            return JsonUtility.ToJson(data, true);
        }
    }
}
