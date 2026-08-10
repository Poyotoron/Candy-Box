using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Poyo.CandyBox.BlendshapeKeeper.Editor
{
    internal sealed class BlendshapeKeeperPreviewWindow : EditorWindow
    {
        private static readonly GUIContent MissingPreviewContent = new GUIContent(
            "走査結果が失われました。元のウィンドウから開き直してください。");
        private static readonly GUIContent ClipContent = new GUIContent("アニメーション");
        private static readonly GUIContent TimeContent = new GUIContent("時刻");
        private static readonly GUIContent ResetViewContent = new GUIContent("視点をリセット");
        private static readonly GUIContent BeforeContent = new GUIContent("修正前");
        private static readonly GUIContent AfterContent = new GUIContent("修正後");
        private static readonly GUIContent[] DisplayModeContents =
        {
            new GUIContent("左右に並べる"),
            new GUIContent("切り替え"),
        };
        private static readonly GUIContent[] BeforeAfterContents =
        {
            BeforeContent,
            AfterContent,
        };
        private static readonly GUIContent[] ViewModeContents =
        {
            new GUIContent("顔"),
            new GUIContent("全身"),
        };
        private static readonly Color BackgroundColor =
            new Color(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color AmbientColor =
            new Color(0.55f, 0.55f, 0.55f, 1f);
        private const string ExpandWindowMessage = "ウィンドウを縦に広げてください。";

        private GameObject _avatarRoot;
        private BlendshapeKeeperPlan _plan;
        private PreviewRenderUtility _previewUtility;
        private GameObject _copyRoot;
        private AnimationClip _modifiedClip;
        private int _clipIndex;
        private int _timeIndex;
        private float[] _times = Array.Empty<float>();
        private GUIContent[] _clipContents = Array.Empty<GUIContent>();
        private GUIContent[] _timeContents = Array.Empty<GUIContent>();
        private float _yaw;
        private float _pitch;
        private float _distance;
        private Vector3 _pivot;
        private float _initialDistance;
        private Vector3 _initialPivot;
        private float _minimumDistance;
        private float _maximumDistance;
        private Vector3 _facePivot;
        private float _faceDistance;
        private float _faceRadius;
        private Vector3 _bodyPivot;
        private float _bodyDistance;
        private float _bodyRadius;
        private bool _fullBody;
        private bool _sideBySide = true;
        private bool _showAfter;
        private bool _closeScheduled;
        private readonly List<SkinnedMeshRenderer> _targetRenderers =
            new List<SkinnedMeshRenderer>();
        private SkinnedMeshRenderer[] _allSkinnedRenderers =
            Array.Empty<SkinnedMeshRenderer>();
        private Renderer[] _allRenderers = Array.Empty<Renderer>();
        private RenderTexture _beforeTexture;
        private Transform _headBone;
        private Transform _leftEyeBone;
        private Transform _rightEyeBone;
        private float _headToNeckDistance;

        internal static void Open(GameObject avatarRoot, BlendshapeKeeperPlan plan)
        {
            var window = GetWindow<BlendshapeKeeperPreviewWindow>(
                true, "Blendshape Keeper プレビュー", true);
            window.minSize = new Vector2(480f, 420f);
            window.ReleasePreviewResources();
            window._avatarRoot = avatarRoot;
            window._plan = plan;
            window._clipIndex = 0;
            window._timeIndex = 0;
            window._closeScheduled = false;
            window._fullBody = false;
            window.BuildPreviewScene();
            window.Show();
        }

        internal static void CloseIfOpen()
        {
            BlendshapeKeeperPreviewWindow[] windows =
                Resources.FindObjectsOfTypeAll<BlendshapeKeeperPreviewWindow>();
            for (int windowIndex = 0; windowIndex < windows.Length; windowIndex++)
            {
                windows[windowIndex].Close();
            }
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            ReleasePreviewResources();
        }

        private void OnFocus()
        {
            if (_plan != null && _copyRoot != null)
            {
                RebuildModifiedClip();
                Repaint();
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            Close();
        }

        private void BuildPreviewScene()
        {
            if (_avatarRoot == null || _plan == null || _plan.Clips.Count == 0)
            {
                return;
            }

            _previewUtility = new PreviewRenderUtility();
            _previewUtility.camera.fieldOfView = 30f;
            _previewUtility.camera.nearClipPlane = 0.01f;
            _previewUtility.camera.farClipPlane = 100f;
            _previewUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            _previewUtility.camera.backgroundColor = ToRenderColor(BackgroundColor);
            _previewUtility.camera.cullingMask = ~0;
            // NOTE: 既定の環境光は黒で、環境光に依存するシェーダーが真っ黒になる。
            _previewUtility.ambientColor = ToRenderColor(AmbientColor);
            _previewUtility.lights[0].intensity = 1.1f;
            _previewUtility.lights[0].transform.rotation = Quaternion.Euler(20f, 200f, 0f);
            _previewUtility.lights[1].intensity = 0.6f;
            _previewUtility.lights[1].transform.rotation = Quaternion.Euler(20f, 340f, 0f);

            _copyRoot = Instantiate(_avatarRoot);
            _copyRoot.hideFlags = HideFlags.HideAndDontSave;
            _copyRoot.name = "CandyBoxPreview";
            _copyRoot.transform.position = Vector3.zero;
            _copyRoot.transform.rotation = Quaternion.identity;
            _copyRoot.SetActive(true);
            CaptureHumanoidBones();

            _allSkinnedRenderers =
                _copyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < _allSkinnedRenderers.Length;
                 rendererIndex++)
            {
                // NOTE: プレビュー用シーンではバウンズ更新が止まり、カリングされることがある。
                _allSkinnedRenderers[rendererIndex].updateWhenOffscreen = true;
                // NOTE: 更新契機が無く、スキニング前の姿勢で描かれることがある。
                _allSkinnedRenderers[rendererIndex]
                    .forceMatrixRecalculationPerRender = true;
            }

            CollectTargetRenderers();
            for (int rendererIndex = 0;
                 rendererIndex < _targetRenderers.Count;
                 rendererIndex++)
            {
                _targetRenderers[rendererIndex].enabled = true;
                ActivateHierarchy(_targetRenderers[rendererIndex].transform);
            }

            Animator[] animators = _copyRoot.GetComponentsInChildren<Animator>(true);
            for (int animatorIndex = 0; animatorIndex < animators.Length; animatorIndex++)
            {
                animators[animatorIndex].enabled = false;
            }

            _allRenderers = _copyRoot.GetComponentsInChildren<Renderer>(true);
            _previewUtility.AddSingleGO(_copyRoot);
            BuildClipContents();
            SelectClip(0);
            SampleClipForPreview(_plan.Clips[_clipIndex].Clip);
            InitializeView();
        }

        private void InitializeView()
        {
            float boneRadius = _headToNeckDistance;

            IReadOnlyList<SkinnedMeshRenderer> faceRenderers =
                _targetRenderers.Count > 0
                    ? _targetRenderers
                    : _allSkinnedRenderers;
            bool hasFaceBounds = TryCombineBounds(faceRenderers, out Bounds faceBounds);
            float boundsRadius = hasFaceBounds
                ? faceBounds.size.magnitude * 0.15f
                : 0f;
            float headRadius = boneRadius > 0.0001f ? boneRadius : boundsRadius;
            if (headRadius <= 0.0001f)
            {
                headRadius = 0.01f;
            }

            if (_leftEyeBone != null && _rightEyeBone != null)
            {
                _facePivot = (_leftEyeBone.position + _rightEyeBone.position) * 0.5f;
            }
            else if (_leftEyeBone != null)
            {
                _facePivot = _leftEyeBone.position;
            }
            else if (_rightEyeBone != null)
            {
                _facePivot = _rightEyeBone.position;
            }
            else if (_headBone != null)
            {
                _facePivot =
                    _headBone.position + _headBone.up * headRadius * 0.5f;
            }
            else
            {
                _facePivot = hasFaceBounds ? faceBounds.center : Vector3.zero;
            }

            float measuredRadius = MeasureRadius(faceRenderers, _facePivot);
            if (boneRadius <= 0.0001f && boundsRadius <= 0.0001f &&
                measuredRadius > 0.0001f)
            {
                headRadius = measuredRadius;
            }

            _faceRadius = Mathf.Max(measuredRadius, headRadius, 0.02f);
            _faceDistance = FitDistance(
                _faceRadius, _previewUtility.camera.fieldOfView);

            if (TryCombineBounds(_allRenderers, out Bounds bodyBounds))
            {
                _bodyPivot = bodyBounds.center;
                _bodyRadius = Mathf.Max(bodyBounds.extents.magnitude, 0.001f);
                _bodyDistance = FitDistance(
                    _bodyRadius, _previewUtility.camera.fieldOfView);
            }
            else
            {
                _bodyPivot = _facePivot;
                _bodyRadius = _faceRadius;
                _bodyDistance = _faceDistance;
            }

            UseCurrentViewMode();
        }

        private void CaptureHumanoidBones()
        {
            _headBone = null;
            _leftEyeBone = null;
            _rightEyeBone = null;
            _headToNeckDistance = 0f;

            Animator animator = _copyRoot.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            _headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            _leftEyeBone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            _rightEyeBone = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (_headBone == null)
            {
                return;
            }

            Transform lowerBone = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (lowerBone == null)
            {
                lowerBone = animator.GetBoneTransform(HumanBodyBones.Chest);
            }

            if (lowerBone != null)
            {
                _headToNeckDistance =
                    Vector3.Distance(_headBone.position, lowerBone.position);
            }
        }

        private void CollectTargetRenderers()
        {
            _targetRenderers.Clear();
            for (int clipIndex = 0; clipIndex < _plan.Clips.Count; clipIndex++)
            {
                BlendshapeKeeperClipPlan clipPlan = _plan.Clips[clipIndex];
                for (int changeIndex = 0;
                     changeIndex < clipPlan.Changes.Count;
                     changeIndex++)
                {
                    string path = clipPlan.Changes[changeIndex].Binding.path;
                    Transform targetTransform = string.IsNullOrEmpty(path)
                        ? _copyRoot.transform
                        : _copyRoot.transform.Find(path);
                    if (targetTransform == null)
                    {
                        continue;
                    }

                    SkinnedMeshRenderer renderer =
                        targetTransform.GetComponent<SkinnedMeshRenderer>();
                    if (renderer != null && !_targetRenderers.Contains(renderer))
                    {
                        _targetRenderers.Add(renderer);
                    }
                }
            }
        }

        private void ActivateHierarchy(Transform target)
        {
            Transform current = target;
            while (current != null)
            {
                current.gameObject.SetActive(true);
                if (current == _copyRoot.transform)
                {
                    break;
                }

                current = current.parent;
            }
        }

        private void SampleClipForPreview(AnimationClip clip)
        {
            if (clip != null)
            {
                float time = _times.Length > 0 ? _times[_timeIndex] : 0f;
                clip.SampleAnimation(_copyRoot, time);
            }

            // NOTE: 人型リターゲットではルートカーブが無くても体の位置が解き直される。
            _copyRoot.transform.position = Vector3.zero;
            _copyRoot.transform.rotation = Quaternion.identity;
        }

        private static float MeasureRadius(
            IReadOnlyList<SkinnedMeshRenderer> renderers, Vector3 pivot)
        {
            bool hasBounds = false;
            Bounds bounds = default;
            for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                if (renderer == null || !renderer.bounds.Contains(pivot))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (hasBounds)
            {
                return Vector3.Distance(pivot, bounds.center) +
                    bounds.extents.magnitude;
            }

            return TryCombineBounds(renderers, out bounds)
                ? Vector3.Distance(pivot, bounds.center) + bounds.extents.magnitude
                : 0f;
        }

        // NOTE: 半径の球を視野角へ収め、周囲に余白を確保する。
        private static float FitDistance(float radius, float fieldOfView)
        {
            float halfFieldOfView = Mathf.Deg2Rad * fieldOfView * 0.5f;
            return radius /
                Mathf.Max(0.0001f, Mathf.Sin(halfFieldOfView)) * 1.2f;
        }

        // NOTE: リニア色空間では、見た目の色をそのまま渡すと暗く描かれる。
        private static Color ToRenderColor(Color color)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Gamma
                ? color
                : color.linear;
        }

        private static bool TryCombineBounds<T>(
            IReadOnlyList<T> renderers, out Bounds bounds)
            where T : Renderer
        {
            bool hasBounds = false;
            bounds = default;
            for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
            {
                T renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        private void UseCurrentViewMode()
        {
            if (_fullBody)
            {
                _initialPivot = _bodyPivot;
                _initialDistance = _bodyDistance;
            }
            else
            {
                _initialPivot = _facePivot;
                _initialDistance = _faceDistance;
            }

            _minimumDistance = Mathf.Max(_initialDistance * 0.1f, 0.0001f);
            _maximumDistance = Mathf.Max(
                _initialDistance * 5f, _minimumDistance);

            ResetView();
        }

        private void ResetView()
        {
            _yaw = 180f;
            _pitch = 0f;
            _pivot = _initialPivot;
            _distance = _initialDistance;
            UpdateCameraClipping();
        }

        private void UpdateCameraClipping()
        {
            _previewUtility.camera.nearClipPlane =
                Mathf.Max(0.0001f, _distance * 0.01f);
            _previewUtility.camera.farClipPlane =
                _distance * 10f + 10f;
        }

        private void BuildClipContents()
        {
            _clipContents = new GUIContent[_plan.Clips.Count];
            for (int clipIndex = 0; clipIndex < _plan.Clips.Count; clipIndex++)
            {
                _clipContents[clipIndex] = new GUIContent(_plan.Clips[clipIndex].ClipLabel);
            }
        }

        private void SelectClip(int clipIndex)
        {
            _clipIndex = Mathf.Clamp(clipIndex, 0, _plan.Clips.Count - 1);
            _times = BlendshapeKeeperPreviewClip.CollectTimes(_plan.Clips[_clipIndex]);
            _timeIndex = Mathf.Clamp(_timeIndex, 0, Mathf.Max(0, _times.Length - 1));
            _timeContents = new GUIContent[_times.Length];
            for (int timeIndex = 0; timeIndex < _times.Length; timeIndex++)
            {
                _timeContents[timeIndex] = new GUIContent(
                    "t=" + _times[timeIndex].ToString("0.000"));
            }

            RebuildModifiedClip();
        }

        private void RebuildModifiedClip()
        {
            if (_modifiedClip != null)
            {
                DestroyImmediate(_modifiedClip);
                _modifiedClip = null;
            }

            if (_plan != null && _plan.Clips.Count > 0)
            {
                _clipIndex = Mathf.Clamp(_clipIndex, 0, _plan.Clips.Count - 1);
                _modifiedClip = BlendshapeKeeperPreviewClip.CreateModifiedClip(
                    _plan.Clips[_clipIndex]);
            }
        }

        private void OnGUI()
        {
            if (_plan == null || _avatarRoot == null || _copyRoot == null ||
                _previewUtility == null || _plan.Clips.Count == 0)
            {
                EditorGUILayout.HelpBox(MissingPreviewContent.text, MessageType.Warning);
                if (!_closeScheduled)
                {
                    _closeScheduled = true;
                    EditorApplication.delayCall += Close;
                }

                return;
            }

            int nextClipIndex = EditorGUILayout.Popup(
                ClipContent, Mathf.Clamp(_clipIndex, 0, _clipContents.Length - 1), _clipContents);
            if (nextClipIndex != _clipIndex)
            {
                SelectClip(nextClipIndex);
            }

            if (_times.Length > 0)
            {
                _timeIndex = EditorGUILayout.Popup(
                    TimeContent,
                    Mathf.Clamp(_timeIndex, 0, _timeContents.Length - 1),
                    _timeContents);
            }

            bool nextFullBody = GUILayout.Toolbar(
                _fullBody ? 1 : 0, ViewModeContents) == 1;
            if (nextFullBody != _fullBody)
            {
                _fullBody = nextFullBody;
                UseCurrentViewMode();
            }

            int displayMode = GUILayout.Toolbar(_sideBySide ? 0 : 1, DisplayModeContents);
            _sideBySide = displayMode == 0;
            if (!_sideBySide)
            {
                _showAfter = GUILayout.Toolbar(_showAfter ? 1 : 0, BeforeAfterContents) == 1;
            }

            if (GUILayout.Button(ResetViewContent))
            {
                ResetView();
            }

            Rect previewArea = GUILayoutUtility.GetRect(
                10f, 10f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (previewArea.height < 40f)
            {
                EditorGUI.HelpBox(previewArea, ExpandWindowMessage, MessageType.Info);
            }
            else if (_times.Length > 0)
            {
                HandleViewInput(previewArea);
                DrawPreviewArea(previewArea);
            }
        }

        private void DrawPreviewArea(Rect previewArea)
        {
            if (_sideBySide)
            {
                float width = (previewArea.width - 4f) * 0.5f;
                Rect beforeRect = new Rect(
                    previewArea.x,
                    previewArea.y,
                    width,
                    previewArea.height - 20f);
                Rect afterRect = new Rect(
                    previewArea.x + width + 4f,
                    previewArea.y,
                    width,
                    previewArea.height - 20f);
                if (CanDrawViewport(beforeRect) && CanDrawViewport(afterRect))
                {
                    DrawViewport(beforeRect, false, true);
                    DrawViewport(afterRect, true, false);
                    GUI.Label(
                        new Rect(beforeRect.x, beforeRect.yMax, beforeRect.width, 20f),
                        BeforeContent,
                        EditorStyles.centeredGreyMiniLabel);
                    GUI.Label(
                        new Rect(afterRect.x, afterRect.yMax, afterRect.width, 20f),
                        AfterContent,
                        EditorStyles.centeredGreyMiniLabel);
                }

                return;
            }

            Rect viewport = new Rect(
                previewArea.x,
                previewArea.y,
                previewArea.width,
                previewArea.height - 20f);
            if (!CanDrawViewport(viewport))
            {
                return;
            }

            DrawViewport(viewport, _showAfter, false);
            GUI.Label(
                new Rect(viewport.x, viewport.yMax, viewport.width, 20f),
                _showAfter ? AfterContent : BeforeContent,
                EditorStyles.centeredGreyMiniLabel);
        }

        private static bool CanDrawViewport(Rect rect)
        {
            return rect.width >= 4f && rect.height >= 4f;
        }

        private void DrawViewport(Rect rect, bool after, bool preserveTexture)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            AnimationClip clip = after
                ? _modifiedClip
                : _plan.Clips[_clipIndex].Clip;
            // NOTE: AnimationMode はエディタ全体へ影響するため、隔離した複製へ直接適用する。
            SampleClipForPreview(clip);

            _previewUtility.BeginPreview(rect, GUIStyle.none);
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            _previewUtility.camera.transform.position =
                _pivot + rotation * new Vector3(0f, 0f, -_distance);
            _previewUtility.camera.transform.rotation = rotation;
            // NOTE: 第 1 引数はスクリプタブルレンダーパイプラインの許可。
            //       ビルトインのプロジェクトで true にすると何も描画されない。
            bool useScriptableRenderPipeline =
                GraphicsSettings.currentRenderPipeline != null;
            _previewUtility.Render(useScriptableRenderPipeline, true);
            Texture texture = _previewUtility.EndPreview();
            if (preserveTexture)
            {
                EnsureBeforeTexture(rect, texture);
                RenderTexture previousTarget = RenderTexture.active;
                try
                {
                    Graphics.Blit(texture, _beforeTexture);
                }
                finally
                {
                    // NOTE: Blit は描画先を変更するため、以降の GUI 描画前に必ず戻す。
                    RenderTexture.active = previousTarget;
                }

                texture = _beforeTexture;
            }

            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        private void EnsureBeforeTexture(Rect rect, Texture texture)
        {
            int width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            RenderTexture source = texture as RenderTexture;
            if (_beforeTexture != null &&
                _beforeTexture.width == width &&
                _beforeTexture.height == height &&
                (source == null
                    ? _beforeTexture.format == RenderTextureFormat.ARGB32
                    : _beforeTexture.graphicsFormat == source.graphicsFormat))
            {
                return;
            }

            ReleaseBeforeTexture();
            if (source != null)
            {
                // NOTE: EndPreview の結果と同じ形式にし、左右の色空間を揃える。
                _beforeTexture = new RenderTexture(width, height, 0)
                {
                    graphicsFormat = source.graphicsFormat,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            else
            {
                _beforeTexture = new RenderTexture(
                    width, height, 0, RenderTextureFormat.ARGB32)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            _beforeTexture.Create();
        }

        private void ReleaseBeforeTexture()
        {
            if (_beforeTexture == null)
            {
                return;
            }

            _beforeTexture.Release();
            DestroyImmediate(_beforeTexture);
            _beforeTexture = null;
        }

        private void HandleViewInput(Rect previewArea)
        {
            Event currentEvent = Event.current;
            if (!previewArea.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
            {
                _yaw += currentEvent.delta.x * 0.5f;
                _pitch = Mathf.Clamp(_pitch + currentEvent.delta.y * 0.5f, -80f, 80f);
                currentEvent.Use();
                Repaint();
            }
            else if (currentEvent.type == EventType.ScrollWheel)
            {
                _distance = Mathf.Clamp(
                    _distance + currentEvent.delta.y * 0.02f,
                    _minimumDistance,
                _maximumDistance);
                UpdateCameraClipping();
                currentEvent.Use();
                Repaint();
            }
        }

        private void ReleasePreviewResources()
        {
            ReleaseBeforeTexture();

            if (_modifiedClip != null)
            {
                DestroyImmediate(_modifiedClip);
                _modifiedClip = null;
            }

            if (_copyRoot != null)
            {
                DestroyImmediate(_copyRoot);
                _copyRoot = null;
            }

            if (_previewUtility != null)
            {
                _previewUtility.Cleanup();
                _previewUtility = null;
            }

            _targetRenderers.Clear();
            _allSkinnedRenderers = Array.Empty<SkinnedMeshRenderer>();
            _allRenderers = Array.Empty<Renderer>();
            _headBone = null;
            _leftEyeBone = null;
            _rightEyeBone = null;
            _headToNeckDistance = 0f;
        }
    }
}
