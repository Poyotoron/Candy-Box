using Poyo.CandyBox.Editor;
using UnityEditor;

namespace Poyo.CandyBox.BlendshapeKeeper.Editor
{
    /// <summary>このツールの入口を共通基盤へ登録する。</summary>
    // NOTE: 共通基盤側からこのアセンブリを参照すると、ツールを無効にした時点で
    //       共通基盤がコンパイルできなくなる。登録は必ずツール側から行う。
    internal static class BlendshapeKeeperEntry
    {
        internal const string ToolId = "00";

        [InitializeOnLoadMethod]
        private static void Register()
        {
            CandyBoxToolRegistry.Register(ToolId, BlendshapeKeeperWindow.Open);
        }
    }
}
