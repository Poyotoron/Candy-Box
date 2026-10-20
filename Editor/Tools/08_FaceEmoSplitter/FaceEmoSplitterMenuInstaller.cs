using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.FaceEmoSplitter.Editor
{
    internal sealed class FaceEmoSplitterMenuSnapshot
    {
        internal Object InstallTarget;
        internal bool Enabled;
    }

    internal static class FaceEmoSplitterMenuInstaller
    {
        private const string ComponentName = "ModularAvatarMenuInstaller";
        private const string TargetProperty = "installTargetMenu";
        private const string EnabledProperty = "m_Enabled";
        private static bool _warned;

        internal static void ResetWarning()
        {
            _warned = false;
        }

        internal static FaceEmoSplitterMenuSnapshot Capture(GameObject instance)
        {
            foreach (Transform child in instance.transform)
            {
                if (!FaceEmoSplitterNames.IsExpression(child.name))
                {
                    continue;
                }

                // NOTE: MA は FaceEmo の依存であり、本ツールの直接の依存にしない。
                Component component = child.GetComponent(ComponentName);
                if (component == null)
                {
                    return null;
                }

                var serialized = new SerializedObject(component);
                SerializedProperty target = serialized.FindProperty(TargetProperty);
                SerializedProperty enabled = serialized.FindProperty(EnabledProperty);
                if (target == null || enabled == null)
                {
                    WarnMissingProperty();
                    return null;
                }

                return new FaceEmoSplitterMenuSnapshot
                {
                    InstallTarget = target.objectReferenceValue,
                    Enabled = enabled.boolValue
                };
            }

            return null;
        }

        internal static bool Restore(GameObject expressionPrefab, FaceEmoSplitterMenuSnapshot snapshot)
        {
            if (snapshot == null || expressionPrefab == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(expressionPrefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Component component = root.GetComponent(ComponentName);
                if (component == null)
                {
                    return false;
                }

                var serialized = new SerializedObject(component);
                SerializedProperty target = serialized.FindProperty(TargetProperty);
                SerializedProperty enabled = serialized.FindProperty(EnabledProperty);
                if (target == null || enabled == null)
                {
                    WarnMissingProperty();
                    return false;
                }

                target.objectReferenceValue = snapshot.InstallTarget;
                enabled.boolValue = snapshot.Enabled;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                return success;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void WarnMissingProperty()
        {
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("Candy Box: MA Menu Installer の設定を読み書きできませんでした。Modular Avatar の版を確認してください。");
            }
        }
    }
}
