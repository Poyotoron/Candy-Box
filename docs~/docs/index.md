# はじめに

**Candy Box** は、VRChat のアバター改変で使う**小さな Unity Editor 拡張の詰め合わせ**です。

「あると便利だけれど、単体でパッケージにするほどではない」道具をひとつにまとめ、
**使うものだけを有効にして**利用します。

## 使うツールだけを有効にします

導入した直後は、**すべてのツールが無効**です。
設定ウィンドウでチェックを入れて「適用」を押したものだけがコンパイルされます。

!!! success "使わないツールは、コンパイル時間に一切影響しません"
    無効なツールはスクリプトのコンパイル対象から完全に外れます。
    「全部入りだから重い」ということが起きないように、この仕組みにしています。

有効・無効の状態は Unity のプロジェクト設定に保存されるため、
バージョン管理でチームに共有されます。

## 収録しているツール

| ツール | 何をするか | 追加で必要なもの |
|---|---|---|
| [00_Blendshape Keeper](blendshape-keeper.md) | 表情アニメーションで改変したブレンドシェイプが元に戻るのを防ぐ | なし |
| [01_Helper for MA Blendshape Sync](ma-blendshape-sync.md) | 衣装のシェイプキーを素体へ追従させる設定をまとめて作る | Modular Avatar 1.17.0 以降 |
| [02_Helper for AAO Merge PhysBone](merge-physbone.md) | 統合する PhysBone の値を比べ、override 値を提案する | AAO: Avatar Optimizer 1.9.0 以降 |
| [03_Helper for AAO Merge Bone](merge-bone.md) | ボーンの統合設定をツリーからまとめて切り替える | AAO: Avatar Optimizer 1.9.0 以降 |
| [04_Hair Tone Matcher](hair-tone-matcher.md) | 差し替えた髪の色味を元の髪へ近づける | なし（lilToon または Poiyomi のマテリアルが対象） |
| [05_Bone Weight Collapser](bone-weight-collapser.md) | 不要なボーンのウェイトを別のボーンへ寄せる | なし |

## 共通する考え方

このパッケージのツールは、次の約束のもとで動きます。

!!! info "確認してから「適用」を押すまで、何も変わりません"
    どのツールも「走査 → 内容の確認 → 適用」の 3 段階です。
    ウィンドウを開いただけ、走査しただけでは、シーンにもアセットにも一切書き込みません。

| 約束 | 内容 |
|---|---|
| 独自のコンポーネントを増やさない | Candy Box 専用のコンポーネントをアバターに付けることはありません |
| 実行時には何もしない | すべてエディタ専用です。ビルド後の挙動には関与しません |
| 元に戻せる | 適用した変更は「元に戻す（Undo）」で取り消せます（一部の例外は各ページに明記しています） |
| 勝手にファイルを作らない | ファイルを生成するのは、出力先を指定したときだけです |

!!! warning "重要なプロジェクトでは、事前のバックアップを推奨します"
    Undo で戻せる操作でも、Unity の再起動やスクリプトの再コンパイルをまたぐと戻せなくなります。
    大切な作業の前には、バージョン管理でのコミットやプロジェクトの複製をおすすめします。

## どこから読むか

| 目的 | ページ |
|---|---|
| 導入したい | [インストール](install.md) |
| ツールの有効・無効を切り替えたい | [設定ウィンドウ](settings.md) |
| 表情で改変が戻ってしまう | [00_Blendshape Keeper](blendshape-keeper.md) |
| 衣装が素体の体型に追従しない | [01_Helper for MA Blendshape Sync](ma-blendshape-sync.md) |
| PhysBone の統合で override を求められた | [02_Helper for AAO Merge PhysBone](merge-physbone.md) |
| ボーンの統合設定が多くて管理できない | [03_Helper for AAO Merge Bone](merge-bone.md) |
| 差し替えた髪の色が合わない | [04_Hair Tone Matcher](hair-tone-matcher.md) |
| 袖や衣装が指などのボーンに引きずられる | [05_Bone Weight Collapser](bone-weight-collapser.md) |
| うまく動かない | [困ったときは](troubleshooting.md) |
