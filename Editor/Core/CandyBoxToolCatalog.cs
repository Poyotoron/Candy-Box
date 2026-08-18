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

#if CANDY_BOX_HAS_AAO
        private static readonly bool AvatarOptimizerAvailable = true;
#else
        private static readonly bool AvatarOptimizerAvailable = false;
#endif

        private const string ModularAvatarRequirement =
            "Modular Avatar 1.17.0 以降";

        private const string AvatarOptimizerRequirement =
            "AAO: Avatar Optimizer 1.9.0 以降";

        private static readonly string BlendshapeKeeperDescription =
            @"メッシュに設定したブレンドシェイプの現在値と、アニメーションのキー値を比べ、
キー値のほうが小さい場合にキー値を現在値まで引き上げます。
表情を再生した瞬間に顔の改変が打ち消されるのを防ぐためのツールです。";

        private static readonly string MaBlendshapeSyncHelperDescription =
            @"衣装のシェイプキーを素体のシェイプキーへ追従させる設定を、まとめて作ります。
素体と同じ名前のシェイプキーを衣装側から探し、チェックを付けたものだけを
MA Blendshape Sync の設定として書き込みます。名前が違うものは手動で紐付けられます。";

        private static readonly string AaoMergePhysBoneHelperDescription =
            @"複数の PhysBone を 1 つに統合するとき、値が食い違うプロパティを洗い出します。
統合対象それぞれの値を一覧にし、最小・最大・平均・中央・最頻から選んだ値を
AAO Merge PhysBone の override として書き込みます。カーブにも対応しています。";

        private static readonly string AaoMergeBoneHelperDescription =
            @"指定したオブジェクト配下のボーンをツリーで一覧し、
どのボーンを親へ統合するかをチェックボックスでまとめて切り替えます。
ボーンチェーンを一定間隔で間引く設定も自動で作れます。";

        private static readonly string HairToneMatcherDescription =
            @"髪を差し替えたときに生じる色味の差を埋めます。
元の髪と新しい髪のテクスチャの色を比べ、新しい髪を元の髪に寄せる補正値を提案します。
lilToon と Poiyomi のマテリアルに対応します。導入は必須ではありません。";

        private static readonly string BoneWeightCollapserDescription =
            @"指定したボーンのウェイトを、別のボーンへまとめて移します。
袖が指ボーンに引きずられて変形する、といった追従の問題を解消するためのツールです。
結果は新しいメッシュとして保存し、元のメッシュには書き込みません。";

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
            new CandyBoxToolEntry(
                "02",
                "02_Helper for AAO Merge PhysBone",
                "統合する PhysBone の値を比べ、override する値を提案します",
                AaoMergePhysBoneHelperDescription,
                AvatarOptimizerRequirement,
                AvatarOptimizerAvailable),
            new CandyBoxToolEntry(
                "03",
                "03_Helper for AAO Merge Bone",
                "ボーンを親へ統合する設定を、ツリーからまとめて切り替えます",
                AaoMergeBoneHelperDescription,
                AvatarOptimizerRequirement,
                AvatarOptimizerAvailable),
            new CandyBoxToolEntry(
                "04",
                "04_Hair Tone Matcher",
                "差し替えた髪の色味を、元の髪に合わせます",
                HairToneMatcherDescription),
            new CandyBoxToolEntry(
                "05",
                "05_Bone Weight Collapser",
                "不要なボーンのウェイトを、別のボーンへ寄せます",
                BoneWeightCollapserDescription),
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
