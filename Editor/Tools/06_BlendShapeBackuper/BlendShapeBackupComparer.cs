using System.Collections.Generic;
using Poyo.CandyBox.Editor;
using UnityEngine;

namespace Poyo.CandyBox.BlendShapeBackuper.Editor
{
    internal static class BlendShapeBackupComparer
    {
        internal const float ValueEpsilon = 1e-3f;

        internal static BlendShapeCompareResult Compare(
            BlendShapeBackupTarget target, BlendShapeBackupListItem item)
        {
            var result = new BlendShapeCompareResult();
            if (target == null || !target.IsValid || item == null || item.Asset == null)
            {
                RefreshLabels(result);
                return result;
            }

            result.Backup = item.Asset;
            result.MetaMismatch = !item.MetaMatches;
            result.ChangedExpanded = true;
            result.UnchangedExpanded = false;
            result.BackupOnlyExpanded = true;
            result.MeshOnlyExpanded = true;

            var meshIndices = new Dictionary<string, int>();
            for (int i = 0; i < target.Mesh.blendShapeCount; i++)
            {
                string name = target.Mesh.GetBlendShapeName(i);
                // NOTE: 同名のキーが存在しても例外にせず、最初に現れたキーを
                //       名前による復元先として扱う。
                if (!meshIndices.ContainsKey(name))
                {
                    meshIndices[name] = i;
                }
            }

            var backupNames = new HashSet<string>();
            List<BlendShapeBackupEntry> entries = item.Asset.Entries;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    BlendShapeBackupEntry entry = entries[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    string name = entry.Name ?? string.Empty;
                    if (!backupNames.Add(name))
                    {
                        result.Duplicated.Add(new BlendShapeCompareRow
                        {
                            Name = name,
                            BackupValue = entry.Value,
                            IsDuplicate = true,
                            RowLabel = string.Format(
                                "{0}  （バックアップ内で名前が重複しています。最初の 1 件だけを使います）",
                                name),
                        });
                        continue;
                    }

                    int meshIndex;
                    if (!meshIndices.TryGetValue(name, out meshIndex))
                    {
                        result.BackupOnly.Add(new BlendShapeCompareRow
                        {
                            Name = name,
                            Category = BlendShapeCompareCategory.BackupOnly,
                            BackupValue = entry.Value,
                            IsSelectable = false,
                            RowLabel = string.Format(
                                "{0}    {1:F1}  （メッシュにこのキーがありません）",
                                name,
                                entry.Value),
                        });
                        continue;
                    }

                    float currentValue =
                        target.Renderer.GetBlendShapeWeight(meshIndex);
                    float delta = entry.Value - currentValue;
                    bool changed = Mathf.Abs(delta) > ValueEpsilon;
                    var row = new BlendShapeCompareRow
                    {
                        Name = name,
                        Category = changed
                            ? BlendShapeCompareCategory.Changed
                            : BlendShapeCompareCategory.Unchanged,
                        MeshIndex = meshIndex,
                        BackupValue = entry.Value,
                        CurrentValue = currentValue,
                        Delta = delta,
                        IsSelectable = true,
                        IsSelected = changed,
                        RowLabel = changed
                            ? string.Format(
                                "{0}    {1:F1} → {2:F1}  （差 {3:+0.0;-0.0;0.0}）",
                                name,
                                currentValue,
                                entry.Value,
                                delta)
                            : string.Format("{0}    {1:F1}", name, currentValue),
                    };

                    if (changed)
                    {
                        result.Changed.Add(row);
                    }
                    else
                    {
                        result.Unchanged.Add(row);
                    }
                }
            }

            // NOTE: インデックスは表示中のメッシュ内だけで使い、対応の有無は
            //       バックアップの名前集合から判定する。
            for (int i = 0; i < target.Mesh.blendShapeCount; i++)
            {
                string name = target.Mesh.GetBlendShapeName(i);
                if (backupNames.Contains(name))
                {
                    continue;
                }

                float currentValue = target.Renderer.GetBlendShapeWeight(i);
                result.MeshOnly.Add(new BlendShapeCompareRow
                {
                    Name = name,
                    Category = BlendShapeCompareCategory.MeshOnly,
                    MeshIndex = i,
                    BackupValue = 0f,
                    CurrentValue = currentValue,
                    IsSelectable = false,
                    RowLabel = string.Format(
                        "{0}    {1:F1}  （バックアップにこのキーがありません）",
                        name,
                        currentValue),
                });
            }

            RefreshLabels(result);
            return result;
        }

        internal static void RefreshLabels(BlendShapeCompareResult result)
        {
            if (result == null)
            {
                return;
            }

            result.SelectedCount = CountSelected(result.Changed) +
                CountSelected(result.Unchanged);
            result.ChangedHeader = string.Format(
                "値が違う（{0} 件）", result.Changed.Count);
            result.UnchangedHeader = string.Format(
                "値が同じ（{0} 件）", result.Unchanged.Count);
            result.BackupOnlyHeader = string.Format(
                "バックアップにのみ（{0} 件）", result.BackupOnly.Count);
            result.MeshOnlyHeader = string.Format(
                "メッシュにのみ（{0} 件）", result.MeshOnly.Count);

            int backupCount = result.Changed.Count + result.Unchanged.Count +
                result.BackupOnly.Count + result.Duplicated.Count;
            int meshCount = result.Changed.Count + result.Unchanged.Count +
                result.MeshOnly.Count;
            result.SummaryLabel = string.Format(
                "復元するキー: {0} 件 / バックアップ {1} 件・メッシュ {2} 件",
                result.SelectedCount,
                backupCount,
                meshCount);
        }

        internal static void SetSelection(
            List<BlendShapeCompareRow> rows, bool selected)
        {
            if (rows == null)
            {
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsSelectable)
                {
                    rows[i].IsSelected = selected;
                }
            }
        }

        private static int CountSelected(List<BlendShapeCompareRow> rows)
        {
            int count = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsSelectable && rows[i].IsSelected)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
