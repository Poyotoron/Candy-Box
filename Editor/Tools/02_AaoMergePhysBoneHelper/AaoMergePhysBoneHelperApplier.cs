using System;
using Anatawa12.AvatarOptimizer;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace Poyo.CandyBox.AaoMergePhysBoneHelper.Editor
{
    internal static class AaoMergePhysBoneHelperApplier
    {
        private const string UndoName = "AAO Merge PhysBone Helper";

        internal static int Apply(AaoMergePhysBoneHelperPlan plan)
        {
            if (plan == null || plan.MergePhysBone == null)
            {
                return 0;
            }

            MergePhysBone mergePhysBone = plan.MergePhysBone;
            Undo.RecordObject(mergePhysBone, UndoName);
            int appliedCount = 0;
            for (int propertyIndex = 0; propertyIndex < plan.Differing.Count; propertyIndex++)
            {
                AaoMergePhysBonePropertyPlan propertyPlan = plan.Differing[propertyIndex];
                if (!propertyPlan.Selected || propertyPlan.Blocked || propertyPlan.Suggestion == null)
                {
                    continue;
                }

                try
                {
                    ApplyProperty(mergePhysBone, propertyPlan);
                    appliedCount++;
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        "Candy Box: " + propertyPlan.Property.DisplayName +
                        " の override に失敗しました。\n" + exception,
                        mergePhysBone);
                }
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(mergePhysBone);
            EditorUtility.SetDirty(mergePhysBone);
            return appliedCount;
        }

        private static void ApplyProperty(
            MergePhysBone mergePhysBone, AaoMergePhysBonePropertyPlan plan)
        {
            AaoMergePhysBoneSuggestion suggestion = plan.Suggestion;
            switch (plan.Property.Key)
            {
                case "Version":
                {
                    var config = mergePhysBone.VersionConfig;
                    config.OverrideStatus = MergePhysBone.OverrideStatus.Overridden;
                    config.VersionOverrideValue =
                        ToEnumValue<VRCPhysBoneBase.Version>(suggestion.Int);
                    break;
                }
                case "IgnoreOtherPhysBones":
                    ApplyBool(mergePhysBone.IgnoreOtherPhysBonesConfig, suggestion);
                    break;
                case "IntegrationType":
                {
                    var config = mergePhysBone.IntegrationTypeConfig;
                    config.OverrideStatus = MergePhysBone.OverrideStatus.Overridden;
                    config.IntegrationTypeOverrideValue =
                        ToEnumValue<VRCPhysBoneBase.IntegrationType>(suggestion.Int);
                    break;
                }
                case "Pull": ApplyFloat(mergePhysBone.PullConfig, suggestion, true); break;
                case "Spring": ApplyFloat(mergePhysBone.SpringConfig, suggestion, true); break;
                case "Stiffness": ApplyFloat(mergePhysBone.StiffnessConfig, suggestion, true); break;
                case "Gravity": ApplyFloat(mergePhysBone.GravityConfig, suggestion, true); break;
                case "GravityFalloff": ApplyFloat(mergePhysBone.GravityFalloffConfig, suggestion, true); break;
                case "ImmobileType":
                {
                    var config = mergePhysBone.ImmobileTypeConfig;
                    config.OverrideStatus = MergePhysBone.OverrideStatus.Overridden;
                    config.ImmobileTypeOverrideValue =
                        ToEnumValue<VRCPhysBoneBase.ImmobileType>(suggestion.Int);
                    break;
                }
                case "Immobile": ApplyFloat(mergePhysBone.ImmobileConfig, suggestion, true); break;
                case "LimitType":
                {
                    var config = mergePhysBone.LimitTypeConfig;
                    config.OverrideStatus = MergePhysBone.OverrideStatus.Overridden;
                    config.LimitTypeOverrideValue =
                        ToEnumValue<VRCPhysBoneBase.LimitType>(suggestion.Int);
                    break;
                }
                case "MaxAngleX": ApplyFloat(mergePhysBone.MaxAngleXConfig, suggestion, true); break;
                case "MaxAngleZ": ApplyFloat(mergePhysBone.MaxAngleZConfig, suggestion, true); break;
                case "LimitRotation": ApplyVector(mergePhysBone.LimitRotationConfig, suggestion); break;
                case "Radius": ApplyFloat(mergePhysBone.RadiusConfig, suggestion, true); break;
                case "AllowCollision": ApplyPermission(mergePhysBone.AllowCollisionConfig, suggestion); break;
                case "StretchMotion":
                    ApplyFloat(mergePhysBone.StretchMotionConfig, suggestion, false);
                    ApplySerializedCurve(mergePhysBone, "stretchMotionConfig", suggestion.Curve);
                    break;
                case "MaxStretch": ApplyFloat(mergePhysBone.MaxStretchConfig, suggestion, true); break;
                case "MaxSquish":
                    ApplyFloat(mergePhysBone.MaxSquishConfig, suggestion, false);
                    ApplySerializedCurve(mergePhysBone, "maxSquishConfig", suggestion.Curve);
                    break;
                case "AllowGrabbing": ApplyPermission(mergePhysBone.AllowGrabbingConfig, suggestion); break;
                case "AllowPosing": ApplyPermission(mergePhysBone.AllowPosingConfig, suggestion); break;
                case "GrabMovement": ApplyFloat(mergePhysBone.GrabMovementConfig, suggestion, false); break;
                case "SnapToHand": ApplyBool(mergePhysBone.SnapToHandConfig, suggestion); break;
                case "ResetWhenDisabled": ApplyBool(mergePhysBone.ResetWhenDisabledConfig, suggestion); break;
                default:
                    throw new InvalidOperationException(
                        "未対応のプロパティです: " + plan.Property.Key);
            }
        }

        private static void ApplyFloat(
            MergePhysBone.OverrideAndValueAPI config,
            AaoMergePhysBoneSuggestion suggestion,
            bool hasPublicCurve)
        {
            config.OverrideStatus = MergePhysBone.OverrideStatus.Overridden;
            config.FloatOverrideValue = suggestion.Float;
            if (hasPublicCurve)
            {
                config.FloatCurveOverrideValue = EnsureCurve(suggestion.Curve);
            }
        }

        private static void ApplyBool(
            MergePhysBone.OverrideAndValueAPI config,
            AaoMergePhysBoneSuggestion suggestion)
        {
            config.OverrideStatus = MergePhysBone.OverrideStatus.Overridden;
            config.BoolOverrideValue = suggestion.Int != 0;
        }

        private static void ApplyVector(
            MergePhysBone.OverrideAndValueAPI config,
            AaoMergePhysBoneSuggestion suggestion)
        {
            config.OverrideStatus = MergePhysBone.OverrideStatus.Overridden;
            config.PositionOverrideValue = suggestion.Vector;
            config.LimitRotationCurveXOverrideValue = EnsureCurve(suggestion.Curve);
            config.LimitRotationCurveYOverrideValue = EnsureCurve(suggestion.CurveY);
            config.LimitRotationCurveZOverrideValue = EnsureCurve(suggestion.CurveZ);
        }

        private static void ApplyPermission(
            MergePhysBone.OverrideAndValueAPI config,
            AaoMergePhysBoneSuggestion suggestion)
        {
            config.OverrideStatus = MergePhysBone.OverrideStatus.Overridden;
            VRCPhysBoneBase.AdvancedBool value =
                ToEnumValue<VRCPhysBoneBase.AdvancedBool>(suggestion.Int);
            config.PermissionOverrideValue = value;
            if (value == VRCPhysBoneBase.AdvancedBool.Other)
            {
                config.PermissionFilterOverrideValue = new VRCPhysBoneBase.PermissionFilter
                {
                    allowSelf = (suggestion.FilterInt & 1) != 0,
                    allowOthers = (suggestion.FilterInt & 2) != 0,
                };
            }
        }

        private static void ApplySerializedCurve(
            MergePhysBone mergePhysBone, string configName, AnimationCurve curve)
        {
            var serializedObject = new SerializedObject(mergePhysBone);
            serializedObject.Update();
            SerializedProperty config = serializedObject.FindProperty(configName);
            SerializedProperty curveProperty = config != null
                ? config.FindPropertyRelative("curve")
                : null;
            if (curveProperty == null)
            {
                throw new InvalidOperationException(
                    "カーブの書き込み先が見つかりません: " + configName);
            }

            curveProperty.animationCurveValue = EnsureCurve(curve);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static AnimationCurve EnsureCurve(AnimationCurve curve)
        {
            return curve != null ? curve : new AnimationCurve();
        }

        private static T ToEnumValue<T>(int enumValueIndex) where T : Enum
        {
            Array values = Enum.GetValues(typeof(T));
            if (values.Length == 0)
            {
                return default;
            }

            if (enumValueIndex < 0 || enumValueIndex >= values.Length)
            {
                return (T)values.GetValue(0);
            }

            return (T)values.GetValue(enumValueIndex);
        }
    }
}
