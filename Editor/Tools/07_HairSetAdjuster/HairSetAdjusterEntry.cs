using Poyo.CandyBox.Editor;
using UnityEditor;

namespace Poyo.CandyBox.HairSetAdjuster.Editor
{
    internal static class HairSetAdjusterEntry
    {
        internal const string ToolId = "07";

        [InitializeOnLoadMethod]
        private static void Register()
        {
            CandyBoxToolRegistry.Register(ToolId, HairSetAdjusterWindow.Open);
        }
    }
}
