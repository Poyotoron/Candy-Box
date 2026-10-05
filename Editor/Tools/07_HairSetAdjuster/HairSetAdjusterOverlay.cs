using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Poyo.CandyBox.HairSetAdjuster.Editor
{
    internal sealed class HairSetFaceOverlay
    {
        private SkinnedMeshRenderer _face;
        private Mesh _mesh;
        private Material _material;
        private Mesh _hiddenMesh;
        private Material _hiddenMaterial;
        private bool _showHidden;
        private bool _hasHiddenTriangles;
        private double _lastBakeTime;
        internal bool IsActive => _mesh != null && _material != null;
        internal void Begin(SkinnedMeshRenderer face, Color color, int[] headVertexIndices, Vector3 headCenter, bool showHidden)
        {
            End();
            if (face == null || face.sharedMesh == null)
            {
                return;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Debug.LogWarning("Candy Box: 顔の重ね描きに使うシェーダーが見つかりませんでした。");
                return;
            }

            _face = face;
            _material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "CandyBoxFaceOverlay"
            };
            _material.SetColor("_Color", color);
            _material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            _material.SetInt("_ZWrite", 1);
            _material.SetInt("_Cull", (int)CullMode.Back);
            _hiddenMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "CandyBoxFaceOverlayHidden",
                // NOTE: 髪の深度が揃ってから隠れた部分を判定するため、最後に描く。
                renderQueue = 4000
            };
            _hiddenMaterial.SetInt("_ZTest", (int)CompareFunction.Greater);
            // NOTE: 透かした顔が後続の描画を隠さないよう、深度には書き込まない。
            _hiddenMaterial.SetInt("_ZWrite", 0);
            _hiddenMaterial.SetInt("_Cull", (int)CullMode.Back);
            _hiddenMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _hiddenMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _hiddenMesh = new Mesh
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "CandyBoxFaceOverlayHidden"
            };
            _showHidden = showHidden;
            SetColor(color);
            _mesh = new Mesh
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "CandyBoxFaceOverlay"
            };
            try
            {
                Bake();
                SelectHiddenTriangles(headVertexIndices, headCenter);
            }
            catch
            {
                End();
                throw;
            }

            // NOTE: シーンの深度描画に参加させ、手前の髪を単色の顔で覆わない。
            SceneView.beforeSceneGui += OnBeforeSceneGui;
            SceneView.RepaintAll();
        }

        internal void SetColor(Color color)
        {
            if (_material == null)
            {
                return;
            }

            _material.SetColor("_Color", color);
            color.a = 0.35f;
            _hiddenMaterial.SetColor("_Color", color);
            SceneView.RepaintAll();
        }

        internal void SetShowHidden(bool showHidden)
        {
            _showHidden = showHidden;
            if (IsActive)
            {
                SceneView.RepaintAll();
            }
        }

        private void SelectHiddenTriangles(int[] headVertexIndices, Vector3 headCenter)
        {
            Vector3[] vertices = _mesh.vertices;
            var isHead = new bool[vertices.Length];
            foreach (int index in headVertexIndices)
            {
                isHead[index] = true;
            }

            Vector3 localCenter = Matrix4x4.TRS(
                _face.transform.position, _face.transform.rotation, Vector3.one).inverse.MultiplyPoint3x4(headCenter);
            var triangles = new List<int>();
            for (int s = 0; s < _mesh.subMeshCount; s++)
            {
                int[] indices = _mesh.GetTriangles(s);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = indices[i];
                    int b = indices[i + 1];
                    int c = indices[i + 2];
                    // NOTE: 頭以外と内向きの面を除かないと、体や目・口の内側まで透けてしまう。
                    // NOTE: 裏面の破棄は巻き方向で決まるため、頂点法線で向きを補正しない。
                    if (isHead[a] && isHead[b] && isHead[c] &&
                        Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]),
                            (vertices[a] + vertices[b] + vertices[c]) / 3f - localCenter) > 0f)
                    {
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);
                    }
                }
            }

            if (vertices.Length > 65535)
            {
                _hiddenMesh.indexFormat = IndexFormat.UInt32;
            }

            _hiddenMesh.SetTriangles(triangles, 0);
            _hasHiddenTriangles = triangles.Count > 0;
        }

        private void Bake()
        {
            _face.BakeMesh(_mesh, true);
            Vector3[] vertices = _mesh.vertices;
            Vector3[] normals = _mesh.normals;
            // NOTE: 元の顔と同じ深さでは Z ファイティングするため、わずかに押し出す。
            if (normals.Length == vertices.Length)
            {
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] += normals[i].normalized * 0.0005f;
                }

                _mesh.vertices = vertices;
                _mesh.RecalculateBounds();
            }

            _hiddenMesh.SetVertices(vertices);
            _hiddenMesh.RecalculateBounds();
            _lastBakeTime = EditorApplication.timeSinceStartup;
        }

        private void OnBeforeSceneGui(SceneView sceneView)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (_face == null || _mesh == null || _material == null)
            {
                End();
                return;
            }

            // NOTE: ドラッグ中の再描画で毎回焼き直す負荷を避ける。
            if (EditorApplication.timeSinceStartup - _lastBakeTime >= 0.2)
            {
                Bake();
            }

            // NOTE: BakeMesh は拡大を焼き込むので、行列には拡大を含めない。
            Matrix4x4 matrix = Matrix4x4.TRS(_face.transform.position, _face.transform.rotation, Vector3.one);
            for (int i = 0; i < _mesh.subMeshCount; i++)
            {
                Graphics.DrawMesh(_mesh, matrix, _material, 0, sceneView.camera, i);
            }

            if (_showHidden && _hasHiddenTriangles)
            {
                Graphics.DrawMesh(_hiddenMesh, matrix, _hiddenMaterial, 0, sceneView.camera, 0);
            }
        }

        internal void End()
        {
            SceneView.beforeSceneGui -= OnBeforeSceneGui;
            bool wasActive = _mesh != null || _material != null || _hiddenMesh != null || _hiddenMaterial != null;
            if (_mesh != null)
            {
                Object.DestroyImmediate(_mesh);
            }

            if (_material != null)
            {
                Object.DestroyImmediate(_material);
            }

            // NOTE: 表示専用の資源は、終了するたびに破棄して再コンパイルや再生への持ち越しを防ぐ。
            if (_hiddenMesh != null)
            {
                Object.DestroyImmediate(_hiddenMesh);
            }

            if (_hiddenMaterial != null)
            {
                Object.DestroyImmediate(_hiddenMaterial);
            }

            _mesh = null;
            _material = null;
            _hiddenMesh = null;
            _hiddenMaterial = null;
            _hasHiddenTriangles = false;
            _face = null;
            if (wasActive)
            {
                SceneView.RepaintAll();
            }
        }
    }
}
