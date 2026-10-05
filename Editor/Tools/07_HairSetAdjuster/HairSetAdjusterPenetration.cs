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
        internal static void CollectHairMesh(GameObject hair, List<Vector3> points, List<int> triangles)
        {
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

                    int baseIndex = points.Count;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        points.Add(matrix.MultiplyPoint3x4(vertex));
                    }

                    if (triangles != null)
                    {
                        for (int s = 0; s < mesh.subMeshCount; s++)
                        {
                            foreach (int index in mesh.GetTriangles(s))
                            {
                                triangles.Add(baseIndex + index);
                            }
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }

        internal static HairSetPenetrationResult Check(HairSetTarget target, int poseVersion, bool buildFaceMesh)
        {
            HairSetHeadField field = HairSetHeadField.Build(target);
            var points = new List<Vector3>();
            var triangles = new List<int>();
            CollectHairMesh(target.Hair, points, buildFaceMesh ? triangles : null);
            var inside = new bool[points.Count];
            var result = new HairSetPenetrationResult
            {
                Total = points.Count,
                PoseVersion = poseVersion
            };
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 point = points[i];
                field.Evaluate(point, out _, out inside[i]);
                if (!inside[i])
                {
                    continue;
                }

                result.Inside++;
                if (result.InsidePoints.Count < 2000)
                {
                    result.InsidePoints.Add(point);
                }
            }

            if (buildFaceMesh)
            {
                var vertices = new List<Vector3>();
                var indices = new List<int>();
                for (int i = 0; i < triangles.Count && result.FaceTriangleCount < 20000; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    if (!inside[a] && !inside[b] && !inside[c])
                    {
                        continue;
                    }

                    // NOTE: 三角形ごとに頂点を持たせ、元メッシュの索引を引き直す誤りを避ける。
                    indices.Add(vertices.Count);
                    vertices.Add(points[a]);
                    indices.Add(vertices.Count);
                    vertices.Add(points[b]);
                    indices.Add(vertices.Count);
                    vertices.Add(points[c]);
                    result.FaceTriangleCount++;
                }

                if (vertices.Count > 0)
                {
                    result.FaceMesh = new Mesh
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        name = "CandyBoxPenetrationFaces"
                    };
                    result.FaceMesh.SetVertices(vertices);
                    result.FaceMesh.SetTriangles(indices, 0);
                    result.FaceMesh.RecalculateBounds();
                }
            }

            result.Label = "めり込み " + result.Inside + " 頂点 / 全 " + result.Total + " 頂点";
            return result;
        }
    }
}
