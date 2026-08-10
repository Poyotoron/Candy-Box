using System.Collections.Generic;
using UnityEngine;

namespace Poyo.CandyBox.AaoMergeBoneHelper.Editor
{
    internal static class AaoMergeBoneChainPlanner
    {
        internal static List<AaoMergeBoneNode> CollectChain(
            AaoMergeBoneNode start, out string note)
        {
            note = null;
            var chain = new List<AaoMergeBoneNode>();
            AaoMergeBoneNode current = start;
            if (current == null)
            {
                return chain;
            }

            chain.Add(current);
            while (true)
            {
                AaoMergeBoneNode next = null;
                int childCount = 0;
                for (int childIndex = 0; childIndex < current.Children.Count; childIndex++)
                {
                    AaoMergeBoneNode child = current.Children[childIndex];
                    if (child.BlockReason == AaoMergeBoneBlockReason.EditorOnly)
                    {
                        continue;
                    }

                    childCount++;
                    next = child;
                }

                if (childCount == 0)
                {
                    return chain;
                }

                if (childCount > 1)
                {
                    note = string.Format(
                        "{0} で枝分かれしているため、そこで止めました。",
                        current.Label);
                    return chain;
                }

                current = next;
                chain.Add(current);
            }
        }

        internal static int Apply(
            List<AaoMergeBoneNode> chain, int keepInterval, out int skipped)
        {
            skipped = 0;
            int checkedCount = 0;
            keepInterval = Mathf.Max(2, keepInterval);
            for (int nodeIndex = 0; nodeIndex < chain.Count; nodeIndex++)
            {
                AaoMergeBoneNode node = chain[nodeIndex];
                if (node.BlockReason != AaoMergeBoneBlockReason.None)
                {
                    skipped++;
                    continue;
                }

                node.Checked = nodeIndex % keepInterval != 0;
                if (node.Checked)
                {
                    checkedCount++;
                }
            }

            return checkedCount;
        }
    }
}
