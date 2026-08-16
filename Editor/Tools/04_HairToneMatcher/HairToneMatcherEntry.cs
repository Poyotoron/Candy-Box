using Poyo.CandyBox.Editor;
using UnityEditor;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal static class HairToneMatcherEntry
    {
        internal const string ToolId = "04";

        [InitializeOnLoadMethod]
        private static void Register()
        {
            CandyBoxToolRegistry.Register(ToolId, HairToneMatcherWindow.Open);
        }
    }
}
