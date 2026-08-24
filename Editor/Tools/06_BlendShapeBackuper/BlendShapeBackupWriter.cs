using System;
using System.IO;
using Poyo.CandyBox.Editor;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BlendShapeBackuper.Editor
{
    internal sealed class BlendShapeBackupSaveResult
    {
        internal bool Succeeded;
        internal BlendShapeBackupAsset Asset;
        internal string AssetPath;

        /// <summary>ウィンドウに出す結果の文字列。ここで組み立てる。</summary>
        internal string MessageLabel;
    }

    internal static class BlendShapeBackupWriter
    {
        internal const string DefaultFolder =
            "Assets/zzz_pytr/CandyBox/BlendShapeBackups";
        private const string CapturedAtFormat = "yyyy-MM-dd HH:mm:ss";
        private const string FileNameTimeFormat = "yyyyMMdd_HHmmss";
        private const string InvalidFolderLabel =
            "保存フォルダはプロジェクト内の Assets 配下を指定してください。";
        private const string InvalidSubFolderLabel =
            "サブフォルダ名に使えない文字が含まれています。";
        private const string CreateFolderFailedLabel =
            "保存フォルダを作成できませんでした。";
        private const string PlayingLabel = "再生中は保存できません。";
        private const string MeshChangedLabel =
            "対象のメッシュが変更されました。対象を指定し直してください。";
        private const string FallbackFileName = "BlendShapeBackup";

        internal static string BuildDefaultName(BlendShapeBackupTarget target)
        {
            if (target == null || !target.IsValid)
            {
                return string.Empty;
            }

            return string.Format(
                "{0}_{1}",
                target.MeshName,
                DateTime.Now.ToString(FileNameTimeFormat));
        }

        internal static bool IsValidSubFolderName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            for (int i = 0; i < name.Length; i++)
            {
                char character = name[i];
                if (character == '/' || character == '\\' || character == ':' ||
                    Array.IndexOf(invalidCharacters, character) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool TryEnsureFolder(
            string folderPath,
            string subFolderName,
            out string resolvedPath,
            out string errorLabel)
        {
            resolvedPath = string.Empty;
            errorLabel = string.Empty;

            string normalizedPath = NormalizeFolderPath(folderPath);
            if (string.IsNullOrEmpty(normalizedPath) ||
                (normalizedPath != "Assets" &&
                 !normalizedPath.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                errorLabel = InvalidFolderLabel;
                return false;
            }

            if (!IsValidSubFolderName(subFolderName))
            {
                errorLabel = InvalidSubFolderLabel;
                return false;
            }

            string[] segments = normalizedPath.Split('/');
            string currentPath = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                if (string.IsNullOrEmpty(segments[i]))
                {
                    errorLabel = InvalidFolderLabel;
                    return false;
                }

                string nextPath = currentPath + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    // NOTE: AssetDatabase にフォルダを認識させたまま直後にアセットを
                    //       作れるよう、親から 1 段ずつ作成する。
                    string guid = AssetDatabase.CreateFolder(currentPath, segments[i]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        errorLabel = CreateFolderFailedLabel;
                        return false;
                    }
                }

                currentPath = nextPath;
            }

            if (!string.IsNullOrEmpty(subFolderName))
            {
                string subFolderPath = currentPath + "/" + subFolderName;
                if (!AssetDatabase.IsValidFolder(subFolderPath))
                {
                    string guid = AssetDatabase.CreateFolder(currentPath, subFolderName);
                    if (string.IsNullOrEmpty(guid))
                    {
                        errorLabel = CreateFolderFailedLabel;
                        return false;
                    }
                }

                currentPath = subFolderPath;
            }

            resolvedPath = currentPath;
            return true;
        }

        internal static BlendShapeBackupSaveResult Save(
            BlendShapeBackupTarget target,
            string folderPath,
            string subFolderName,
            string displayName)
        {
            var result = new BlendShapeBackupSaveResult();
            if (target == null || !target.IsValid)
            {
                result.MessageLabel = target == null
                    ? "対象のメッシュを指定してください。"
                    : target.BlockedLabel;
                return result;
            }

            if (EditorApplication.isPlaying)
            {
                result.MessageLabel = PlayingLabel;
                return result;
            }

            string resolvedFolder;
            string folderError;
            if (!TryEnsureFolder(
                folderPath, subFolderName, out resolvedFolder, out folderError))
            {
                result.MessageLabel = folderError;
                return result;
            }

            Mesh currentMesh =
                target.Renderer == null ? null : target.Renderer.sharedMesh;
            // NOTE: 対象の解決後にメッシュだけが差し替わることがあるため、
            //       アセットを作る前に現在の参照が保存対象と同じか確認する。
            if (currentMesh == null || currentMesh != target.Mesh)
            {
                result.MessageLabel = MeshChangedLabel;
                return result;
            }

            BlendShapeBackupAsset asset =
                ScriptableObject.CreateInstance<BlendShapeBackupAsset>();
            asset.DisplayName = string.IsNullOrEmpty(displayName)
                ? BuildDefaultName(target)
                : displayName;
            asset.CapturedAt = DateTime.Now.ToString(CapturedAtFormat);
            asset.SourceMeshGuid = target.MeshGuid;
            asset.SourceMeshFileId = target.MeshFileId;
            asset.SourceMeshName = target.MeshName;
            asset.SourceMeshPath = target.MeshPath;
            asset.VertexCount = target.VertexCount;
            asset.BlendShapeCount = target.BlendShapeCount;
            asset.RendererPath = target.RendererPath;

            // NOTE: 範囲外の値や同名キーも利用者の状態の一部なので、加工せず
            //       メッシュの並び順どおりに保存する。
            for (int i = 0; i < currentMesh.blendShapeCount; i++)
            {
                asset.Entries.Add(new BlendShapeBackupEntry
                {
                    Name = currentMesh.GetBlendShapeName(i),
                    Value = target.Renderer.GetBlendShapeWeight(i),
                });
            }

            string fileName = ReplaceInvalidFileNameCharacters(asset.DisplayName);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                // NOTE: 表示名は利用者の入力のまま保存し、ファイル名だけを
                //       読み分けられる既定名へ置き換える。
                fileName = ReplaceInvalidFileNameCharacters(
                    BuildDefaultName(target));
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = FallbackFileName;
            }

            string candidatePath = string.Format(
                "{0}/{1}.asset", resolvedFolder, fileName);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(candidatePath);
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);

            result.Succeeded = true;
            result.Asset = asset;
            result.AssetPath = assetPath;
            result.MessageLabel = string.Format(
                "バックアップを保存しました: {0}（{1} 件）",
                assetPath,
                asset.Entries.Count);
            return result;
        }

        private static string NormalizeFolderPath(string folderPath)
        {
            return string.IsNullOrEmpty(folderPath)
                ? string.Empty
                : folderPath.Trim().Replace('\\', '/').TrimEnd('/');
        }

        private static string ReplaceInvalidFileNameCharacters(string value)
        {
            string sanitized = value ?? string.Empty;
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidCharacters.Length; i++)
            {
                sanitized = sanitized.Replace(invalidCharacters[i], '_');
            }

            sanitized = sanitized.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            return sanitized;
        }
    }
}
