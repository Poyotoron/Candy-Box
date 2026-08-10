namespace Poyo.CandyBox.Editor
{
    /// <summary>パッケージ共通の定数。</summary>
    internal static class CandyBoxInfo
    {
        internal const string PackageId = "net.maaaaa.candy-box";
        internal const string DisplayName = "Candy Box";
        internal const string Version = "0.4.0";

        // 設定ウィンドウの入口。ツールごとのメニュー項目は作らない。
        // NOTE: Unity は同じパスをメニュー項目と親フォルダの両方には使えないため、
        //       ここを親フォルダ化すると設定ウィンドウの入口が消える。
        internal const string MenuPath = "Tools/Candy Box";
        internal const int MenuPriority = 1000;

        // ツール NN を有効化する Scripting Define Symbol は接頭辞 + ID。
        internal const string DefineSymbolPrefix = "CANDY_BOX_TOOL_";
    }
}
