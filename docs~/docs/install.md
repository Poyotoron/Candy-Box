# インストール

## 動作環境

| 項目 | 要件 |
|---|---|
| Unity | 2022.3 (LTS) |
| VRChat SDK | 不要（入っていても問題ありません） |
| 対応 | エディタ専用。実行時のアセンブリを持ちません |

!!! note "パッケージ自体には必須の依存パッケージがありません"
    一部のツールだけが外部パッケージと連携します。未導入でもパッケージ全体は問題なく動き、
    そのツールだけが有効化できない状態になります。

| ツール | 必要なパッケージ |
|---|---|
| 01_Helper for MA Blendshape Sync | Modular Avatar 1.17.0 以降 |
| 02_Helper for AAO Merge PhysBone | AAO: Avatar Optimizer 1.9.0 以降 |
| 03_Helper for AAO Merge Bone | AAO: Avatar Optimizer 1.9.0 以降 |

04_Hair Tone Matcher に必要なパッケージはありません。
ただし、色を合わせる先（新しい髪）のマテリアルが **lilToon** または **Poiyomi** である必要があります。

## VCC / ALCOM から導入する（推奨）

1. VCC または ALCOM に、配布元のリポジトリを追加します。
2. 対象の Unity プロジェクトを開き、**Manage Packages** を選びます。
3. 一覧から「Candy Box」を追加します。

## unitypackage から導入する

VCC を使っていない場合は、[リリースページ](https://github.com/Poyotoron/Candy-Box/releases)から
`.unitypackage` をダウンロードし、Unity のプロジェクトへドラッグ＆ドロップしてインポートしてください。

## 導入できたか確認する

Unity のメニューに次の項目が増えていればインストール成功です。

```
Tools > Candy Box
```

クリックすると設定ウィンドウが開きます。

!!! warning "導入直後は、すべてのツールが無効です"
    ツールのウィンドウを開くには、まず[設定ウィンドウ](settings.md)で有効にする必要があります。
    メニューに増えるのは `Tools > Candy Box` の 1 項目だけで、ツールごとのメニュー項目はありません。

## 更新する

VCC / ALCOM から更新するか、新しい `.unitypackage` をインポートしてください。

有効にしているツールの状態は Unity のプロジェクト設定に保存されているため、
更新しても選択は保たれます。
