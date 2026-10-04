using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace Poyo.CandyBox.HairSetAdjuster.Editor
{
    internal static class HairSetAdjusterColliders
    {
        private const float CenterRadiusRatio = 0.5f;
        private const float MinimumRatio = 0.75f;
        private const float MaximumRatio = 1.33f;
        private const float MaximumAxisAngle = 20f;
        private const string PlaneReason = "平面は対象外です";
        private const string BoneReason = "付いている骨が分かりません";
        private const string ShapeReason = "同じ骨に同じ形の Collider がありません";
        private const string DistanceReason = "位置か大きさが離れています";
        private const string UndoName = "Hair Set Adjuster Colliders";

        private struct WorldShape
        {
            internal Vector3 Center;
            internal Vector3 Axis;
            internal float Radius;
            internal float Height;
        }

        internal static HairSetColliderPlan Collect(HairSetTarget target, int poseVersion)
        {
            var plan = new HairSetColliderPlan
            {
                PoseVersion = poseVersion
            };
            var seen = new HashSet<VRCPhysBoneColliderBase>();
            var avatarColliders = new List<VRCPhysBoneColliderBase>();
            foreach (VRCPhysBoneColliderBase collider in
                target.AvatarRoot.GetComponentsInChildren<VRCPhysBoneColliderBase>(true))
            {
                if (!collider.transform.IsChildOf(target.Hair.transform))
                {
                    avatarColliders.Add(collider);
                }
            }

            int matches = 0;
            foreach (VRCPhysBoneBase bone in target.Hair.GetComponentsInChildren<VRCPhysBoneBase>(true))
            {
                if (bone.colliders == null)
                {
                    continue;
                }

                foreach (VRCPhysBoneColliderBase collider in bone.colliders)
                {
                    if (collider == null || !collider.transform.IsChildOf(target.Hair.transform) || !seen.Add(collider))
                    {
                        continue;
                    }

                    string reason;
                    VRCPhysBoneColliderBase candidate = FindCandidate(target, collider, avatarColliders, out reason);
                    string path = AnimationUtility.CalculateTransformPath(
                        collider.transform, target.AvatarRoot.transform);
                    plan.Rows.Add(new HairSetColliderRow
                    {
                        HairCollider = collider,
                        AvatarCollider = candidate,
                        Selected = candidate != null,
                        Label = candidate != null
                            ? path + " → " + AnimationUtility.CalculateTransformPath(
                                candidate.transform, target.AvatarRoot.transform)
                            : path + "  候補なし: " + reason,
                    });
                    if (candidate != null)
                    {
                        matches++;
                    }
                }
            }

            plan.SummaryLabel = "髪側の Collider " + plan.Rows.Count + " 個 / 候補あり " + matches + " 個";
            return plan;
        }

        private static string AttachedBone(HairSetTarget target, VRCPhysBoneColliderBase collider)
        {
            Transform root = collider.rootTransform != null ? collider.rootTransform : collider.transform;
            for (Transform t = root; t != null; t = t.parent)
            {
                if (target.HumanoidBoneNames.Contains(t.name))
                {
                    return t.name;
                }

                if (t == target.AvatarRoot.transform)
                {
                    break;
                }
            }

            return null;
        }

        private static WorldShape GetWorldShape(VRCPhysBoneColliderBase collider)
        {
            Transform root = collider.rootTransform != null ? collider.rootTransform : collider.transform;
            Vector3 lossy = root.lossyScale;
            // NOTE: 髪の拡大率を反映した世界の半径・高さで、本体との大きさを比較する。
            float scale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z));
            return new WorldShape
            {
                Center = root.TransformPoint(collider.position),
                Radius = collider.radius * scale,
                Height = collider.height * scale,
                Axis = (root.rotation * collider.rotation) * Vector3.up,
            };
        }

        private static VRCPhysBoneColliderBase FindCandidate(
            HairSetTarget target,
            VRCPhysBoneColliderBase hair,
            List<VRCPhysBoneColliderBase> candidates,
            out string reason)
        {
            reason = PlaneReason;
            if (hair.shapeType == VRCPhysBoneColliderBase.ShapeType.Plane)
            {
                return null;
            }

            string bone = AttachedBone(target, hair);
            reason = BoneReason;
            if (bone == null)
            {
                return null;
            }

            WorldShape source = GetWorldShape(hair);
            bool sameBoneShape = false;
            float bestDistance = float.PositiveInfinity;
            VRCPhysBoneColliderBase best = null;
            foreach (VRCPhysBoneColliderBase candidate in candidates)
            {
                if (candidate.shapeType != hair.shapeType || AttachedBone(target, candidate) != bone)
                {
                    continue;
                }

                sameBoneShape = true;
                if (candidate.insideBounds != hair.insideBounds)
                {
                    continue;
                }

                WorldShape shape = GetWorldShape(candidate);
                float distance = Vector3.Distance(source.Center, shape.Center);
                if (distance > CenterRadiusRatio * Mathf.Max(source.Radius, shape.Radius) ||
                    !RatioMatches(shape.Radius, source.Radius))
                {
                    continue;
                }

                if (hair.shapeType == VRCPhysBoneColliderBase.ShapeType.Capsule)
                {
                    float angle = Vector3.Angle(source.Axis, shape.Axis);
                    // NOTE: カプセルの軸は向きが逆でも同じ形なので、鋭角で比較する。
                    if (angle > 90f)
                    {
                        angle = 180f - angle;
                    }

                    if (angle > MaximumAxisAngle || !RatioMatches(shape.Height, source.Height))
                    {
                        continue;
                    }
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            reason = sameBoneShape ? DistanceReason : ShapeReason;
            return best;
        }

        private static bool RatioMatches(float avatar, float hair)
        {
            if (hair <= 0f)
            {
                return false;
            }

            float ratio = avatar / hair;
            return ratio >= MinimumRatio && ratio <= MaximumRatio;
        }

        internal static string Apply(HairSetTarget target, HairSetColliderPlan plan, bool deleteUnused)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            int group = Undo.GetCurrentGroup();
            int references = 0;
            int removed = 0;
            try
            {
                var mapping = new Dictionary<VRCPhysBoneColliderBase, VRCPhysBoneColliderBase>();
                foreach (HairSetColliderRow row in plan.Rows)
                {
                    if (row.Selected &&
                        row.HairCollider != null &&
                        row.AvatarCollider != null &&
                        row.HairCollider.transform.IsChildOf(target.Hair.transform) &&
                        !row.AvatarCollider.transform.IsChildOf(target.Hair.transform))
                    {
                        mapping[row.HairCollider] = row.AvatarCollider;
                    }
                }

                foreach (VRCPhysBoneBase bone in target.Hair.GetComponentsInChildren<VRCPhysBoneBase>(true))
                {
                    if (bone.colliders == null)
                    {
                        continue;
                    }

                    bool recorded = false;
                    // NOTE: 重複を取り除いても添字がずれないように、後ろから編集する。
                    for (int i = bone.colliders.Count - 1; i >= 0; i--)
                    {
                        VRCPhysBoneColliderBase source = bone.colliders[i];
                        if (source == null || !mapping.TryGetValue(source, out VRCPhysBoneColliderBase destination))
                        {
                            continue;
                        }

                        // NOTE: Undo は変更前に記録し、複数の PhysBone と削除を一回で戻せるようにする。
                        if (!recorded)
                        {
                            Undo.RecordObject(bone, UndoName);
                            recorded = true;
                        }

                        if (bone.colliders.Contains(destination))
                        {
                            bone.colliders.RemoveAt(i);
                        }
                        else
                        {
                            bone.colliders[i] = destination;
                        }

                        references++;
                    }

                    if (recorded)
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
                        EditorUtility.SetDirty(bone);
                    }
                }

                if (deleteUnused)
                {
                    // NOTE: 髪の外から使われている Collider を消さないため、付け替え後にアバター全体を調べる。
                    var used = new HashSet<VRCPhysBoneColliderBase>();
                    foreach (VRCPhysBoneBase bone in target.AvatarRoot.GetComponentsInChildren<VRCPhysBoneBase>(true))
                    {
                        if (bone.colliders != null)
                        {
                            foreach (VRCPhysBoneColliderBase collider in bone.colliders)
                            {
                                if (collider != null)
                                {
                                    used.Add(collider);
                                }
                            }
                        }
                    }

                    foreach (VRCPhysBoneColliderBase collider in mapping.Keys)
                    {
                        if (used.Contains(collider))
                        {
                            continue;
                        }

                        // NOTE: ボーン階層を残すため、削除するのはコンポーネントだけ。
                        Undo.DestroyObjectImmediate(collider);
                        removed++;
                    }
                }

                return "参照を " + references + " 件付け替えました。" +
                    (deleteUnused ? "Collider を " + removed + " 個削除しました。" : "削除はしていません。");
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
        }
    }
}
