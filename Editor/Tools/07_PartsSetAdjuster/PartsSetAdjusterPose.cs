using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.PartsSetAdjuster.Editor
{
    internal static class PartsSetAdjusterPose
    {
        internal static PartsSetBasePose Capture(PartsSetTarget target)
        {
            if (target == null || !target.IsValid || target.AdjustTarget == null)
            {
                return default;
            }

            return new PartsSetBasePose
            {
                Position = target.AdjustTarget.position,
                Rotation = target.AdjustTarget.rotation,
                LocalScale = target.AdjustTarget.localScale,
                RootPosition = target.AvatarRoot.transform.position,
                RootRotation = target.AvatarRoot.transform.rotation,
                IsValid = true,
            };
        }

        internal static void Apply(PartsSetTarget target, PartsSetBasePose basePose, PartsSetOffset offset)
        {
            Quaternion r = basePose.RootRotation;
            Quaternion rr = r * Quaternion.Euler(offset.Angles) * Quaternion.Inverse(r);
            Vector3 scale = offset.AxisScale * offset.Scale;
            // NOTE: 毎回基準から計算し、対象のローカル軸で拡大することで誤差の蓄積とせん断を避ける。
            // NOTE: 平行移動を先に掛けないと、回転と拡大の不動点が頭の中心から位置オフセットの分だけずれる。
            Vector3 v = Forward(
                rr,
                basePose.Rotation,
                scale,
                basePose.Position + r * offset.Position - target.ReferenceCenter);
            target.AdjustTarget.SetPositionAndRotation(target.ReferenceCenter + v, rr * basePose.Rotation);
            target.AdjustTarget.localScale = Vector3.Scale(basePose.LocalScale, scale);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target.AdjustTarget);
        }

        internal static PartsSetOffset Solve(PartsSetTarget target, PartsSetBasePose basePose)
        {
            Quaternion r = basePose.RootRotation;
            Quaternion rr = target.AdjustTarget.rotation * Quaternion.Inverse(basePose.Rotation);
            Vector3 angles = (Quaternion.Inverse(r) * rr * r).eulerAngles;
            Vector3 current = target.AdjustTarget.localScale;
            Vector3 original = basePose.LocalScale;
            Vector3 scale = new Vector3(
                Divide(current.x, original.x),
                Divide(current.y, original.y),
                Divide(current.z, original.z));
            PartsSetOffset offset = PartsSetOffset.Identity;
            offset.Angles = new Vector3(SignedAngle(angles.x), SignedAngle(angles.y), SignedAngle(angles.z));
            if (Mathf.Abs(scale.x - scale.y) <= 1e-4f &&
                Mathf.Abs(scale.x - scale.z) <= 1e-4f &&
                Mathf.Abs(scale.y - scale.z) <= 1e-4f)
            {
                offset.Scale = scale.x;
            }
            else
            {
                offset.AxisScale = scale;
            }

            offset.Position = Quaternion.Inverse(r) *
                (Inverse(basePose, offset, target.AdjustTarget.position - target.ReferenceCenter) -
                    (basePose.Position - target.ReferenceCenter));
            return offset;
        }

        internal static bool HasRootMoved(PartsSetTarget target, PartsSetBasePose basePose)
        {
            return Vector3.Distance(target.AvatarRoot.transform.position, basePose.RootPosition) > 1e-4f ||
                Quaternion.Angle(target.AvatarRoot.transform.rotation, basePose.RootRotation) > 0.01f;
        }

        private static Vector3 Forward(Quaternion rr, Quaternion a, Vector3 s, Vector3 v)
        {
            return rr * (a * Vector3.Scale(s, Quaternion.Inverse(a) * v));
        }

        internal static Vector3 Inverse(PartsSetBasePose basePose, PartsSetOffset offset, Vector3 worldDelta)
        {
            Quaternion r = basePose.RootRotation;
            Quaternion rr = r * Quaternion.Euler(offset.Angles) * Quaternion.Inverse(r);
            Quaternion a = basePose.Rotation;
            Vector3 s = offset.AxisScale * offset.Scale;
            Vector3 v = Quaternion.Inverse(a) * (Quaternion.Inverse(rr) * worldDelta);
            return a * Vector3.Scale(v, new Vector3(Divide(1f, s.x), Divide(1f, s.y), Divide(1f, s.z)));
        }

        internal static Vector3 WorldDeltaToOffset(PartsSetBasePose basePose, PartsSetOffset offset, Vector3 worldDelta)
        {
            // NOTE: ワールドでの移動量も回転と拡大の逆写像を通さないと、頭合わせと探索が狙った位置へ届かない。
            return Quaternion.Inverse(basePose.RootRotation) * Inverse(basePose, offset, worldDelta);
        }

        private static float Divide(float value, float divisor)
        {
            return Mathf.Abs(divisor) < 1e-6f ? 1f : value / divisor;
        }

        private static float SignedAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
