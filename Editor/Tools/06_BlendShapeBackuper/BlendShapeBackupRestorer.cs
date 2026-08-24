using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BlendShapeBackuper.Editor
{
    internal sealed class BlendShapeBackupPreview
    {
        private SkinnedMeshRenderer _renderer;
        private float[] _savedValues;

        internal bool IsActive { get; private set; }

        internal void Begin(
            BlendShapeBackupTarget target, BlendShapeCompareResult result)
        {
            if (IsActive)
            {
                End();
            }

            if (target == null || !target.IsValid || result == null)
            {
                return;
            }

            _renderer = target.Renderer;
            Mesh currentMesh = _renderer == null ? null : _renderer.sharedMesh;
            if (currentMesh == null)
            {
                _renderer = null;
                return;
            }

            int blendShapeCount = currentMesh.blendShapeCount;
            // NOTE: プレビュー中にチェックが変わっても確実に元へ戻せるよう、
            //       現在のメッシュにある全キーを退避する。
            _savedValues = new float[blendShapeCount];
            for (int i = 0; i < _savedValues.Length; i++)
            {
                _savedValues[i] = _renderer.GetBlendShapeWeight(i);
            }

            ApplyPreviewRows(_renderer, blendShapeCount, result.Changed);
            ApplyPreviewRows(_renderer, blendShapeCount, result.Unchanged);
            IsActive = true;
            SceneView.RepaintAll();
        }

        internal void End()
        {
            if (!IsActive)
            {
                return;
            }

            if (_renderer != null && _savedValues != null)
            {
                Mesh mesh = _renderer.sharedMesh;
                int currentCount = mesh == null ? 0 : mesh.blendShapeCount;
                // NOTE: プレビュー中にメッシュが差し替わっても範囲外へ
                //       書き込まないよう、退避値と現在の短いほうまで戻す。
                int restoreCount = Mathf.Min(_savedValues.Length, currentCount);
                for (int i = 0; i < restoreCount; i++)
                {
                    _renderer.SetBlendShapeWeight(i, _savedValues[i]);
                }
            }

            _renderer = null;
            _savedValues = null;
            IsActive = false;
            SceneView.RepaintAll();
        }

        private static void ApplyPreviewRows(
            SkinnedMeshRenderer renderer,
            int blendShapeCount,
            List<BlendShapeCompareRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                BlendShapeCompareRow row = rows[i];
                if (row.IsSelected && row.MeshIndex >= 0 &&
                    row.MeshIndex < blendShapeCount)
                {
                    // NOTE: プレビューは一時表示なので Undo とシーンの変更履歴には載せない。
                    renderer.SetBlendShapeWeight(row.MeshIndex, row.BackupValue);
                }
            }
        }
    }

    internal static class BlendShapeBackupRestorer
    {
        private const string RestoreUndoName = "Restore BlendShape Values";
        private const string PlayingLabel = "再生中は復元できません。";
        private const string NothingSelectedLabel =
            "復元するキーが選択されていません。";

        internal static BlendShapeRestoreResult Restore(
            BlendShapeBackupTarget target, BlendShapeCompareResult result)
        {
            var restoreResult = new BlendShapeRestoreResult();
            if (EditorApplication.isPlaying)
            {
                restoreResult.MessageLabel = PlayingLabel;
                return restoreResult;
            }

            if (target == null || !target.IsValid || result == null ||
                !HasSelectedRow(result))
            {
                restoreResult.MessageLabel = NothingSelectedLabel;
                return restoreResult;
            }

            SkinnedMeshRenderer renderer = target.Renderer;
            Mesh currentMesh = renderer == null ? null : renderer.sharedMesh;
            if (renderer == null || currentMesh == null)
            {
                restoreResult.MessageLabel = NothingSelectedLabel;
                return restoreResult;
            }

            var meshIndices = BuildMeshIndexMap(currentMesh);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(RestoreUndoName);
            int group = Undo.GetCurrentGroup();
            try
            {
                // NOTE: 元の値を 1 回の操作で戻せるよう、書き込み前に Renderer 全体を記録する。
                Undo.RecordObject(renderer, RestoreUndoName);
                RestoreRows(
                    renderer, result.Changed, meshIndices, restoreResult);
                RestoreRows(
                    renderer, result.Unchanged, meshIndices, restoreResult);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }

            restoreResult.Succeeded = true;
            restoreResult.MessageLabel = string.Format(
                "復元しました: {0} 件（飛ばした: {1} 件）",
                restoreResult.WrittenCount,
                restoreResult.SkippedCount);
            return restoreResult;
        }

        private static Dictionary<string, int> BuildMeshIndexMap(Mesh mesh)
        {
            var meshIndices = new Dictionary<string, int>();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                // NOTE: 同名キーでは保存・比較と同じく最初に現れたものを優先する。
                if (!meshIndices.ContainsKey(name))
                {
                    meshIndices[name] = i;
                }
            }

            return meshIndices;
        }

        private static void RestoreRows(
            SkinnedMeshRenderer renderer,
            List<BlendShapeCompareRow> rows,
            Dictionary<string, int> meshIndices,
            BlendShapeRestoreResult result)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                BlendShapeCompareRow row = rows[i];
                if (!row.IsSelected)
                {
                    continue;
                }

                int meshIndex;
                // NOTE: 比較後にメッシュが差し替わることがあるため、保持していた
                //       インデックスを使わず、書き込み直前に名前から引き直す。
                if (!meshIndices.TryGetValue(row.Name, out meshIndex))
                {
                    result.SkippedCount++;
                    continue;
                }

                renderer.SetBlendShapeWeight(meshIndex, row.BackupValue);
                result.WrittenCount++;
            }
        }

        private static bool HasSelectedRow(BlendShapeCompareResult result)
        {
            return HasSelectedRow(result.Changed) || HasSelectedRow(result.Unchanged);
        }

        private static bool HasSelectedRow(List<BlendShapeCompareRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsSelectable && rows[i].IsSelected)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
