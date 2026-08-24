using System;
using System.Collections.Generic;
using System.IO;
using Poyo.CandyBox.Editor;
using UnityEditor;

namespace Poyo.CandyBox.BlendShapeBackuper.Editor
{
    internal static class BlendShapeBackupLocator
    {
        internal static List<BlendShapeBackupListItem> Collect(
            BlendShapeBackupTarget target, bool includeAll)
        {
            var items = new List<BlendShapeBackupListItem>();
            string[] guids = AssetDatabase.FindAssets("t:BlendShapeBackupAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                BlendShapeBackupAsset asset =
                    AssetDatabase.LoadAssetAtPath<BlendShapeBackupAsset>(assetPath);
                // NOTE: 検索結果には同名型や壊れたアセットが混ざることがあるため、
                //       実際に目的の型として読み込めたものだけを扱う。
                if (asset == null)
                {
                    continue;
                }

                bool metaMatches = target != null && target.IsValid &&
                    !string.IsNullOrEmpty(target.MeshGuid) &&
                    asset.SourceMeshGuid == target.MeshGuid &&
                    asset.SourceMeshFileId == target.MeshFileId;
                // NOTE: GUID を持たない対象同士を一致とみなすと、無関係な
                //       バックアップまで同じメッシュとして並んでしまう。
                if (!includeAll && !metaMatches)
                {
                    continue;
                }

                string displayName = string.IsNullOrEmpty(asset.DisplayName)
                    ? Path.GetFileNameWithoutExtension(assetPath)
                    : asset.DisplayName;
                int entryCount = asset.Entries == null ? 0 : asset.Entries.Count;
                items.Add(new BlendShapeBackupListItem
                {
                    Asset = asset,
                    AssetPath = assetPath,
                    MetaMatches = metaMatches,
                    RowLabel = string.Format(
                        "{0}  {1}  {2} 件",
                        displayName,
                        asset.CapturedAt,
                        entryCount),
                    SubLabel = metaMatches
                        ? string.Format("元メッシュ: {0}", asset.SourceMeshName)
                        : string.Format(
                            "元メッシュ: {0} / 別のメッシュから保存されています",
                            asset.SourceMeshName),
                    SortKey = asset.CapturedAt ?? string.Empty,
                });
            }

            // NOTE: 保存日時は辞書順と時刻順が一致する形式なので、壊れた値で
            //       例外になりうる日時変換をせず文字列のまま並べ替える。
            items.Sort(CompareItems);
            return items;
        }

        private static int CompareItems(
            BlendShapeBackupListItem left, BlendShapeBackupListItem right)
        {
            bool leftEmpty = string.IsNullOrEmpty(left.SortKey);
            bool rightEmpty = string.IsNullOrEmpty(right.SortKey);
            if (leftEmpty != rightEmpty)
            {
                return leftEmpty ? 1 : -1;
            }

            return string.Compare(right.SortKey, left.SortKey, StringComparison.Ordinal);
        }
    }
}
