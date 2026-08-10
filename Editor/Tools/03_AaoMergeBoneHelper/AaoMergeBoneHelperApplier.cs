using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.AaoMergeBoneHelper.Editor
{
    internal static class AaoMergeBoneHelperApplier
    {
        private const string UndoName = "AAO Merge Bone Helper";

        internal static void Apply(
            AaoMergeBoneHelperPlan plan, out int added, out int removed)
        {
            added = 0;
            removed = 0;
            if (plan == null)
            {
                return;
            }

            var additions = new List<GameObject>();
            var removals = new List<Component>();
            var removalObjects = new List<GameObject>();
            for (int nodeIndex = 0; nodeIndex < plan.AllNodes.Count; nodeIndex++)
            {
                AaoMergeBoneNode node = plan.AllNodes[nodeIndex];
                if (node.Transform == null ||
                    node.BlockReason != AaoMergeBoneBlockReason.None)
                {
                    continue;
                }

                GameObject gameObject = node.Transform.gameObject;
                Component component = AaoMergeBoneType.Get(gameObject);
                if (node.Checked && component == null)
                {
                    additions.Add(gameObject);
                }
                else if (!node.Checked && component != null)
                {
                    removals.Add(component);
                    removalObjects.Add(gameObject);
                }
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                for (int additionIndex = 0; additionIndex < additions.Count; additionIndex++)
                {
                    GameObject gameObject = additions[additionIndex];
                    try
                    {
                        if (!AaoMergeBoneType.Has(gameObject) &&
                            AaoMergeBoneType.Add(gameObject) != null)
                        {
                            PrefabUtility.RecordPrefabInstancePropertyModifications(gameObject);
                            EditorUtility.SetDirty(gameObject);
                            added++;
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            "Candy Box: " + gameObject.name +
                            " への AAO Merge Bone 追加に失敗しました。\n" + exception,
                            gameObject);
                    }
                }

                for (int removalIndex = 0; removalIndex < removals.Count; removalIndex++)
                {
                    Component component = removals[removalIndex];
                    GameObject gameObject = removalObjects[removalIndex];
                    try
                    {
                        if (component != null)
                        {
                            AaoMergeBoneType.Remove(component);
                            PrefabUtility.RecordPrefabInstancePropertyModifications(gameObject);
                            EditorUtility.SetDirty(gameObject);
                            removed++;
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            "Candy Box: " + gameObject.name +
                            " からの AAO Merge Bone 削除に失敗しました。\n" + exception,
                            gameObject);
                    }
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }
    }
}
