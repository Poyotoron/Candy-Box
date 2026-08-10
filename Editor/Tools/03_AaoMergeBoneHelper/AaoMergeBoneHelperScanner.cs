using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.AaoMergeBoneHelper.Editor
{
    internal static class AaoMergeBoneHelperScanner
    {
        internal static readonly string[] BlockReasonTexts =
        {
            string.Empty,
            "アバタールートは統合できません",
            "人型ボーンは統合できません",
            "EditorOnly のため対象外です",
        };

        private static readonly string[] WarningTexts =
        {
            "Transform 以外のコンポーネントがあります",
            "スケールが均一ではありません",
            "アニメーションで動かされています",
        };

        private const string SelectTooltip = "クリックすると選択します";

        internal static AaoMergeBoneHelperPlan Scan(
            GameObject target, GameObject avatarRoot)
        {
            var plan = new AaoMergeBoneHelperPlan
            {
                AvatarRoot = avatarRoot,
                AnimationScanned = avatarRoot != null,
            };
            HashSet<Transform> humanoidBones = CollectHumanoidBones(avatarRoot);
            HashSet<string> animatedPaths = avatarRoot != null
                ? AaoMergeBoneAnimationUsage.Collect(avatarRoot)
                : new HashSet<string>();
            plan.Root = BuildNode(
                target.transform,
                null,
                0,
                false,
                plan,
                avatarRoot,
                humanoidBones,
                animatedPaths);

            RefreshAllDynamicState(plan);
            int configuredCount = 0;
            for (int nodeIndex = 0; nodeIndex < plan.AllNodes.Count; nodeIndex++)
            {
                if (plan.AllNodes[nodeIndex].HasComponentInitially)
                {
                    configuredCount++;
                }
            }

            plan.CountText = string.Format(
                "ボーン {0} 件 / 統合できる {1} 件 / 設定済み {2} 件",
                plan.AllNodes.Count,
                plan.MergeableCount,
                configuredCount);
            BuildStartChoices(plan, target.transform);
            return plan;
        }

        internal static void CarryOverViewState(
            AaoMergeBoneHelperPlan previous,
            AaoMergeBoneHelperPlan next)
        {
            if (previous == null || next == null)
            {
                return;
            }

            for (int nextIndex = 0; nextIndex < next.AllNodes.Count; nextIndex++)
            {
                AaoMergeBoneNode nextNode = next.AllNodes[nextIndex];
                AaoMergeBoneNode previousNode = FindNode(previous, nextNode.Transform);
                if (previousNode != null)
                {
                    nextNode.Expanded = previousNode.Expanded;
                }
            }
        }

        internal static void RefreshAfterCheckChange(
            AaoMergeBoneHelperPlan plan, AaoMergeBoneNode changedNode)
        {
            AaoMergeBoneNode current = changedNode.Parent;
            while (current != null)
            {
                RefreshUnevenScale(current);
                BuildStatusText(current);
                current = current.Parent;
            }

            RefreshChangeCounts(plan);
        }

        internal static void RefreshAllDynamicState(AaoMergeBoneHelperPlan plan)
        {
            for (int nodeIndex = plan.AllNodes.Count - 1; nodeIndex >= 0; nodeIndex--)
            {
                AaoMergeBoneNode node = plan.AllNodes[nodeIndex];
                RefreshUnevenScale(node);
                BuildStatusText(node);
            }

            RefreshChangeCounts(plan);
        }

        internal static AaoMergeBoneNode FindNode(
            AaoMergeBoneHelperPlan plan, Transform transform)
        {
            if (plan == null || transform == null)
            {
                return null;
            }

            for (int nodeIndex = 0; nodeIndex < plan.AllNodes.Count; nodeIndex++)
            {
                if (plan.AllNodes[nodeIndex].Transform == transform)
                {
                    return plan.AllNodes[nodeIndex];
                }
            }

            return null;
        }

        private static void BuildStartChoices(
            AaoMergeBoneHelperPlan plan, Transform target)
        {
            plan.StartChoicePaths = new string[plan.AllNodes.Count];
            for (int nodeIndex = 0; nodeIndex < plan.AllNodes.Count; nodeIndex++)
            {
                AaoMergeBoneNode node = plan.AllNodes[nodeIndex];
                string path = AnimationUtility.CalculateTransformPath(node.Transform, target);
                node.TargetRelativePath = string.IsNullOrEmpty(path) ? node.Label : path;
                plan.StartChoicePaths[nodeIndex] = node.TargetRelativePath;
            }
        }

        private static AaoMergeBoneNode BuildNode(
            Transform transform,
            AaoMergeBoneNode parent,
            int depth,
            bool ancestorEditorOnly,
            AaoMergeBoneHelperPlan plan,
            GameObject avatarRoot,
            HashSet<Transform> humanoidBones,
            HashSet<string> animatedPaths)
        {
            bool isEditorOnly = ancestorEditorOnly || transform.CompareTag("EditorOnly");
            Component mergeBone = AaoMergeBoneType.Get(transform.gameObject);
            bool hasComponent = mergeBone != null;
            AaoMergeBoneBlockReason blockReason = GetBlockReason(
                transform, avatarRoot, humanoidBones, isEditorOnly);
            string label = transform.name;
            var node = new AaoMergeBoneNode
            {
                Transform = transform,
                Parent = parent,
                Depth = depth,
                HasComponentInitially = hasComponent,
                Checked = hasComponent && blockReason == AaoMergeBoneBlockReason.None,
                BlockReason = blockReason,
                AvoidNameConflict = hasComponent
                    ? AaoMergeBoneType.GetAvoidNameConflict(mergeBone)
                    : true,
                Label = label,
                LabelContent = new GUIContent(label, SelectTooltip),
            };
            if (blockReason == AaoMergeBoneBlockReason.None)
            {
                plan.MergeableCount++;
            }

            if (HasAdditionalComponents(transform.gameObject, mergeBone))
            {
                node.Warnings |= AaoMergeBoneWarning.HasComponents;
            }

            if (avatarRoot != null)
            {
                node.AnimationPath = AnimationUtility.CalculateTransformPath(
                    transform, avatarRoot.transform);
                if (animatedPaths.Contains(node.AnimationPath))
                {
                    node.Warnings |= AaoMergeBoneWarning.Animated;
                }
            }

            plan.AllNodes.Add(node);
            for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
            {
                AaoMergeBoneNode child = BuildNode(
                    transform.GetChild(childIndex),
                    node,
                    depth + 1,
                    isEditorOnly,
                    plan,
                    avatarRoot,
                    humanoidBones,
                    animatedPaths);
                node.Children.Add(child);
            }

            return node;
        }

        private static AaoMergeBoneBlockReason GetBlockReason(
            Transform transform,
            GameObject avatarRoot,
            HashSet<Transform> humanoidBones,
            bool isEditorOnly)
        {
            if (avatarRoot != null && transform == avatarRoot.transform)
            {
                return AaoMergeBoneBlockReason.AvatarRoot;
            }

            if (humanoidBones.Contains(transform))
            {
                return AaoMergeBoneBlockReason.HumanoidBone;
            }

            return isEditorOnly
                ? AaoMergeBoneBlockReason.EditorOnly
                : AaoMergeBoneBlockReason.None;
        }

        private static HashSet<Transform> CollectHumanoidBones(GameObject avatarRoot)
        {
            var result = new HashSet<Transform>();
            if (avatarRoot == null)
            {
                return result;
            }

            Animator animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                return result;
            }

            for (int boneIndex = (int)HumanBodyBones.Hips;
                 boneIndex < (int)HumanBodyBones.LastBone;
                 boneIndex++)
            {
                Transform bone = animator.GetBoneTransform((HumanBodyBones)boneIndex);
                if (bone != null)
                {
                    result.Add(bone);
                }
            }

            return result;
        }

        private static bool HasAdditionalComponents(
            GameObject gameObject, Component mergeBone)
        {
            Component[] components = gameObject.GetComponents<Component>();
            for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                Component component = components[componentIndex];
                if (component is Transform || mergeBone != null && component == mergeBone)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static void RefreshUnevenScale(AaoMergeBoneNode node)
        {
            node.Warnings &= ~AaoMergeBoneWarning.UnevenScale;
            Vector3 scale = node.Transform.localScale;
            bool uneven = !Mathf.Approximately(scale.x, scale.y) ||
                !Mathf.Approximately(scale.y, scale.z);
            if (uneven && HasUncheckedNonEditorOnlyDescendant(node))
            {
                node.Warnings |= AaoMergeBoneWarning.UnevenScale;
            }
        }

        private static bool HasUncheckedNonEditorOnlyDescendant(AaoMergeBoneNode node)
        {
            for (int childIndex = 0; childIndex < node.Children.Count; childIndex++)
            {
                AaoMergeBoneNode child = node.Children[childIndex];
                if (child.BlockReason != AaoMergeBoneBlockReason.EditorOnly && !child.Checked)
                {
                    return true;
                }

                if (HasUncheckedNonEditorOnlyDescendant(child))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RefreshChangeCounts(AaoMergeBoneHelperPlan plan)
        {
            int addCount = 0;
            int removeCount = 0;
            for (int nodeIndex = 0; nodeIndex < plan.AllNodes.Count; nodeIndex++)
            {
                AaoMergeBoneNode node = plan.AllNodes[nodeIndex];
                if (node.BlockReason != AaoMergeBoneBlockReason.None)
                {
                    continue;
                }

                if (node.Checked && !node.HasComponentInitially)
                {
                    addCount++;
                }
                else if (!node.Checked && node.HasComponentInitially)
                {
                    removeCount++;
                }
            }

            plan.AddCount = addCount;
            plan.RemoveCount = removeCount;
            plan.SummaryText = string.Format("追加 {0} 件 / 削除 {1} 件", addCount, removeCount);
            plan.ApplyText = "適用（" + plan.SummaryText + "）";
        }

        private static void BuildStatusText(AaoMergeBoneNode node)
        {
            if (node.BlockReason != AaoMergeBoneBlockReason.None)
            {
                node.StatusText = BlockReasonTexts[(int)node.BlockReason];
                return;
            }

            var builder = new StringBuilder();
            if (node.HasComponentInitially)
            {
                builder.Append("設定済み");
            }

            AppendWarning(builder, node.Warnings, AaoMergeBoneWarning.HasComponents, 0);
            AppendWarning(builder, node.Warnings, AaoMergeBoneWarning.UnevenScale, 1);
            AppendWarning(builder, node.Warnings, AaoMergeBoneWarning.Animated, 2);
            node.StatusText = builder.ToString();
        }

        private static void AppendWarning(
            StringBuilder builder,
            AaoMergeBoneWarning warnings,
            AaoMergeBoneWarning warning,
            int textIndex)
        {
            if ((warnings & warning) == 0)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(" / ");
            }

            builder.Append(WarningTexts[textIndex]);
        }
    }
}
