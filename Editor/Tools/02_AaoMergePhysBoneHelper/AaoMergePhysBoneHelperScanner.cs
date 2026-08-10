using System;
using System.Collections.Generic;
using Anatawa12.AvatarOptimizer;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace Poyo.CandyBox.AaoMergePhysBoneHelper.Editor
{
    internal static class AaoMergePhysBoneHelperScanner
    {
        private const string CollidersBlockedReason =
            "コライダーの一覧は提案できません。AAO Merge PhysBone 側で Merge を選んでください。";
        private const string EndpointBlockedReason =
            "Endpoint Position は提案できません。AAO Merge PhysBone 側で Clear を選んでください。";
        private const string SelectTooltip = "クリックすると選択します";

        internal static AaoMergePhysBoneHelperPlan Scan(MergePhysBone mergePhysBone)
        {
            var plan = new AaoMergePhysBoneHelperPlan { MergePhysBone = mergePhysBone };
            Animator animator = mergePhysBone.GetComponentInParent<Animator>();
            Transform avatarRoot = animator != null ? animator.transform : null;
            foreach (VRCPhysBoneBase physBone in mergePhysBone.PhysBones)
            {
                if (physBone == null)
                {
                    continue;
                }

                string label = BuildPath(physBone.transform, avatarRoot);
                int chainLength = GetMaximumDepth(
                    physBone.rootTransform != null ? physBone.rootTransform : physBone.transform);
                plan.Sources.Add(new AaoMergePhysBoneSource
                {
                    PhysBone = physBone,
                    GameObject = physBone.gameObject,
                    Label = label,
                    LabelContent = new GUIContent(label, SelectTooltip),
                    ChainLength = chainLength,
                    ChainLengthText = string.Format(
                        "チェーン {0} 段", chainLength),
                });
            }

            plan.ChainLengthDiffers = HasDifferentChainLengths(plan.Sources);
            var serializedObjects = new SerializedObject[plan.Sources.Count];
            for (int sourceIndex = 0; sourceIndex < plan.Sources.Count; sourceIndex++)
            {
                serializedObjects[sourceIndex] =
                    new SerializedObject(plan.Sources[sourceIndex].PhysBone);
                serializedObjects[sourceIndex].UpdateIfRequiredOrScript();
            }

            for (int propertyIndex = 0;
                 propertyIndex < AaoMergePhysBoneProperty.All.Length;
                 propertyIndex++)
            {
                AaoMergePhysBoneProperty property = AaoMergePhysBoneProperty.All[propertyIndex];
                if (!AllSourcesHaveProperty(serializedObjects, property.ValueFieldName))
                {
                    plan.MissingPropertyCount++;
                    continue;
                }

                var propertyPlan = new AaoMergePhysBonePropertyPlan
                {
                    Property = property,
                    CurrentOverrideText = GetOverrideText(mergePhysBone, property.Key),
                    ChainLengthDiffers = plan.ChainLengthDiffers,
                };
                ReadValues(propertyPlan, plan.Sources, serializedObjects);
                propertyPlan.HasDifference = HasDifference(propertyPlan);
                if (!propertyPlan.HasDifference)
                {
                    propertyPlan.Selected = false;
                    plan.Identical.Add(propertyPlan);
                    continue;
                }

                propertyPlan.OutlierSourceIndex = FindOutlierIndex(propertyPlan);
                if (propertyPlan.OutlierSourceIndex >= 0)
                {
                    propertyPlan.OutlierText = string.Format(
                        "{0} だけ値が異なります。",
                        plan.Sources[propertyPlan.OutlierSourceIndex].Label);
                }

                if (property.Kind == AaoMergePhysBoneValueKind.Unsupported)
                {
                    propertyPlan.Blocked = true;
                    propertyPlan.Selected = false;
                    propertyPlan.BlockedReason = property.Key == "Colliders"
                        ? CollidersBlockedReason
                        : EndpointBlockedReason;
                    propertyPlan.BlockedDisplayText =
                        property.DisplayName + ": " + propertyPlan.BlockedReason;
                    plan.Blocked.Add(propertyPlan);
                    continue;
                }

                AaoMergePhysBoneStatistics.BuildStatisticsText(propertyPlan);
                AaoMergePhysBoneStatistics.Recompute(
                    propertyPlan, AaoMergePhysBoneMetric.Mode);
                if (propertyPlan.Blocked)
                {
                    plan.Blocked.Add(propertyPlan);
                }
                else
                {
                    plan.Differing.Add(propertyPlan);
                }
            }

            BuildPlanDisplayText(plan);
            return plan;
        }

        internal static void RefreshPlanDisplayText(AaoMergePhysBoneHelperPlan plan)
        {
            BuildPlanDisplayText(plan);
        }

        internal static void CarryOverViewState(
            AaoMergePhysBoneHelperPlan previous,
            AaoMergePhysBoneHelperPlan next)
        {
            if (previous == null || next == null)
            {
                return;
            }

            next.DifferingExpanded = previous.DifferingExpanded;
            next.BlockedExpanded = previous.BlockedExpanded;
            next.IdenticalExpanded = previous.IdenticalExpanded;
            CarryOverPropertyExpansion(previous, next.Differing);
            CarryOverPropertyExpansion(previous, next.Blocked);
            CarryOverPropertyExpansion(previous, next.Identical);
        }

        private static void CarryOverPropertyExpansion(
            AaoMergePhysBoneHelperPlan previous,
            List<AaoMergePhysBonePropertyPlan> nextProperties)
        {
            for (int propertyIndex = 0; propertyIndex < nextProperties.Count; propertyIndex++)
            {
                AaoMergePhysBonePropertyPlan nextProperty = nextProperties[propertyIndex];
                AaoMergePhysBonePropertyPlan previousProperty =
                    FindProperty(previous.Differing, nextProperty.Property.Key) ??
                    FindProperty(previous.Blocked, nextProperty.Property.Key) ??
                    FindProperty(previous.Identical, nextProperty.Property.Key);
                if (previousProperty != null)
                {
                    nextProperty.Expanded = previousProperty.Expanded;
                }
            }
        }

        private static AaoMergePhysBonePropertyPlan FindProperty(
            List<AaoMergePhysBonePropertyPlan> properties, string key)
        {
            for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
            {
                if (string.Equals(
                        properties[propertyIndex].Property.Key,
                        key,
                        StringComparison.Ordinal))
                {
                    return properties[propertyIndex];
                }
            }

            return null;
        }

        private static void ReadValues(
            AaoMergePhysBonePropertyPlan propertyPlan,
            List<AaoMergePhysBoneSource> sources,
            SerializedObject[] serializedObjects)
        {
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                SerializedObject serializedObject = serializedObjects[sourceIndex];
                SerializedProperty valueProperty =
                    serializedObject.FindProperty(propertyPlan.Property.ValueFieldName);
                var value = new AaoMergePhysBoneValue { SourceIndex = sourceIndex };
                switch (propertyPlan.Property.Kind)
                {
                    case AaoMergePhysBoneValueKind.Float:
                        value.Float = valueProperty.floatValue;
                        value.Curve = ReadCurve(
                            serializedObject, propertyPlan.Property.CurveFieldName);
                        value.DisplayText = FormatFloatValue(value.Float, value.Curve);
                        break;
                    case AaoMergePhysBoneValueKind.Bool:
                        value.Int = valueProperty.boolValue ? 1 : 0;
                        value.DisplayText = value.Int != 0 ? "有効" : "無効";
                        break;
                    case AaoMergePhysBoneValueKind.Enum:
                        value.Int = valueProperty.enumValueIndex;
                        StoreEnumNames(propertyPlan, valueProperty);
                        value.DisplayText = GetEnumDisplayName(propertyPlan, value.Int);
                        break;
                    case AaoMergePhysBoneValueKind.Vector3:
                        value.Vector = valueProperty.vector3Value;
                        value.Curve = ReadCurve(
                            serializedObject, propertyPlan.Property.CurveFieldName);
                        value.CurveY = ReadCurve(
                            serializedObject, propertyPlan.Property.CurveFieldNameY);
                        value.CurveZ = ReadCurve(
                            serializedObject, propertyPlan.Property.CurveFieldNameZ);
                        value.DisplayText = FormatVectorValue(value.Vector, value);
                        break;
                    case AaoMergePhysBoneValueKind.Permission:
                        value.Int = valueProperty.enumValueIndex;
                        StoreEnumNames(propertyPlan, valueProperty);
                        SerializedProperty filter = serializedObject.FindProperty(
                            propertyPlan.Property.FilterFieldName);
                        value.FilterInt = ReadFilter(filter);
                        value.DisplayText = GetEnumDisplayName(propertyPlan, value.Int) +
                            " / " + FormatFilter(value.FilterInt);
                        break;
                    case AaoMergePhysBoneValueKind.Unsupported:
                        ReadUnsupported(value, propertyPlan.Property, valueProperty);
                        break;
                }

                propertyPlan.Values.Add(value);
            }
        }

        private static void ReadUnsupported(
            AaoMergePhysBoneValue value,
            AaoMergePhysBoneProperty property,
            SerializedProperty valueProperty)
        {
            if (property.Key == "EndpointPosition")
            {
                value.Vector = valueProperty.vector3Value;
                value.DisplayText = FormatVector(value.Vector);
                return;
            }

            int count = valueProperty.isArray ? valueProperty.arraySize : 0;
            value.ObjectReferences = new UnityEngine.Object[count];
            for (int itemIndex = 0; itemIndex < count; itemIndex++)
            {
                value.ObjectReferences[itemIndex] =
                    valueProperty.GetArrayElementAtIndex(itemIndex).objectReferenceValue;
            }

            value.DisplayText = count.ToString() + " 件";
        }

        private static bool HasDifference(AaoMergePhysBonePropertyPlan plan)
        {
            if (plan.Values.Count < 2)
            {
                return false;
            }

            for (int valueIndex = 1; valueIndex < plan.Values.Count; valueIndex++)
            {
                if (!ValuesEqual(plan.Property, plan.Values[0], plan.Values[valueIndex]))
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindOutlierIndex(AaoMergePhysBonePropertyPlan plan)
        {
            if (plan.Values.Count < 3)
            {
                return -1;
            }

            int result = -1;
            for (int candidateIndex = 0; candidateIndex < plan.Values.Count; candidateIndex++)
            {
                int commonIndex = candidateIndex == 0 ? 1 : 0;
                if (ValuesEqual(
                        plan.Property,
                        plan.Values[candidateIndex],
                        plan.Values[commonIndex]))
                {
                    continue;
                }

                bool othersMatch = true;
                for (int valueIndex = 0; valueIndex < plan.Values.Count; valueIndex++)
                {
                    if (valueIndex != candidateIndex && valueIndex != commonIndex &&
                        !ValuesEqual(
                            plan.Property,
                            plan.Values[commonIndex],
                            plan.Values[valueIndex]))
                    {
                        othersMatch = false;
                        break;
                    }
                }

                if (!othersMatch || result >= 0)
                {
                    continue;
                }

                result = candidateIndex;
            }

            return result;
        }

        private static bool ValuesEqual(
            AaoMergePhysBoneProperty property,
            AaoMergePhysBoneValue left,
            AaoMergePhysBoneValue right)
        {
            switch (property.Kind)
            {
                case AaoMergePhysBoneValueKind.Float:
                    return left.Float == right.Float && CurvesEqual(left.Curve, right.Curve);
                case AaoMergePhysBoneValueKind.Vector3:
                    return left.Vector == right.Vector &&
                        CurvesEqual(left.Curve, right.Curve) &&
                        CurvesEqual(left.CurveY, right.CurveY) &&
                        CurvesEqual(left.CurveZ, right.CurveZ);
                case AaoMergePhysBoneValueKind.Bool:
                case AaoMergePhysBoneValueKind.Enum:
                    return left.Int == right.Int;
                case AaoMergePhysBoneValueKind.Permission:
                    return left.Int == right.Int && left.FilterInt == right.FilterInt;
                case AaoMergePhysBoneValueKind.Unsupported:
                    return property.Key == "EndpointPosition"
                        ? left.Vector == right.Vector
                        : ObjectReferencesEqual(left.ObjectReferences, right.ObjectReferences);
                default:
                    return false;
            }
        }

        private static bool CurvesEqual(AnimationCurve left, AnimationCurve right)
        {
            int leftLength = left != null ? left.length : 0;
            int rightLength = right != null ? right.length : 0;
            if (leftLength == 0 && rightLength == 0)
            {
                return true;
            }

            if (leftLength != rightLength)
            {
                return false;
            }

            for (int keyIndex = 0; keyIndex < leftLength; keyIndex++)
            {
                Keyframe leftKey = left.keys[keyIndex];
                Keyframe rightKey = right.keys[keyIndex];
                if (leftKey.time != rightKey.time || leftKey.value != rightKey.value)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ObjectReferencesEqual(
            UnityEngine.Object[] left, UnityEngine.Object[] right)
        {
            int leftLength = left != null ? left.Length : 0;
            int rightLength = right != null ? right.Length : 0;
            if (leftLength != rightLength)
            {
                return false;
            }

            for (int itemIndex = 0; itemIndex < leftLength; itemIndex++)
            {
                if (left[itemIndex] != right[itemIndex])
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetOverrideText(MergePhysBone mergePhysBone, string key)
        {
            MergePhysBone.OverrideStatus status;
            switch (key)
            {
                case "Version": { var c = mergePhysBone.VersionConfig; status = c.OverrideStatus; break; }
                case "EndpointPosition": { var c = mergePhysBone.EndPointPositionConfig; status = c.OverrideStatus; break; }
                case "IgnoreOtherPhysBones": { var c = mergePhysBone.IgnoreOtherPhysBonesConfig; status = c.OverrideStatus; break; }
                case "IntegrationType": { var c = mergePhysBone.IntegrationTypeConfig; status = c.OverrideStatus; break; }
                case "Pull": { var c = mergePhysBone.PullConfig; status = c.OverrideStatus; break; }
                case "Spring": { var c = mergePhysBone.SpringConfig; status = c.OverrideStatus; break; }
                case "Stiffness": { var c = mergePhysBone.StiffnessConfig; status = c.OverrideStatus; break; }
                case "Gravity": { var c = mergePhysBone.GravityConfig; status = c.OverrideStatus; break; }
                case "GravityFalloff": { var c = mergePhysBone.GravityFalloffConfig; status = c.OverrideStatus; break; }
                case "ImmobileType": { var c = mergePhysBone.ImmobileTypeConfig; status = c.OverrideStatus; break; }
                case "Immobile": { var c = mergePhysBone.ImmobileConfig; status = c.OverrideStatus; break; }
                case "LimitType": { var c = mergePhysBone.LimitTypeConfig; status = c.OverrideStatus; break; }
                case "MaxAngleX": { var c = mergePhysBone.MaxAngleXConfig; status = c.OverrideStatus; break; }
                case "MaxAngleZ": { var c = mergePhysBone.MaxAngleZConfig; status = c.OverrideStatus; break; }
                case "LimitRotation": { var c = mergePhysBone.LimitRotationConfig; status = c.OverrideStatus; break; }
                case "Radius": { var c = mergePhysBone.RadiusConfig; status = c.OverrideStatus; break; }
                case "AllowCollision": { var c = mergePhysBone.AllowCollisionConfig; status = c.OverrideStatus; break; }
                case "Colliders": { var c = mergePhysBone.CollidersConfig; status = c.OverrideStatus; break; }
                case "StretchMotion": { var c = mergePhysBone.StretchMotionConfig; status = c.OverrideStatus; break; }
                case "MaxStretch": { var c = mergePhysBone.MaxStretchConfig; status = c.OverrideStatus; break; }
                case "MaxSquish": { var c = mergePhysBone.MaxSquishConfig; status = c.OverrideStatus; break; }
                case "AllowGrabbing": { var c = mergePhysBone.AllowGrabbingConfig; status = c.OverrideStatus; break; }
                case "AllowPosing": { var c = mergePhysBone.AllowPosingConfig; status = c.OverrideStatus; break; }
                case "GrabMovement": { var c = mergePhysBone.GrabMovementConfig; status = c.OverrideStatus; break; }
                case "SnapToHand": { var c = mergePhysBone.SnapToHandConfig; status = c.OverrideStatus; break; }
                case "ResetWhenDisabled": { var c = mergePhysBone.ResetWhenDisabledConfig; status = c.OverrideStatus; break; }
                default: return string.Empty;
            }

            switch (status)
            {
                case MergePhysBone.OverrideStatus.Copied: return "Copy";
                case MergePhysBone.OverrideStatus.Overridden: return "Override";
                case MergePhysBone.OverrideStatus.Cleared: return "Clear";
                case MergePhysBone.OverrideStatus.Merged: return "Merge";
                case MergePhysBone.OverrideStatus.Fixed: return "Fix";
                default: return string.Empty;
            }
        }

        private static bool AllSourcesHaveProperty(
            SerializedObject[] serializedObjects, string fieldName)
        {
            for (int sourceIndex = 0; sourceIndex < serializedObjects.Length; sourceIndex++)
            {
                if (serializedObjects[sourceIndex].FindProperty(fieldName) == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static AnimationCurve ReadCurve(
            SerializedObject serializedObject, string fieldName)
        {
            if (fieldName == null)
            {
                return null;
            }

            SerializedProperty property = serializedObject.FindProperty(fieldName);
            return property != null ? property.animationCurveValue : null;
        }

        private static int ReadFilter(SerializedProperty filter)
        {
            if (filter == null)
            {
                return 0;
            }

            SerializedProperty allowSelf = filter.FindPropertyRelative("allowSelf");
            SerializedProperty allowOthers = filter.FindPropertyRelative("allowOthers");
            return (allowSelf != null && allowSelf.boolValue ? 1 : 0) |
                (allowOthers != null && allowOthers.boolValue ? 2 : 0);
        }

        private static void StoreEnumNames(
            AaoMergePhysBonePropertyPlan plan, SerializedProperty property)
        {
            if (plan.EnumDisplayNames == null)
            {
                plan.EnumNames = property.enumNames;
                plan.EnumDisplayNames = property.enumDisplayNames;
            }
        }

        private static string GetEnumDisplayName(AaoMergePhysBonePropertyPlan plan, int index)
        {
            return plan.EnumDisplayNames != null && index >= 0 && index < plan.EnumDisplayNames.Length
                ? plan.EnumDisplayNames[index]
                : index.ToString();
        }

        private static string FormatFloatValue(float value, AnimationCurve curve)
        {
            string text = value.ToString("0.###");
            return curve != null && curve.length > 0 ? text + "（カーブあり）" : text;
        }

        private static string FormatVectorValue(
            Vector3 vector, AaoMergePhysBoneValue value)
        {
            string text = FormatVector(vector);
            return value.Curve != null && value.Curve.length > 0 ||
                value.CurveY != null && value.CurveY.length > 0 ||
                value.CurveZ != null && value.CurveZ.length > 0
                    ? text + "（カーブあり）"
                    : text;
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Format(
                "({0}, {1}, {2})",
                value.x.ToString("0.###"),
                value.y.ToString("0.###"),
                value.z.ToString("0.###"));
        }

        private static string FormatFilter(int filter)
        {
            switch (filter)
            {
                case 1: return "Self";
                case 2: return "Others";
                case 3: return "Self, Others";
                default: return "なし";
            }
        }

        private static bool HasDifferentChainLengths(List<AaoMergePhysBoneSource> sources)
        {
            if (sources.Count < 2)
            {
                return false;
            }

            int first = sources[0].ChainLength;
            for (int sourceIndex = 1; sourceIndex < sources.Count; sourceIndex++)
            {
                if (sources[sourceIndex].ChainLength != first)
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetMaximumDepth(Transform root)
        {
            int maximum = 1;
            for (int childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                maximum = Mathf.Max(maximum, 1 + GetMaximumDepth(root.GetChild(childIndex)));
            }

            return maximum;
        }

        private static string BuildPath(Transform target, Transform avatarRoot)
        {
            Transform root = avatarRoot;
            if (root == null)
            {
                root = target;
                while (root.parent != null)
                {
                    root = root.parent;
                }
            }

            string path = AnimationUtility.CalculateTransformPath(target, root);
            return string.IsNullOrEmpty(path) ? target.name : path;
        }

        private static void BuildPlanDisplayText(AaoMergePhysBoneHelperPlan plan)
        {
            plan.SourcesHeaderText = string.Format("統合対象（{0} 件）", plan.Sources.Count);
            plan.DifferingHeaderText = string.Format("差異あり（{0} 件）", plan.Differing.Count);
            plan.BlockedHeaderText = string.Format("統合不可（{0} 件）", plan.Blocked.Count);
            plan.IdenticalHeaderText = string.Format("差異なし（{0} 件）", plan.Identical.Count);
            if (plan.MissingPropertyCount > 0)
            {
                plan.MissingPropertyText = string.Format(
                    "この環境では扱えないプロパティが {0} 件あります。",
                    plan.MissingPropertyCount);
            }

            int selectedCount = 0;
            for (int propertyIndex = 0; propertyIndex < plan.Differing.Count; propertyIndex++)
            {
                if (plan.Differing[propertyIndex].Selected && !plan.Differing[propertyIndex].Blocked)
                {
                    selectedCount++;
                }
            }

            plan.ApplyText = string.Format("{0} 件を override", selectedCount);
        }
    }
}
