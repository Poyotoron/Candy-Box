using Poyo.CandyBox.Editor;
using UnityEditor;

namespace Poyo.CandyBox.PartsSetAdjuster.Editor
{
    internal static class PartsSetAdjusterEntry
    {
        internal const string ToolId = "07";
        [InitializeOnLoadMethod]
        private static void Register()
        {
            CandyBoxToolRegistry.Register(ToolId, PartsSetAdjusterWindow.Open);
        }
    }
}
