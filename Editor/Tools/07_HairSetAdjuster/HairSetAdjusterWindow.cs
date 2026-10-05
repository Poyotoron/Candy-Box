using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.Dynamics;

namespace Poyo.CandyBox.HairSetAdjuster.Editor
{
    internal sealed class HairSetAdjusterWindow : EditorWindow
    {
        private static readonly GUIContent FaceContent = new GUIContent("顔メッシュ");
        private static readonly GUIContent HairContent = new GUIContent("髪");
        private static readonly GUIContent TargetContent = new GUIContent("調整の対象（空欄なら自動）");
        private static readonly GUIContent ColorContent = new GUIContent("色");
        private static readonly GUIContent ShowHiddenContent = new GUIContent("髪に隠れた部分も表示");
        private static readonly GUIContent PenetrationDisplayContent = new GUIContent("表し方");
        private static readonly GUIContent[] PenetrationDisplayContents =
        {
            new GUIContent("面"),
            new GUIContent("点"),
            new GUIContent("面と点")
        };
        private static readonly GUIContent SliderRangeContent = new GUIContent("スライダーの幅");
        private static readonly GUIContent RightContent = new GUIContent("左右");
        private static readonly GUIContent UpContent = new GUIContent("上下");
        private static readonly GUIContent ForwardContent = new GUIContent("前後");
        private static readonly GUIContent PitchContent = new GUIContent("前後の傾き");
        private static readonly GUIContent YawContent = new GUIContent("左右の向き");
        private static readonly GUIContent RollContent = new GUIContent("左右の傾き");
        private static readonly GUIContent ScaleContent = new GUIContent("大きさ");
        private static readonly GUIContent XContent = new GUIContent("X");
        private static readonly GUIContent YContent = new GUIContent("Y");
        private static readonly GUIContent ZContent = new GUIContent("Z");
        private static readonly GUIContent[] SliderRangeContents =
        {
            new GUIContent("大きく"),
            new GUIContent("ふつう"),
            new GUIContent("細かく")
        };
        private static readonly float[] PositionHalfWidths = { 0.15f, 0.03f, 0.005f };
        private static readonly float[] AngleHalfWidths = { 30f, 5f, 1f };
        private static readonly float[] ScaleHalfWidths = { 0.2f, 0.05f, 0.01f };
        private const float PositionLimit = 1f;
        private const float AngleLimit = 180f;
        private const float ScaleMin = 0.1f;
        private const float ScaleMax = 10f;
        private const float SliderFieldWidth = 64f;
        private static readonly GUIContent AutoFitContent = new GUIContent("自動で合わせる");
        private static readonly GUIContent FitScaleContent = new GUIContent("大きさも合わせる");
        private static readonly GUIContent CaptureContent = new GUIContent("基準をいまの姿勢にする");
        private static readonly GUIContent ResetContent = new GUIContent("基準に戻す");
        private static readonly GUIContent OverlayHelp = new GUIContent("Scene ビューにだけ表示します。シーンやマテリアルは変更しません。");
        private static readonly GUIContent ColliderHelp = new GUIContent("位置を合わせてから確認してください。位置が合っていないと、同じ役目の Collider も見つかりません。");
        private static readonly GUIContent[] ColorContents =
        {
            new GUIContent("マゼンタ"),
            new GUIContent("シアン"),
            new GUIContent("黄")
        };

        private static readonly Color[] OverlayColors =
        {
            new Color(1f, 0f, 0.8f, 1f),
            new Color(0f, 0.9f, 1f, 1f),
            new Color(1f, 0.9f, 0f, 1f)
        };

        private const string RootMovedMessage = "アバターが動いたため、「基準をいまの姿勢にする」を押してください。";
        private const string StalePenetrationMessage = "チェックのあとで動かしました。もう一度チェックしてください。";
        private const string StaleColliderMessage = "確認のあとで位置を動かしました。もう一度確認してください。";

        [SerializeField] private SkinnedMeshRenderer _face;
        [SerializeField] private GameObject _hair;
        [SerializeField] private Transform _adjustTargetOverride;
        [SerializeField] private HairSetOffset _offset = HairSetOffset.Identity;
        [SerializeField] private bool _overlayEnabled;
        [SerializeField] private bool _overlayShowHidden = true;
        [SerializeField] private int _overlayColorIndex;
        [SerializeField] private bool _showPenetration = true;
        [SerializeField] private int _penetrationDisplayIndex;
        [SerializeField] private bool _deleteUnusedColliders;
        [SerializeField] private int _sliderRangeIndex;
        [SerializeField] private bool _fitScale = true;
        private Material _penetrationMaterial;
        private HairSetOffset _sliderCenter;
        private HairSetTarget _target;
        private HairSetBasePose _basePose;
        private int _dragUndoGroup = -1;
        private int _poseVersion;
        private Vector2 _scroll;
        private bool _axisScaleExpanded;
        private bool _hasPhysBones;
        private string _fitResult;
        private string _colliderResult;
        private string _migrationConfirmation;
        private int _selectedColliderCount;
        private HairSetPenetrationResult _penetration;
        private HairSetColliderPlan _colliderPlan;
        private readonly HairSetFaceOverlay _overlay = new HairSetFaceOverlay();
        private bool HasTarget => _target != null &&
            _target.IsValid &&
            _target.Face != null &&
            _target.Hair != null &&
            _target.AdjustTarget != null &&
            _target.HeadBone != null &&
            _target.AvatarRoot != null &&
            !EditorApplication.isPlayingOrWillChangePlaymode;
        internal static void Open()
        {
            var window = GetWindow<HairSetAdjusterWindow>(false, "07_Hair Set Adjuster", true);
            window.minSize = new Vector2(560f, 700f);
            window.Show();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload += EndDisplays;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SceneView.duringSceneGui += DrawPenetration;
            SceneView.beforeSceneGui += DrawPenetrationFaces;
            ResolveTarget();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= EndDisplays;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneView.duringSceneGui -= DrawPenetration;
            SceneView.beforeSceneGui -= DrawPenetrationFaces;
            CommitDrag();
            EndDisplays();
        }

        private void EndDisplays()
        {
            _overlay.End();
            SetPenetration(null);
            if (_penetrationMaterial != null)
            {
                DestroyImmediate(_penetrationMaterial);
                _penetrationMaterial = null;
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                EndDisplays();
                CommitDrag();
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                ResolveTarget();
            }

            Repaint();
        }

        private void ResolveTarget()
        {
            CommitDrag();
            Transform previous = _target != null ? _target.AdjustTarget : null;
            SkinnedMeshRenderer previousFace = _target != null ? _target.Face : null;
            GameObject previousHair = _target != null ? _target.Hair : null;
            _target = HairSetAdjusterTarget.Resolve(_face, _hair, _adjustTargetOverride);
            if (!HasTarget || previous != _target.AdjustTarget)
            {
                _basePose = HairSetAdjusterPose.Capture(_target);
                _offset = HairSetOffset.Identity;
            }

            if (!HasTarget || previous != _target.AdjustTarget || previousFace != _face || previousHair != _hair)
            {
                SetPenetration(null);
                _colliderPlan = null;
                _fitResult = null;
                _colliderResult = null;
                _poseVersion++;
            }

            _hasPhysBones = HasTarget && _hair.GetComponentsInChildren<VRCPhysBoneBase>(true).Length > 0;
            _overlay.End();
            if (HasTarget && _overlayEnabled)
            {
                _overlay.Begin(_face, OverlayColors[Mathf.Clamp(_overlayColorIndex, 0, 2)],
                    _target.HeadVertexIndices, _target.HeadCenter, _overlayShowHidden);
            }

            UpdateMigrationConfirmation();
        }

        private void OnFocus()
        {
            ResolveTarget();
            SyncPose();
        }

        private void OnUndoRedo()
        {
            // NOTE: Undo 後の姿勢を逆算しないと、次のスライダー操作で取り消した変更が復活する。
            SyncPose();
            SceneView.RepaintAll();
        }

        private void SyncPose()
        {
            if (HasTarget && _basePose.IsValid)
            {
                HairSetOffset solved = HairSetAdjusterPose.Solve(_target, _basePose);
                bool positionChanged = Mathf.Abs(solved.Position.x - _offset.Position.x) > 1e-5f ||
                    Mathf.Abs(solved.Position.y - _offset.Position.y) > 1e-5f ||
                    Mathf.Abs(solved.Position.z - _offset.Position.z) > 1e-5f;
                bool scaleChanged = Mathf.Abs(solved.Scale - _offset.Scale) > 1e-5f ||
                    Mathf.Abs(solved.AxisScale.x - _offset.AxisScale.x) > 1e-5f ||
                    Mathf.Abs(solved.AxisScale.y - _offset.AxisScale.y) > 1e-5f ||
                    Mathf.Abs(solved.AxisScale.z - _offset.AxisScale.z) > 1e-5f;
                // NOTE: 同じ回転でも Euler 角は別の組を返すため、成分で比べると確認結果が無駄に古くなる。
                bool rotationChanged = Quaternion.Angle(
                    Quaternion.Euler(solved.Angles), Quaternion.Euler(_offset.Angles)) > 0.001f;
                if (positionChanged || scaleChanged || rotationChanged)
                {
                    if (!rotationChanged)
                    {
                        solved.Angles = _offset.Angles;
                    }

                    _offset = solved;
                    _poseVersion++;
                }
            }

            Repaint();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUI.BeginChangeCheck();
            _face = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                FaceContent, _face, typeof(SkinnedMeshRenderer), true);
            _hair = (GameObject)EditorGUILayout.ObjectField(HairContent, _hair, typeof(GameObject), true);
            _adjustTargetOverride = (Transform)EditorGUILayout.ObjectField(
                TargetContent, _adjustTargetOverride, typeof(Transform), true);
            if (EditorGUI.EndChangeCheck())
            {
                ResolveTarget();
            }

            if (!HasTarget)
            {
                EditorGUILayout.HelpBox(
                    _target != null &&
                    _target.BlockedReason != null ? _target.BlockedReason : "対象が失われました。顔メッシュと髪を指定し直してください。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField(_target.AdjustTargetLabel, EditorStyles.wordWrappedLabel);
            }

            EditorGUI.BeginDisabledGroup(!HasTarget);
            DrawOverlay();
            DrawPose();
            DrawCheck();
            DrawColliders();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndScrollView();
            // NOTE: ドラッグ中には重いチェックを行わず、確定した一操作をまとめて Undo する。
            if (_dragUndoGroup >= 0 && GUIUtility.hotControl == 0)
            {
                CommitDrag();
            }
        }

        private void DrawOverlay()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("表示", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle("顔の色を変える", _overlayEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                _overlayEnabled = enabled;
                if (enabled && HasTarget)
                {
                    _overlay.Begin(_face, OverlayColors[_overlayColorIndex],
                        _target.HeadVertexIndices, _target.HeadCenter, _overlayShowHidden);
                }
                else
                {
                    _overlay.End();
                }
            }

            EditorGUI.BeginChangeCheck();
            int index = EditorGUILayout.Popup(ColorContent, _overlayColorIndex, ColorContents);
            if (EditorGUI.EndChangeCheck())
            {
                _overlayColorIndex = index;
                if (_overlay.IsActive)
                {
                    _overlay.SetColor(OverlayColors[index]);
                }
            }

            EditorGUI.BeginDisabledGroup(!_overlayEnabled);
            EditorGUI.BeginChangeCheck();
            _overlayShowHidden = EditorGUILayout.Toggle(ShowHiddenContent, _overlayShowHidden);
            if (EditorGUI.EndChangeCheck())
            {
                _overlay.SetShowHidden(_overlayShowHidden);
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.LabelField(OverlayHelp, EditorStyles.miniLabel);
        }

        private void DrawPose()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("位置合わせ", EditorStyles.boldLabel);
            bool moved = HasTarget && HairSetAdjusterPose.HasRootMoved(_target, _basePose);
            if (moved)
            {
                EditorGUILayout.HelpBox(RootMovedMessage, MessageType.Info);
            }

            EditorGUI.BeginDisabledGroup(moved);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(AutoFitContent, GUILayout.MinWidth(GUI.skin.button.CalcSize(AutoFitContent).x)))
            {
                AutoFit();
            }

            _fitScale = EditorGUILayout.ToggleLeft(FitScaleContent, _fitScale);
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_fitResult))
            {
                EditorGUILayout.HelpBox(_fitResult, MessageType.Info);
            }

            // NOTE: ドラッグ中に窓を動かすと、つまみが指の下から逃げるため、離してから取り直す。
            if (_dragUndoGroup < 0)
            {
                _sliderCenter = _offset;
            }

            _sliderRangeIndex = EditorGUILayout.Popup(SliderRangeContent, _sliderRangeIndex, SliderRangeContents);
            int i = Mathf.Clamp(_sliderRangeIndex, 0, 2);
            HairSetOffset next = _offset;
            EditorGUI.BeginChangeCheck();
            next.Position.x = CenteredSlider(RightContent, next.Position.x, _sliderCenter.Position.x,
                PositionHalfWidths[i], -PositionLimit, PositionLimit);
            next.Position.y = CenteredSlider(UpContent, next.Position.y, _sliderCenter.Position.y,
                PositionHalfWidths[i], -PositionLimit, PositionLimit);
            next.Position.z = CenteredSlider(ForwardContent, next.Position.z, _sliderCenter.Position.z,
                PositionHalfWidths[i], -PositionLimit, PositionLimit);
            next.Angles.x = CenteredSlider(PitchContent, next.Angles.x, _sliderCenter.Angles.x,
                AngleHalfWidths[i], -AngleLimit, AngleLimit);
            next.Angles.y = CenteredSlider(YawContent, next.Angles.y, _sliderCenter.Angles.y,
                AngleHalfWidths[i], -AngleLimit, AngleLimit);
            next.Angles.z = CenteredSlider(RollContent, next.Angles.z, _sliderCenter.Angles.z,
                AngleHalfWidths[i], -AngleLimit, AngleLimit);
            next.Scale = CenteredSlider(ScaleContent, next.Scale, _sliderCenter.Scale,
                ScaleHalfWidths[i], ScaleMin, ScaleMax);
            bool offsetChanged = EditorGUI.EndChangeCheck();
            // NOTE: 折りたたみの開閉は姿勢の編集ではないため、変更検知の外に置く。
            _axisScaleExpanded = EditorGUILayout.Foldout(_axisScaleExpanded, "軸ごとの大きさ（対象のローカル軸）", true);
            if (_axisScaleExpanded)
            {
                EditorGUI.BeginChangeCheck();
                next.AxisScale.x = CenteredSlider(XContent, next.AxisScale.x, _sliderCenter.AxisScale.x,
                    ScaleHalfWidths[i], ScaleMin, ScaleMax);
                next.AxisScale.y = CenteredSlider(YContent, next.AxisScale.y, _sliderCenter.AxisScale.y,
                    ScaleHalfWidths[i], ScaleMin, ScaleMax);
                next.AxisScale.z = CenteredSlider(ZContent, next.AxisScale.z, _sliderCenter.AxisScale.z,
                    ScaleHalfWidths[i], ScaleMin, ScaleMax);
                offsetChanged |= EditorGUI.EndChangeCheck();
            }

            if (offsetChanged && HasTarget && !moved)
            {
                _offset = next;
                if (_dragUndoGroup < 0)
                {
                    _dragUndoGroup = BeginPoseUndo();
                }

                WritePose();
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(CaptureContent, GUILayout.MinWidth(GUI.skin.button.CalcSize(CaptureContent).x)))
            {
                CapturePose();
            }

            EditorGUI.BeginDisabledGroup(moved);
            if (GUILayout.Button(ResetContent, GUILayout.MinWidth(GUI.skin.button.CalcSize(ResetContent).x)))
            {
                ResetPose();
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private static float CenteredSlider(
            GUIContent label, float value, float center, float halfWidth, float min, float max)
        {
            Rect rect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), label);
            Rect sliderRect = rect;
            sliderRect.xMax -= SliderFieldWidth + 4f;
            Rect fieldRect = rect;
            fieldRect.xMin = rect.xMax - SliderFieldWidth;
            EditorGUI.BeginChangeCheck();
            float next = GUI.HorizontalSlider(sliderRect, value, center - halfWidth, center + halfWidth);
            next = EditorGUI.FloatField(fieldRect, next);
            // NOTE: 範囲外の逆算値を、描画しただけで丸めて姿勢を飛ばさない。
            if (!EditorGUI.EndChangeCheck())
            {
                return value;
            }

            return Mathf.Clamp(next, min, max);
        }

        private static int BeginPoseUndo()
        {
            // NOTE: 直前のユーザー操作と混ざらないよう、新しいグループを始める。
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Hair Set Adjuster");
            return Undo.GetCurrentGroup();
        }

        private void WritePose()
        {
            Undo.RecordObject(_target.AdjustTarget, "Hair Set Adjuster");
            HairSetAdjusterPose.Apply(_target, _basePose, _offset);
            _poseVersion++;
            // NOTE: 非アクティブなエディタでもスキンメッシュの見た目を更新する。
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private void ApplyButtonPose()
        {
            CommitDrag();
            int group = BeginPoseUndo();
            try
            {
                WritePose();
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }

            OnPoseCommitted();
        }

        private void AutoFit()
        {
            CommitDrag();
            HairSetFitResult result = HairSetAdjusterFit.Fit(_target, _basePose.RootRotation, _fitScale);
            if (!result.Succeeded)
            {
                _fitResult = result.FailureReason;
                return;
            }

            // NOTE: 拡大を掛ける前の姿勢で移動量を逆写像し、頭の中心から位置がずれるのを防ぐ。
            _offset.Position += HairSetAdjusterPose.WorldDeltaToOffset(
                _basePose, _offset, result.WorldDelta);
            _offset.Scale = Mathf.Clamp(_offset.Scale * result.Scale, ScaleMin, ScaleMax);
            ApplyButtonPose();
            Vector3 delta = Quaternion.Inverse(_basePose.RootRotation) * result.WorldDelta * 100f;
            _fitResult = (result.FromHeadBone
                ? "頭ボーンから合わせました。"
                : "頭ボーンが無いため、髪の形だけで合わせました。後頭部やうなじが出ていないか確かめてください。") +
                "左右 " + delta.x.ToString("+0.0;-0.0;0.0") + " / 上下 " + delta.y.ToString("+0.0;-0.0;0.0") +
                " / 前後 " + delta.z.ToString("+0.0;-0.0;0.0") + " cm";
            if (_fitScale)
            {
                _fitResult += "、大きさ ×" + result.Scale.ToString("0.000");
            }
        }

        private void CapturePose()
        {
            CommitDrag();
            // NOTE: アバターを動かした後は頭の中心も現在の位置から取り直す。
            _target = HairSetAdjusterTarget.Resolve(_face, _hair, _adjustTargetOverride);
            _basePose = HairSetAdjusterPose.Capture(_target);
            _offset = HairSetOffset.Identity;
            _poseVersion++;
            _fitResult = null;
        }

        private void ResetPose()
        {
            _offset = HairSetOffset.Identity;
            ApplyButtonPose();
        }

        private void CommitDrag()
        {
            if (_dragUndoGroup < 0)
            {
                return;
            }

            Undo.CollapseUndoOperations(_dragUndoGroup);
            _dragUndoGroup = -1;
            OnPoseCommitted();
        }

        private void OnPoseCommitted()
        {
            if (HasTarget)
            {
                SetPenetration(HairSetAdjusterPenetration.Check(_target, _poseVersion, true));
            }
        }

        private void DrawCheck()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("めり込みチェック", EditorStyles.boldLabel);
            if (GUILayout.Button("チェック"))
            {
                OnPoseCommitted();
            }

            if (_penetration != null)
            {
                EditorGUILayout.LabelField(_penetration.Label);
                if (_penetration.PoseVersion != _poseVersion)
                {
                    EditorGUILayout.HelpBox(StalePenetrationMessage, MessageType.Info);
                }
            }

            EditorGUI.BeginChangeCheck();
            _showPenetration = EditorGUILayout.Toggle("めり込んだ位置を表示", _showPenetration);
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            EditorGUI.BeginDisabledGroup(!_showPenetration);
            EditorGUI.BeginChangeCheck();
            _penetrationDisplayIndex = EditorGUILayout.Popup(
                PenetrationDisplayContent, _penetrationDisplayIndex, PenetrationDisplayContents);
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            EditorGUI.EndDisabledGroup();
        }

        private void DrawPenetration(SceneView sceneView)
        {
            if (Event.current.type != EventType.Repaint || !_showPenetration || _penetration == null ||
                !HasTarget || _penetrationDisplayIndex == 0)
            {
                return;
            }

            Color previous = Handles.color;
            try
            {
                Handles.color = Color.red;
                foreach (Vector3 p in _penetration.InsidePoints)
                {
                    Handles.DotHandleCap(
                        0,
                        p,
                        Quaternion.identity,
                        HandleUtility.GetHandleSize(p) * 0.012f,
                        EventType.Repaint);
                }
            }
            finally
            {
                Handles.color = previous;
            }
        }

        private void SetPenetration(HairSetPenetrationResult result)
        {
            // NOTE: チェックのたびに古い表示メッシュを捨て、ウィンドウや再生へ資源を持ち越さない。
            if (_penetration != null && _penetration.FaceMesh != null)
            {
                DestroyImmediate(_penetration.FaceMesh);
            }

            _penetration = result;
            SceneView.RepaintAll();
        }

        private void DrawPenetrationFaces(SceneView sceneView)
        {
            if (Event.current.type != EventType.Repaint || !_showPenetration || _penetration == null ||
                _penetration.FaceMesh == null || !HasTarget || _penetrationDisplayIndex == 1)
            {
                return;
            }

            if (_penetrationMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                {
                    Debug.LogWarning("Candy Box: めり込みの表示に使うシェーダーが見つかりませんでした。");
                    return;
                }

                _penetrationMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "CandyBoxPenetrationFaces",
                    renderQueue = 4001
                };
                _penetrationMaterial.SetColor("_Color", new Color(1f, 0.15f, 0.15f, 0.55f));
                // NOTE: 頭の中に埋まった面も見せ、片面の髪を裏から見ても消さない。
                _penetrationMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
                _penetrationMaterial.SetInt("_ZWrite", 0);
                _penetrationMaterial.SetInt("_Cull", (int)CullMode.Off);
                _penetrationMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _penetrationMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            }

            // NOTE: 頂点はチェック時のワールド座標なので、対象の行列を重ねない。
            Graphics.DrawMesh(_penetration.FaceMesh, Matrix4x4.identity, _penetrationMaterial, 0, sceneView.camera, 0);
        }

        private void DrawColliders()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Collider の移行", EditorStyles.boldLabel);
            if (!_hasPhysBones)
            {
                EditorGUILayout.HelpBox("髪に PhysBone がありません。", MessageType.Info);
            }

            EditorGUI.BeginDisabledGroup(!_hasPhysBones);
            EditorGUILayout.LabelField(ColliderHelp, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Collider を確認"))
            {
                CollectColliders();
            }

            bool fresh = _colliderPlan != null && _colliderPlan.PoseVersion == _poseVersion;
            if (_colliderPlan != null)
            {
                if (!fresh)
                {
                    EditorGUILayout.HelpBox(StaleColliderMessage, MessageType.Info);
                }
                else
                {
                    EditorGUILayout.LabelField(_colliderPlan.SummaryLabel);
                    EditorGUI.BeginChangeCheck();
                    foreach (HairSetColliderRow row in _colliderPlan.Rows)
                    {
                        EditorGUI.BeginDisabledGroup(row.AvatarCollider == null || row.HairCollider == null);
                        row.Selected = EditorGUILayout.ToggleLeft(row.Label, row.Selected);
                        EditorGUI.EndDisabledGroup();
                    }

                    if (EditorGUI.EndChangeCheck())
                    {
                        UpdateMigrationConfirmation();
                    }
                }
            }

            _deleteUnusedColliders = EditorGUILayout.ToggleLeft("使われなくなった髪側の Collider を削除する", _deleteUnusedColliders);
            EditorGUI.BeginDisabledGroup(!fresh ||
                _selectedColliderCount == 0 ||
                (HasTarget &&
                HairSetAdjusterPose.HasRootMoved(_target, _basePose)));
            if (GUILayout.Button("移行"))
            {
                MigrateColliders();
            }

            EditorGUI.EndDisabledGroup();
            if (!string.IsNullOrEmpty(_colliderResult))
            {
                EditorGUILayout.HelpBox(_colliderResult, MessageType.Info);
            }

            EditorGUI.EndDisabledGroup();
        }

        private void CollectColliders()
        {
            _colliderPlan = HairSetAdjusterColliders.Collect(_target, _poseVersion);
            _colliderResult = null;
            UpdateMigrationConfirmation();
        }

        private void UpdateMigrationConfirmation()
        {
            _selectedColliderCount = 0;
            if (_colliderPlan != null)
            {
                foreach (HairSetColliderRow row in _colliderPlan.Rows)
                {
                    if (row.Selected && row.HairCollider != null && row.AvatarCollider != null)
                    {
                        _selectedColliderCount++;
                    }
                }
            }

            _migrationConfirmation = _selectedColliderCount + " 組の Collider を移行します。よろしいですか？";
        }

        private void MigrateColliders()
        {
            if (!EditorUtility.DisplayDialog("Candy Box", _migrationConfirmation, "移行", "キャンセル"))
            {
                return;
            }

            _colliderResult = HairSetAdjusterColliders.Apply(_target, _colliderPlan, _deleteUnusedColliders);
            _colliderPlan = null;
            UpdateMigrationConfirmation();
        }
    }
}
