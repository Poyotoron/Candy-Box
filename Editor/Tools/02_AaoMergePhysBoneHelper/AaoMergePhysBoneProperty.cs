namespace Poyo.CandyBox.AaoMergePhysBoneHelper.Editor
{
    internal enum AaoMergePhysBoneValueKind
    {
        Float,
        Bool,
        Enum,
        Vector3,
        Permission,
        Unsupported,
    }

    internal enum AaoMergePhysBoneEnumKind
    {
        None,
        Version,
        IntegrationType,
        ImmobileType,
        LimitType,
    }

    internal sealed class AaoMergePhysBoneProperty
    {
        internal readonly string Key;
        internal readonly string DisplayName;
        internal readonly string ValueFieldName;
        internal readonly string CurveFieldName;
        internal readonly string CurveFieldNameY;
        internal readonly string CurveFieldNameZ;
        internal readonly string FilterFieldName;
        internal readonly AaoMergePhysBoneValueKind Kind;
        internal readonly AaoMergePhysBoneEnumKind EnumKind;
        internal readonly bool HasRange;
        internal readonly float RangeMin;
        internal readonly float RangeMax;

        private AaoMergePhysBoneProperty(
            string key,
            string displayName,
            string valueFieldName,
            AaoMergePhysBoneValueKind kind,
            string curveFieldName = null,
            string curveFieldNameY = null,
            string curveFieldNameZ = null,
            string filterFieldName = null,
            AaoMergePhysBoneEnumKind enumKind = AaoMergePhysBoneEnumKind.None,
            bool hasRange = false,
            float rangeMin = 0f,
            float rangeMax = 0f)
        {
            Key = key;
            DisplayName = displayName;
            ValueFieldName = valueFieldName;
            CurveFieldName = curveFieldName;
            CurveFieldNameY = curveFieldNameY;
            CurveFieldNameZ = curveFieldNameZ;
            FilterFieldName = filterFieldName;
            Kind = kind;
            EnumKind = enumKind;
            HasRange = hasRange;
            RangeMin = rangeMin;
            RangeMax = rangeMax;
        }

        internal static readonly AaoMergePhysBoneProperty[] All =
        {
            new AaoMergePhysBoneProperty(
                "Version", "Version", "version", AaoMergePhysBoneValueKind.Enum,
                enumKind: AaoMergePhysBoneEnumKind.Version),
            new AaoMergePhysBoneProperty(
                "EndpointPosition", "Endpoint Position", "endpointPosition",
                AaoMergePhysBoneValueKind.Unsupported),
            new AaoMergePhysBoneProperty(
                "IgnoreOtherPhysBones", "Ignore Other Phys Bones", "ignoreOtherPhysBones",
                AaoMergePhysBoneValueKind.Bool),
            new AaoMergePhysBoneProperty(
                "IntegrationType", "Integration Type", "integrationType",
                AaoMergePhysBoneValueKind.Enum,
                enumKind: AaoMergePhysBoneEnumKind.IntegrationType),
            new AaoMergePhysBoneProperty(
                "Pull", "Pull", "pull", AaoMergePhysBoneValueKind.Float,
                curveFieldName: "pullCurve", hasRange: true, rangeMin: 0f, rangeMax: 1f),
            new AaoMergePhysBoneProperty(
                "Spring", "Spring", "spring", AaoMergePhysBoneValueKind.Float,
                curveFieldName: "springCurve", hasRange: true, rangeMin: 0f, rangeMax: 1f),
            new AaoMergePhysBoneProperty(
                "Stiffness", "Stiffness", "stiffness", AaoMergePhysBoneValueKind.Float,
                curveFieldName: "stiffnessCurve", hasRange: true, rangeMin: 0f, rangeMax: 1f),
            new AaoMergePhysBoneProperty(
                "Gravity", "Gravity", "gravity", AaoMergePhysBoneValueKind.Float,
                curveFieldName: "gravityCurve", hasRange: true, rangeMin: -1f, rangeMax: 1f),
            new AaoMergePhysBoneProperty(
                "GravityFalloff", "Gravity Falloff", "gravityFalloff",
                AaoMergePhysBoneValueKind.Float, curveFieldName: "gravityFalloffCurve",
                hasRange: true, rangeMin: 0f, rangeMax: 1f),
            new AaoMergePhysBoneProperty(
                "ImmobileType", "Immobile Type", "immobileType",
                AaoMergePhysBoneValueKind.Enum,
                enumKind: AaoMergePhysBoneEnumKind.ImmobileType),
            new AaoMergePhysBoneProperty(
                "Immobile", "Immobile", "immobile", AaoMergePhysBoneValueKind.Float,
                curveFieldName: "immobileCurve", hasRange: true, rangeMin: 0f, rangeMax: 1f),
            new AaoMergePhysBoneProperty(
                "LimitType", "Limit Type", "limitType", AaoMergePhysBoneValueKind.Enum,
                enumKind: AaoMergePhysBoneEnumKind.LimitType),
            new AaoMergePhysBoneProperty(
                "MaxAngleX", "Max Angle X", "maxAngleX", AaoMergePhysBoneValueKind.Float,
                curveFieldName: "maxAngleXCurve", hasRange: true, rangeMin: 0f, rangeMax: 180f),
            new AaoMergePhysBoneProperty(
                "MaxAngleZ", "Max Angle Z", "maxAngleZ", AaoMergePhysBoneValueKind.Float,
                curveFieldName: "maxAngleZCurve", hasRange: true, rangeMin: 0f, rangeMax: 90f),
            new AaoMergePhysBoneProperty(
                "LimitRotation", "Limit Rotation", "limitRotation",
                AaoMergePhysBoneValueKind.Vector3,
                curveFieldName: "limitRotationXCurve",
                curveFieldNameY: "limitRotationYCurve",
                curveFieldNameZ: "limitRotationZCurve"),
            new AaoMergePhysBoneProperty(
                "Radius", "Radius", "radius", AaoMergePhysBoneValueKind.Float,
                curveFieldName: "radiusCurve"),
            new AaoMergePhysBoneProperty(
                "AllowCollision", "Allow Collision", "allowCollision",
                AaoMergePhysBoneValueKind.Permission, filterFieldName: "collisionFilter"),
            new AaoMergePhysBoneProperty(
                "Colliders", "Colliders", "colliders", AaoMergePhysBoneValueKind.Unsupported),
            new AaoMergePhysBoneProperty(
                "StretchMotion", "Stretch Motion", "stretchMotion",
                AaoMergePhysBoneValueKind.Float, curveFieldName: "stretchMotionCurve",
                hasRange: true, rangeMin: 0f, rangeMax: 1f),
            new AaoMergePhysBoneProperty(
                "MaxStretch", "Max Stretch", "maxStretch", AaoMergePhysBoneValueKind.Float,
                curveFieldName: "maxStretchCurve"),
            new AaoMergePhysBoneProperty(
                "MaxSquish", "Max Squish", "maxSquish", AaoMergePhysBoneValueKind.Float,
                curveFieldName: "maxSquishCurve", hasRange: true, rangeMin: 0f, rangeMax: 1f),
            new AaoMergePhysBoneProperty(
                "AllowGrabbing", "Allow Grabbing", "allowGrabbing",
                AaoMergePhysBoneValueKind.Permission, filterFieldName: "grabFilter"),
            new AaoMergePhysBoneProperty(
                "AllowPosing", "Allow Posing", "allowPosing",
                AaoMergePhysBoneValueKind.Permission, filterFieldName: "poseFilter"),
            new AaoMergePhysBoneProperty(
                "GrabMovement", "Grab Movement", "grabMovement",
                AaoMergePhysBoneValueKind.Float,
                hasRange: true, rangeMin: 0f, rangeMax: 1f),
            new AaoMergePhysBoneProperty(
                "SnapToHand", "Snap To Hand", "snapToHand", AaoMergePhysBoneValueKind.Bool),
            new AaoMergePhysBoneProperty(
                "ResetWhenDisabled", "Reset When Disabled", "resetWhenDisabled",
                AaoMergePhysBoneValueKind.Bool),
        };
    }
}
