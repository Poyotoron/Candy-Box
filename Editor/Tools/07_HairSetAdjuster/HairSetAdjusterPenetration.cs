using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.HairSetAdjuster.Editor
{
    internal sealed class HairSetHeadField
    {
        private const float CellSize = 0.01f;
        private const float NearDistance = 0.02f;
        private const float InsideTolerance = 0.001f;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private Vector3[] _points;
        private Vector3[] _normals;

        internal static HairSetHeadField Build(HairSetTarget target)
        {
            var field = new HairSetHeadField();
            var mesh = new Mesh
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            try
            {
                target.Face.BakeMesh(mesh, true);
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                // NOTE: 拡大は焼き込み済みなので位置と回転だけでワールドへ直す。
                Matrix4x4 matrix = Matrix4x4.TRS(
                    target.Face.transform.position,
                    target.Face.transform.rotation,
                    Vector3.one);
                field._points = new Vector3[target.HeadVertexIndices.Length];
                field._normals = new Vector3[field._points.Length];
                for (int i = 0; i < field._points.Length; i++)
                {
                    int index = target.HeadVertexIndices[i];
                    Vector3 p = matrix.MultiplyPoint3x4(vertices[index]);
                    field._points[i] = p;
                    field._normals[i] = index < normals.Length
                        ? matrix.MultiplyVector(normals[index]).normalized
                        : Vector3.zero;
                    long key = Key(
                        Mathf.FloorToInt(p.x / CellSize),
                        Mathf.FloorToInt(p.y / CellSize),
                        Mathf.FloorToInt(p.z / CellSize));
                    if (!field._cells.TryGetValue(key, out List<int> cell))
                    {
                        cell = new List<int>();
                        field._cells.Add(key, cell);
                    }

                    cell.Add(i);
                }

                return field;
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        private static long Key(int ix, int iy, int iz)
        {
            // NOTE: 3 軸の格子番号を 20 ビットずつ詰める。負の番号を避けるため 100000 を足す。
            return ((long)(ix + 100000) << 40) | ((long)(iy + 100000) << 20) | (long)(iz + 100000);
        }

        internal void Evaluate(Vector3 point, out bool near, out bool inside)
        {
            int ix = Mathf.FloorToInt(point.x / CellSize);
            int iy = Mathf.FloorToInt(point.y / CellSize);
            int iz = Mathf.FloorToInt(point.z / CellSize);
            int closest = -1;
            float distance = float.PositiveInfinity;
            for (int x = -2; x <= 2; x++)
            {
                for (int y = -2; y <= 2; y++)
                {
                    for (int z = -2; z <= 2; z++)
                    {
                        if (!_cells.TryGetValue(Key(ix + x, iy + y, iz + z), out List<int> cell))
                        {
                            continue;
                        }

                        foreach (int index in cell)
                        {
                            float d = (point - _points[index]).sqrMagnitude;
                            if (d < distance)
                            {
                                distance = d;
                                closest = index;
                            }
                        }
                    }
                }
            }

            near = closest >= 0 && distance <= NearDistance * NearDistance;
            inside = near && Vector3.Dot(point - _points[closest], _normals[closest]) < -InsideTolerance;
        }
    }

    internal static class HairSetAdjusterPenetration
    {
        private static readonly Vector3[] SearchAxes =
        {
            Vector3.up,
            Vector3.forward,
            Vector3.up
        };
        private static readonly float[] SearchRanges =
        {
            0.10f,
            0.03f,
            0.01f
        };
        private static readonly float[] SearchSteps =
        {
            0.01f,
            0.005f,
            0.002f
        };
        internal static List<Vector3> CollectHairPoints(GameObject hair)
        {
            var points = new List<Vector3>();
            var baked = new Mesh
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            try
            {
                foreach (Renderer renderer in hair.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    Mesh mesh;
                    Matrix4x4 matrix;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        if (skinned.sharedMesh == null)
                        {
                            continue;
                        }

                        baked.Clear();
                        skinned.BakeMesh(baked, true);
                        mesh = baked;
                        // NOTE: スキンメッシュだけは拡大が焼き込み済みで、通常の MeshRenderer は行列で拡大する。
                        matrix = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                    }
                    else
                    {
                        if (renderer is MeshRenderer)
                        {
                            MeshFilter filter = renderer.GetComponent<MeshFilter>();
                            if (filter == null || filter.sharedMesh == null)
                            {
                                continue;
                            }

                            mesh = filter.sharedMesh;
                            matrix = renderer.localToWorldMatrix;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        points.Add(matrix.MultiplyPoint3x4(vertex));
                    }
                }

                return points;
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }

        internal static HairSetPenetrationResult Check(HairSetTarget target, int poseVersion)
        {
            HairSetHeadField field = HairSetHeadField.Build(target);
            List<Vector3> points = CollectHairPoints(target.Hair);
            var result = new HairSetPenetrationResult
            {
                Total = points.Count,
                PoseVersion = poseVersion
            };
            foreach (Vector3 point in points)
            {
                field.Evaluate(point, out _, out bool inside);
                if (!inside)
                {
                    continue;
                }

                result.Inside++;
                if (result.InsidePoints.Count < 2000)
                {
                    result.InsidePoints.Add(point);
                }
            }

            result.Label = "めり込み " + result.Inside + " 頂点 / 全 " + result.Total + " 頂点";
            return result;
        }

        internal static Vector3 Search(HairSetTarget target, HairSetBasePose basePose, HairSetOffset start)
        {
            List<Vector3> all = CollectHairPoints(target.Hair);
            var samples = new List<Vector3>();
            // NOTE: 固定間隔で間引き、同じ開始姿勢には同じ結果を返す。
            int stride = Mathf.Max(1, Mathf.CeilToInt(all.Count / 4096f));
            for (int i = 0; i < all.Count; i += stride)
            {
                samples.Add(all[i]);
            }

            HairSetHeadField field = HairSetHeadField.Build(target);
            Vector3 best = Vector3.zero;
            int bestScore = Score(field, samples, best);
            try
            {
                for (int stage = 0; stage < SearchAxes.Length; stage++)
                {
                    Vector3 center = best;
                    Vector3 axis = basePose.RootRotation * SearchAxes[stage];
                    int n = Mathf.RoundToInt(SearchRanges[stage] / SearchSteps[stage]);
                    for (int i = -n; i <= n; i++)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Candy Box",
                            "めり込みが少ない位置を探しています…",
                            (stage + (float)(i + n) / (2 * n + 1)) / SearchAxes.Length);
                        // NOTE: 浮動小数の刻みを足し上げず、整数の候補番号から位置を計算する。
                        Vector3 delta = center + axis * (i * SearchSteps[stage]);
                        Vector3 position = start.Position
                            + HairSetAdjusterPose.WorldDeltaToOffset(basePose, start, delta);
                        if (Mathf.Abs(position.x) > 0.15f
                            || Mathf.Abs(position.y) > 0.15f
                            || Mathf.Abs(position.z) > 0.15f)
                        {
                            continue;
                        }

                        int score = Score(field, samples, delta);
                        if (score > bestScore || (score == bestScore && delta.sqrMagnitude < best.sqrMagnitude))
                        {
                            best = delta;
                            bestScore = score;
                        }
                    }
                }

                return start.Position + HairSetAdjusterPose.WorldDeltaToOffset(basePose, start, best);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static int Score(HairSetHeadField field, List<Vector3> samples, Vector3 delta)
        {
            int score = 0;
            // NOTE: めり込みだけを減らすと髪が浮くため、頭の近くにある頂点数も評価する。
            //       探索中は Transform に触れず、点の平行移動だけで評価する。
            foreach (Vector3 point in samples)
            {
                field.Evaluate(point + delta, out bool near, out bool inside);
                if (near)
                {
                    score++;
                }

                if (inside)
                {
                    score -= 10;
                }
            }

            return score;
        }
    }
}
