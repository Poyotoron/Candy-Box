using Poyo.CandyBox.Editor;
using UnityEditor;

namespace Poyo.CandyBox.AaoMergeBoneHelper.Editor
{
    /// <summary>このツールの入口を共通基盤へ登録する。</summary>
    // NOTE: 共通基盤から任意ツールを参照せず、無効時も共通基盤を成立させる。
    internal static class AaoMergeBoneHelperEntry
    {
        internal const string ToolId = "03";

        [InitializeOnLoadMethod]
        private static void Register()
        {
            CandyBoxToolRegistry.Register(ToolId, AaoMergeBoneHelperWindow.Open);
        }
    }
}
