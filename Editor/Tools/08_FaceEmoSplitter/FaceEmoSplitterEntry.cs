using Poyo.CandyBox.Editor;
using UnityEditor;

namespace Poyo.CandyBox.FaceEmoSplitter.Editor
{
    internal static class FaceEmoSplitterEntry
    {
        internal const string ToolId = "08";

        [InitializeOnLoadMethod]
        private static void Register()
        {
            CandyBoxToolRegistry.Register(ToolId, FaceEmoSplitterWindow.Open);
        }
    }
}
