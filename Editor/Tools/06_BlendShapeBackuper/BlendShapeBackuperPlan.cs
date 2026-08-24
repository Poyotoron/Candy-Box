using System.Collections.Generic;
using Poyo.CandyBox.Editor;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BlendShapeBackuper.Editor
{
    internal enum BlendShapeTargetBlockReason
    {
        None,
        MissingRenderer,
        MissingMesh,
        NoBlendShape,
    }

    internal sealed class BlendShapeBackupTarget
    {
        internal SkinnedMeshRenderer Renderer;
        internal Mesh Mesh;
        internal int BlendShapeCount;
        internal int VertexCount;

        /// <summary>元メッシュのアセット GUID。取れないときは空文字。</summary>
        internal string MeshGuid;

        /// <summary>元メッシュの localFileIdentifier。取れないときは 0。</summary>
        internal long MeshFileId;

        internal string MeshName;
        internal string MeshPath;
        internal string RendererPath;

        /// <summary>ウィンドウに出す 1 行の要約。解決時に組み立てる。</summary>
        internal string SummaryLabel;

        internal BlendShapeTargetBlockReason BlockReason;
        internal string BlockedLabel;

        internal bool IsValid => BlockReason == BlendShapeTargetBlockReason.None;
    }

    internal sealed class BlendShapeBackupListItem
    {
        internal BlendShapeBackupAsset Asset;
        internal string AssetPath;

        /// <summary>対象メッシュと GUID / fileId が一致するか。</summary>
        internal bool MetaMatches;

        /// <summary>一覧に出す 1 行。探索時に組み立てる。</summary>
        internal string RowLabel;

        /// <summary>行の下に小さく出す補足（元メッシュ名とパス）。探索時に組み立てる。</summary>
        internal string SubLabel;

        /// <summary>並べ替えのキー。CapturedAt の文字列をそのまま使う。</summary>
        internal string SortKey;
    }

    internal enum BlendShapeCompareCategory
    {
        Changed,
        Unchanged,
        BackupOnly,
        MeshOnly,
    }

    internal sealed class BlendShapeCompareRow
    {
        internal string Name;
        internal BlendShapeCompareCategory Category;

        /// <summary>メッシュ側のインデックス。MeshOnly / 両方にある行で有効。無ければ -1。</summary>
        internal int MeshIndex = -1;

        internal float BackupValue;
        internal float CurrentValue;
        internal float Delta;

        /// <summary>チェックできるか。両方にある行だけ true。</summary>
        internal bool IsSelectable;

        internal bool IsSelected;

        /// <summary>同じ名前が重複していて、2 件目以降として無視された行か。</summary>
        internal bool IsDuplicate;

        /// <summary>一覧に出す 1 行。比較時に組み立てる。</summary>
        internal string RowLabel;
    }

    internal sealed class BlendShapeCompareResult
    {
        internal BlendShapeBackupAsset Backup;

        /// <summary>区分ごとの行。表示順はこのリストの順。</summary>
        internal List<BlendShapeCompareRow> Changed = new List<BlendShapeCompareRow>();
        internal List<BlendShapeCompareRow> Unchanged = new List<BlendShapeCompareRow>();
        internal List<BlendShapeCompareRow> BackupOnly = new List<BlendShapeCompareRow>();
        internal List<BlendShapeCompareRow> MeshOnly = new List<BlendShapeCompareRow>();

        /// <summary>重複した名前の行。件数の表示にだけ使う。</summary>
        internal List<BlendShapeCompareRow> Duplicated = new List<BlendShapeCompareRow>();

        /// <summary>区分ごとの折りたたみ状態。比較のたびに既定へ戻す。</summary>
        internal bool ChangedExpanded = true;
        internal bool UnchangedExpanded;
        internal bool BackupOnlyExpanded = true;
        internal bool MeshOnlyExpanded = true;

        /// <summary>meta が一致しないバックアップを選んでいるか。</summary>
        internal bool MetaMismatch;

        /// <summary>見出しに出す件数の要約。比較時に組み立てる。</summary>
        internal string SummaryLabel;

        /// <summary>区分ごとの見出し。比較時に組み立てる。</summary>
        internal string ChangedHeader;
        internal string UnchangedHeader;
        internal string BackupOnlyHeader;
        internal string MeshOnlyHeader;

        internal int SelectedCount;
    }

    internal sealed class BlendShapeRestoreResult
    {
        internal bool Succeeded;
        internal int WrittenCount;
        internal int SkippedCount;

        /// <summary>ウィンドウに出す結果の文字列。ここで組み立てる。</summary>
        internal string MessageLabel;
    }

    internal static class BlendShapeBackupTargetResolver
    {
        private const string MissingRendererLabel = "対象のメッシュを指定してください。";
        private const string MissingMeshLabel = "メッシュが設定されていません。";
        private const string NoBlendShapeLabel =
            "このメッシュにはブレンドシェイプがありません。";

        internal static BlendShapeBackupTarget Resolve(SkinnedMeshRenderer renderer)
        {
            var target = new BlendShapeBackupTarget
            {
                Renderer = renderer,
                MeshGuid = string.Empty,
                MeshPath = string.Empty,
            };

            if (renderer == null)
            {
                target.BlockReason = BlendShapeTargetBlockReason.MissingRenderer;
                target.BlockedLabel = MissingRendererLabel;
                return target;
            }

            Mesh mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                target.BlockReason = BlendShapeTargetBlockReason.MissingMesh;
                target.BlockedLabel = MissingMeshLabel;
                return target;
            }

            if (mesh.blendShapeCount == 0)
            {
                target.BlockReason = BlendShapeTargetBlockReason.NoBlendShape;
                target.BlockedLabel = NoBlendShapeLabel;
                return target;
            }

            target.Mesh = mesh;
            target.BlendShapeCount = mesh.blendShapeCount;
            target.VertexCount = mesh.vertexCount;
            target.MeshName = mesh.name;
            target.RendererPath = BuildRendererPath(renderer.transform);

            string guid;
            long fileId;
            // NOTE: シーン内で生成されたメッシュなどはアセット情報を持たないが、
            //       値の保存自体はできるため対象として扱う。
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                mesh, out guid, out fileId))
            {
                target.MeshGuid = guid;
                target.MeshFileId = fileId;
                target.MeshPath = AssetDatabase.GetAssetPath(mesh);
            }
            else
            {
                target.MeshGuid = string.Empty;
                target.MeshFileId = 0;
                target.MeshPath = string.Empty;
            }

            target.SummaryLabel = string.Format(
                "{0}（ブレンドシェイプ {1} 個 / 頂点 {2} 個）",
                target.MeshName,
                target.BlendShapeCount,
                target.VertexCount);
            target.BlockReason = BlendShapeTargetBlockReason.None;
            target.BlockedLabel = string.Empty;
            return target;
        }

        private static string BuildRendererPath(Transform transform)
        {
            var names = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }
    }
}
