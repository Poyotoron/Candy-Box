using System.Collections.Generic;
using UnityEngine;

namespace Poyo.CandyBox.Editor
{
    /// <summary>ブレンドシェイプ 1 件分のキー名と値。</summary>
    [System.Serializable]
    public sealed class BlendShapeBackupEntry
    {
        public string Name;
        public float Value;
    }

    /// <summary>ブレンドシェイプの値のバックアップ。</summary>
    // NOTE: ツールを無効にするとそのアセンブリはコンパイルされない。この型をツール側に置くと
    //       無効化した時点で保存済みのアセットが読めなくなるため、常時コンパイルされる側に置く。
    public sealed class BlendShapeBackupAsset : ScriptableObject
    {
        public string DisplayName;

        /// <summary>保存日時。yyyy-MM-dd HH:mm:ss 形式。</summary>
        public string CapturedAt;

        public string SourceMeshGuid;
        public long SourceMeshFileId;
        public string SourceMeshName;
        public string SourceMeshPath;
        public int VertexCount;
        public int BlendShapeCount;

        /// <summary>保存元のヒエラルキーパス。表示専用。</summary>
        public string RendererPath;

        public List<BlendShapeBackupEntry> Entries = new List<BlendShapeBackupEntry>();
    }
}
