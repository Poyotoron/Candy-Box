using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poyo.CandyBox.HairSetAdjuster.Editor
{
    internal sealed class HairSetTriangleGrid
    {
        private const float CellSize = 0.01f;
        private const float RayStep = 0.0025f;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private readonly Vector3[] _vertices;
        private readonly int[] _triangles;
        private readonly int[] _visited;
        private int _visitId;

        internal HairSetTriangleGrid(Vector3[] vertices, int[] triangles)
        {
            _vertices = vertices;
            _triangles = triangles;
            _visited = new int[triangles.Length / 3];
            for (int i = 0; i < _visited.Length; i++)
            {
                Vector3 a = vertices[triangles[i * 3]];
                Vector3 b = vertices[triangles[i * 3 + 1]];
                Vector3 c = vertices[triangles[i * 3 + 2]];
                Vector3 min = Vector3.Min(a, Vector3.Min(b, c));
                Vector3 max = Vector3.Max(a, Vector3.Max(b, c));
                for (int x = Mathf.FloorToInt(min.x / CellSize); x <= Mathf.FloorToInt(max.x / CellSize); x++)
                {
                    for (int y = Mathf.FloorToInt(min.y / CellSize); y <= Mathf.FloorToInt(max.y / CellSize); y++)
                    {
                        for (int z = Mathf.FloorToInt(min.z / CellSize); z <= Mathf.FloorToInt(max.z / CellSize); z++)
                        {
                            long key = Key(x, y, z);
                            if (!_cells.TryGetValue(key, out List<int> cell))
                            {
                                cell = new List<int>();
                                _cells.Add(key, cell);
                            }

                            // NOTE: 番号順のリストだけを走査し、辞書の列挙順に結果を依存させない。
                            cell.Add(i);
                        }
                    }
                }
            }
        }

        private static long Key(int x, int y, int z)
        {
            // NOTE: 負の格子番号にも使えるよう、各軸をずらして 20 ビットずつ詰める。
            return ((long)(x + 100000) << 40) | ((long)(y + 100000) << 20) | (long)(z + 100000);
        }

        internal float RayMin(Vector3 origin, Vector3 direction, float reach)
        {
            return RayMin(origin, direction, -reach, reach);
        }

        internal float RayMin(Vector3 origin, Vector3 direction, float min, float max)
        {
            if (_visitId == int.MaxValue)
            {
                Array.Clear(_visited, 0, _visited.Length);
                _visitId = 0;
            }

            _visitId++;
            float best = float.PositiveInfinity;
            int first = Mathf.FloorToInt(min / RayStep);
            int last = Mathf.CeilToInt(max / RayStep);
            for (int k = first; k <= last; k++)
            {
                Vector3 p = origin + direction * (k * RayStep);
                if (!_cells.TryGetValue(Key(
                    Mathf.FloorToInt(p.x / CellSize),
                    Mathf.FloorToInt(p.y / CellSize),
                    Mathf.FloorToInt(p.z / CellSize)), out List<int> cell))
                {
                    continue;
                }

                foreach (int i in cell)
                {
                    if (_visited[i] == _visitId)
                    {
                        continue;
                    }

                    _visited[i] = _visitId;
                    Vector3 a = _vertices[_triangles[i * 3]];
                    Vector3 e1 = _vertices[_triangles[i * 3 + 1]] - a;
                    Vector3 e2 = _vertices[_triangles[i * 3 + 2]] - a;
                    Vector3 h = Vector3.Cross(direction, e2);
                    float det = Vector3.Dot(e1, h);
                    // NOTE: 髪のカードは両面を調べ、頭皮の内側にある負の交差距離も残す。
                    if (Mathf.Abs(det) < 1e-12f)
                    {
                        continue;
                    }

                    float inverse = 1f / det;
                    Vector3 s = origin - a;
                    float u = Vector3.Dot(s, h) * inverse;
                    if (u < 0f || u > 1f)
                    {
                        continue;
                    }

                    Vector3 q = Vector3.Cross(s, e1);
                    float v = Vector3.Dot(direction, q) * inverse;
                    if (v < 0f || u + v > 1f)
                    {
                        continue;
                    }

                    float t = Vector3.Dot(e2, q) * inverse;
                    if (t >= min && t <= max && t < best)
                    {
                        best = t;
                    }
                }
            }

            return float.IsPositiveInfinity(best) ? float.NaN : best;
        }
    }
}
