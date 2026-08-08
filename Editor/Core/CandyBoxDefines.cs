using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Poyo.CandyBox.Editor
{
    internal static class CandyBoxDefines
    {
        // NOTE: プラットフォームを切り替えたときにツールが消えないよう、
        //       VRChat がビルド対象にする 3 つすべてへ同じシンボルを書く。
        private static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.Standalone,
            NamedBuildTarget.Android,
            NamedBuildTarget.iOS,
        };

        /// <summary>指定シンボルそれぞれの有無を 1 回の読み取りで判定する。</summary>
        internal static void GetDefined(IReadOnlyList<string> symbols, bool[] results)
        {
            if (symbols == null || results == null || symbols.Count != results.Length)
            {
                Debug.LogError("Candy Box: 有効状態の読み取り先が正しくありません。");
                return;
            }

            PlayerSettings.GetScriptingDefineSymbols(
                NamedBuildTarget.Standalone, out string[] defines);
            for (int symbolIndex = 0; symbolIndex < symbols.Count; symbolIndex++)
            {
                results[symbolIndex] = Array.IndexOf(defines, symbols[symbolIndex]) >= 0;
            }
        }

        /// <summary>複数のシンボルの有無を 1 回の書き込みで反映する。</summary>
        // NOTE: シンボルを 1 つずつ書くと書き込みのたびに再コンパイルが走るため、
        //       まとめて 1 回で反映する。
        internal static void SetDefined(
            IReadOnlyList<string> symbols, IReadOnlyList<bool> enabled)
        {
            if (symbols == null || enabled == null || symbols.Count != enabled.Count)
            {
                Debug.LogError("Candy Box: シンボルと有効状態の数が一致しません。");
                return;
            }

            for (int targetIndex = 0; targetIndex < Targets.Length; targetIndex++)
            {
                NamedBuildTarget target = Targets[targetIndex];
                PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
                var updated = new List<string>(defines);
                bool changed = false;

                for (int symbolIndex = 0; symbolIndex < symbols.Count; symbolIndex++)
                {
                    string symbol = symbols[symbolIndex];
                    if (enabled[symbolIndex])
                    {
                        if (!updated.Contains(symbol))
                        {
                            updated.Add(symbol);
                            changed = true;
                        }
                    }
                    else
                    {
                        while (updated.Remove(symbol))
                        {
                            changed = true;
                        }
                    }
                }

                if (!changed)
                {
                    continue;
                }

                PlayerSettings.SetScriptingDefineSymbols(target, updated.ToArray());
            }
        }
    }
}
