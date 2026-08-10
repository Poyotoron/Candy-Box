namespace Poyo.CandyBox.Editor
{
    internal sealed class CandyBoxToolEntry
    {
        internal readonly string Id;
        internal readonly string DisplayName;
        internal readonly string Summary;
        internal readonly string Description;
        internal readonly string RequirementLabel;
        internal readonly bool IsAvailable;

        private readonly string _defineSymbol;

        internal CandyBoxToolEntry(string id, string displayName, string summary, string description)
        {
            Id = id;
            DisplayName = displayName;
            Summary = summary;
            Description = description;
            RequirementLabel = null;
            IsAvailable = true;
            _defineSymbol = CandyBoxInfo.DefineSymbolPrefix + id;
        }

        internal CandyBoxToolEntry(
            string id,
            string displayName,
            string summary,
            string description,
            string requirementLabel,
            bool isAvailable)
        {
            Id = id;
            DisplayName = displayName;
            Summary = summary;
            Description = description;
            RequirementLabel = requirementLabel;
            IsAvailable = isAvailable;
            _defineSymbol = CandyBoxInfo.DefineSymbolPrefix + id;
        }

        internal string DefineSymbol
        {
            get { return _defineSymbol; }
        }
    }

    internal static class CandyBoxToolCatalog
    {
        // NOTE: const bool にすると片方の分岐が到達不能になるため、読み取り専用値にする。
#if CANDY_BOX_HAS_MA
        private static readonly bool ModularAvatarAvailable = true;
#else
        private static readonly bool ModularAvatarAvailable = false;
#endif

        private const string ModularAvatarRequirement =
            "Modular Avatar 1.17.0 以降";

        private static readonly string BlendshapeKeeperDescription =
            @"メッシュに設定したブレンドシェイプの現在値と、アニメーションのキー値を比べ、
キー値のほうが小さい場合にキー値を現在値まで引き上げます。
表情を再生した瞬間に顔の改変が打ち消されるのを防ぐためのツールです。";

        private static readonly string MaBlendshapeSyncHelperDescription =
            @"衣装のシェイプキーを素体のシェイプキーへ追従させる設定を、まとめて作ります。
素体と同じ名前のシェイプキーを衣装側から探し、チェックを付けたものだけを
MA Blendshape Sync の設定として書き込みます。名前が違うものは手動で紐付けられます。";

        internal static readonly CandyBoxToolEntry[] Tools =
        {
            new CandyBoxToolEntry(
                "00",
                "00_Blendshape Keeper",
                "表情アニメで改変したブレンドシェイプが戻るのを防ぎます",
                BlendshapeKeeperDescription),
            new CandyBoxToolEntry(
                "01",
                "01_Helper for MA Blendshape Sync",
                "衣装のシェイプキーを素体へ追従させる設定をまとめて作ります",
                MaBlendshapeSyncHelperDescription,
                ModularAvatarRequirement,
                ModularAvatarAvailable),
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
