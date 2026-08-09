using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poyo.CandyBox.Editor
{
    /// <summary>有効なツールが自分の開き方を登録する窓口。</summary>
    // NOTE: Core からツールのアセンブリを参照すると、そのツールを無効にした時点で
    //       Core がコンパイルできなくなる。依存を逆向きにするためのレジストリ。
    public static class CandyBoxToolRegistry
    {
        private static readonly Dictionary<string, Action> Openers =
            new Dictionary<string, Action>();

        public static void Register(string toolId, Action opener)
        {
            if (string.IsNullOrEmpty(toolId))
            {
                Debug.LogError("Candy Box: ツール ID が指定されていないため登録できません。");
                return;
            }

            if (opener == null)
            {
                Debug.LogError("Candy Box: ツールの起動処理が指定されていないため登録できません。");
                return;
            }

            Openers[toolId] = opener;
        }

        internal static bool TryGetOpener(string toolId, out Action opener)
        {
            return Openers.TryGetValue(toolId, out opener);
        }
    }
}
