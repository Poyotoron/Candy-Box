using Poyo.CandyBox.Editor;
using UnityEditor;

namespace Poyo.CandyBox.BlendShapeBackuper.Editor
{
    internal static class BlendShapeBackuperEntry
    {
        internal const string ToolId = "06";

        [InitializeOnLoadMethod]
        private static void Register()
        {
            CandyBoxToolRegistry.Register(ToolId, BlendShapeBackuperWindow.Open);
        }
    }
}
