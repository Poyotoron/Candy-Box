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
        private double _lastBakeTime;
        internal bool IsActive => _mesh != null && _material != null;
        internal void Begin(SkinnedMeshRenderer face, Color color)
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
            _mesh = new Mesh
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "CandyBoxFaceOverlay"
            };
            try
            {
                Bake();
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
            SceneView.RepaintAll();
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
        }

        internal void End()
        {
            SceneView.beforeSceneGui -= OnBeforeSceneGui;
            bool wasActive = _mesh != null || _material != null;
            if (_mesh != null)
            {
                Object.DestroyImmediate(_mesh);
            }

            if (_material != null)
            {
                Object.DestroyImmediate(_material);
            }

            _mesh = null;
            _material = null;
            _face = null;
            if (wasActive)
            {
                SceneView.RepaintAll();
            }
        }
    }
}
