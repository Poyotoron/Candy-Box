namespace Poyo.CandyBox.Editor
{
    internal sealed class CandyBoxToolEntry
    {
        internal readonly string Id;
        internal readonly string DisplayName;
        internal readonly string Summary;
        internal readonly string Description;

        private readonly string _defineSymbol;

        internal CandyBoxToolEntry(string id, string displayName, string summary, string description)
        {
            Id = id;
            DisplayName = displayName;
            Summary = summary;
            Description = description;
            _defineSymbol = CandyBoxInfo.DefineSymbolPrefix + id;
        }

        internal string DefineSymbol
        {
            get { return _defineSymbol; }
        }
    }

    internal static class CandyBoxToolCatalog
    {
        private static readonly string BlendshapeKeeperDescription =
            @"メッシュに設定したブレンドシェイプの現在値と、アニメーションのキー値を比べ、
キー値のほうが小さい場合にキー値を現在値まで引き上げます。
表情を再生した瞬間に顔の改変が打ち消されるのを防ぐためのツールです。";

        internal static readonly CandyBoxToolEntry[] Tools =
        {
            new CandyBoxToolEntry(
                "00",
                "00_Blendshape Keeper",
                "表情アニメで改変したブレンドシェイプが戻るのを防ぎます",
                BlendshapeKeeperDescription),
        };

        internal static CandyBoxToolEntry Find(string id)
        {
            for (int i = 0; i < Tools.Length; i++)
            {
                if (Tools[i].Id == id)
                {
                    return Tools[i];
                }
            }

            return null;
        }
    }
}
