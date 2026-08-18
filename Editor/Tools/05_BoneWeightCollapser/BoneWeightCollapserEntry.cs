using Poyo.CandyBox.Editor;
using UnityEditor;

namespace Poyo.CandyBox.BoneWeightCollapser.Editor
{
    internal static class BoneWeightCollapserEntry
    {
        internal const string ToolId = "05";

        [InitializeOnLoadMethod]
        private static void Register()
        {
            CandyBoxToolRegistry.Register(ToolId, BoneWeightCollapserWindow.Open);
        }
    }
}
