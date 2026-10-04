using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace Poyo.CandyBox.HairSetAdjuster.Editor
{
    internal sealed class HairSetAdjusterWindow : EditorWindow
    {
        private static readonly GUIContent FaceContent = new GUIContent("顔メッシュ");
        private static readonly GUIContent HairContent = new GUIContent("髪");
        private static readonly GUIContent TargetContent = new GUIContent("調整の対象（空欄なら自動）");
        private static readonly GUIContent ColorContent = new GUIContent("色");
        private static readonly GUIContent AlignContent = new GUIContent("頭ボーンに合わせる");
        private static readonly GUIContent SearchContent = new GUIContent("めり込みが少ない位置を探す");
        private static readonly GUIContent CaptureContent = new GUIContent("基準をいまの姿勢にする");
        private static readonly GUIContent ResetContent = new GUIContent("基準に戻す");
        private static readonly GUIContent OverlayHelp = new GUIContent("Scene ビューにだけ表示します。シーンやマテリアルは変更しません。");
        private static readonly GUIContent ColliderHelp = new GUIContent("位置を合わせてから確認してください。位置が合っていないと、同じ役目の Collider も見つかりません。");
        private static readonly GUIContent[] ColorContents = { new GUIContent("マゼンタ"), new GUIContent("シアン"), new GUIContent("黄") };
        private static readonly Color[] OverlayColors = { new Color(1f, 0f, 0.8f, 1f), new Color(0f, 0.9f, 1f, 1f), new Color(1f, 0.9f, 0f, 1f) };
        private const string RootMovedMessage = "アバターが動いたため、「基準をいまの姿勢にする」を押してください。";
        private const string DescendantMessage = "調整の対象を動かしても、髪の頭ボーンは動きません。髪の頭ボーンかその親を対象にしてください。";
        private const string StalePenetrationMessage = "チェックのあとで動かしました。もう一度チェックしてください。";
        private const string StaleColliderMessage = "確認のあとで位置を動かしました。もう一度確認してください。";

        [SerializeField] private SkinnedMeshRenderer _face;
        [SerializeField] private GameObject _hair;
        [SerializeField] private Transform _adjustTargetOverride;
        [SerializeField] private HairSetOffset _offset = HairSetOffset.Identity;
        [SerializeField] private bool _overlayEnabled;
        [SerializeField] private int _overlayColorIndex;
        [SerializeField] private bool _showPenetration = true;
        [SerializeField] private bool _deleteUnusedColliders;
        private HairSetTarget _target;
        private HairSetBasePose _basePose;
        private int _dragUndoGroup = -1;
        private int _poseVersion;
        private Vector2 _scroll;
        private bool _axisScaleExpanded;
        private bool _hasPhysBones;
        private string _alignReason;
        private string _searchResult;
        private string _colliderResult;
        private string _migrationConfirmation;
        private int _selectedColliderCount;
        private HairSetPenetrationResult _penetration;
        private HairSetColliderPlan _colliderPlan;
        private readonly HairSetFaceOverlay _overlay = new HairSetFaceOverlay();
        private bool HasTarget => _target != null && _target.IsValid && _target.Face != null &&
            _target.Hair != null && _target.AdjustTarget != null && _target.HeadBone != null &&
            _target.AvatarRoot != null && !EditorApplication.isPlayingOrWillChangePlaymode;

        internal static void Open()
        {
            var window = GetWindow<HairSetAdjusterWindow>(false, "07_Hair Set Adjuster", true);
            window.minSize = new Vector2(560f, 700f);
            window.Show();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload += EndOverlay;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SceneView.duringSceneGui += DrawPenetration;
            ResolveTarget();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= EndOverlay;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneView.duringSceneGui -= DrawPenetration;
            EndOverlay();
            CommitDrag();
        }

        private void EndOverlay() => _overlay.End();

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) { EndOverlay(); CommitDrag(); }
            if (state == PlayModeStateChange.EnteredEditMode) ResolveTarget();
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
                _penetration = null;
                _colliderPlan = null;
                _searchResult = null;
                _colliderResult = null;
                _poseVersion++;
            }
            _hasPhysBones = HasTarget && _hair.GetComponentsInChildren<VRCPhysBoneBase>(true).Length > 0;
            _alignReason = null;
            if (HasTarget)
            {
                if (_target.HairHeadBone == null)
                    _alignReason = "髪の中に、頭ボーン（" + _target.HeadBone.name + "）と同じ名前のボーンがありません。";
                else if (!_target.HairHeadBone.IsChildOf(_target.AdjustTarget)) _alignReason = DescendantMessage;
            }
            _overlay.End();
            if (HasTarget && _overlayEnabled) _overlay.Begin(_face, OverlayColors[Mathf.Clamp(_overlayColorIndex, 0, 2)]);
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
                _offset = HairSetAdjusterPose.Solve(_target, _basePose);
                _poseVersion++;
            }
            Repaint();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUI.BeginChangeCheck();
            _face = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(FaceContent, _face, typeof(SkinnedMeshRenderer), true);
            _hair = (GameObject)EditorGUILayout.ObjectField(HairContent, _hair, typeof(GameObject), true);
            _adjustTargetOverride = (Transform)EditorGUILayout.ObjectField(TargetContent, _adjustTargetOverride, typeof(Transform), true);
            if (EditorGUI.EndChangeCheck()) ResolveTarget();
            if (!HasTarget)
                EditorGUILayout.HelpBox(_target != null && _target.BlockedReason != null ? _target.BlockedReason :
                    "対象が失われました。顔メッシュと髪を指定し直してください。", MessageType.Warning);
            else EditorGUILayout.LabelField(_target.AdjustTargetLabel, EditorStyles.wordWrappedLabel);
            EditorGUI.BeginDisabledGroup(!HasTarget);
            DrawOverlay();
            DrawPose();
            DrawCheck();
            DrawColliders();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndScrollView();
            // NOTE: ドラッグ中には重いチェックを行わず、確定した一操作をまとめて Undo する。
            if (_dragUndoGroup >= 0 && GUIUtility.hotControl == 0) CommitDrag();
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
                if (enabled && HasTarget) _overlay.Begin(_face, OverlayColors[_overlayColorIndex]);
                else _overlay.End();
            }
            EditorGUI.BeginChangeCheck();
            int index = EditorGUILayout.Popup(ColorContent, _overlayColorIndex, ColorContents);
            if (EditorGUI.EndChangeCheck())
            {
                _overlayColorIndex = index;
                if (_overlay.IsActive) _overlay.SetColor(OverlayColors[index]);
            }
            EditorGUILayout.LabelField(OverlayHelp, EditorStyles.miniLabel);
        }

        private void DrawPose()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("位置合わせ", EditorStyles.boldLabel);
            bool moved = HasTarget && HairSetAdjusterPose.HasRootMoved(_target, _basePose);
            if (moved) EditorGUILayout.HelpBox(RootMovedMessage, MessageType.Info);
            EditorGUI.BeginDisabledGroup(moved);
            HairSetOffset next = _offset;
            EditorGUI.BeginChangeCheck();
            next.Position.x = EditorGUILayout.Slider("左右", next.Position.x, -0.15f, 0.15f);
            next.Position.y = EditorGUILayout.Slider("上下", next.Position.y, -0.15f, 0.15f);
            next.Position.z = EditorGUILayout.Slider("前後", next.Position.z, -0.15f, 0.15f);
            next.Angles.x = EditorGUILayout.Slider("前後の傾き", next.Angles.x, -30f, 30f);
            next.Angles.y = EditorGUILayout.Slider("左右の向き", next.Angles.y, -30f, 30f);
            next.Angles.z = EditorGUILayout.Slider("左右の傾き", next.Angles.z, -30f, 30f);
            next.Scale = EditorGUILayout.Slider("大きさ", next.Scale, 0.8f, 1.2f);
            bool offsetChanged = EditorGUI.EndChangeCheck();
            // NOTE: 折りたたみの開閉は姿勢の編集ではないため、変更検知の外に置く。
            _axisScaleExpanded = EditorGUILayout.Foldout(_axisScaleExpanded, "軸ごとの大きさ（対象のローカル軸）", true);
            if (_axisScaleExpanded)
            {
                EditorGUI.BeginChangeCheck();
                next.AxisScale.x = EditorGUILayout.Slider("X", next.AxisScale.x, 0.8f, 1.2f);
                next.AxisScale.y = EditorGUILayout.Slider("Y", next.AxisScale.y, 0.8f, 1.2f);
                next.AxisScale.z = EditorGUILayout.Slider("Z", next.AxisScale.z, 0.8f, 1.2f);
                offsetChanged |= EditorGUI.EndChangeCheck();
            }
            // NOTE: 範囲外の逆算値を、描画しただけで丸めて姿勢を飛ばさない。
            if (offsetChanged && HasTarget && !moved)
            {
                _offset = next;
                if (_dragUndoGroup < 0) _dragUndoGroup = BeginPoseUndo();
                WritePose();
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_alignReason != null);
            if (GUILayout.Button(AlignContent, GUILayout.MinWidth(GUI.skin.button.CalcSize(AlignContent).x))) AlignHead();
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button(SearchContent, GUILayout.MinWidth(GUI.skin.button.CalcSize(SearchContent).x))) SearchPosition();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(CaptureContent, GUILayout.MinWidth(GUI.skin.button.CalcSize(CaptureContent).x))) CapturePose();
            EditorGUI.BeginDisabledGroup(moved);
            if (GUILayout.Button(ResetContent, GUILayout.MinWidth(GUI.skin.button.CalcSize(ResetContent).x))) ResetPose();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (_alignReason != null) EditorGUILayout.HelpBox(_alignReason, MessageType.Info);
            if (!string.IsNullOrEmpty(_searchResult)) EditorGUILayout.HelpBox(_searchResult, MessageType.Info);
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
            try { WritePose(); }
            finally { Undo.CollapseUndoOperations(group); }
            OnPoseCommitted();
        }

        private void AlignHead()
        {
            _offset.Position += Quaternion.Inverse(_basePose.RootRotation) *
                (_target.HeadBone.position - _target.HairHeadBone.position);
            ApplyButtonPose();
        }

        private void CapturePose()
        {
            CommitDrag();
            // NOTE: アバターを動かした後は頭の中心も現在の位置から取り直す。
            _target = HairSetAdjusterTarget.Resolve(_face, _hair, _adjustTargetOverride);
            _basePose = HairSetAdjusterPose.Capture(_target);
            _offset = HairSetOffset.Identity;
            _poseVersion++;
            _searchResult = null;
        }

        private void ResetPose()
        {
            _offset = HairSetOffset.Identity;
            ApplyButtonPose();
        }

        private void CommitDrag()
        {
            if (_dragUndoGroup < 0) return;
            Undo.CollapseUndoOperations(_dragUndoGroup);
            _dragUndoGroup = -1;
            OnPoseCommitted();
        }

        private void OnPoseCommitted()
        {
            if (HasTarget) _penetration = HairSetAdjusterPenetration.Check(_target, _poseVersion);
        }

        private void SearchPosition()
        {
            int before = HairSetAdjusterPenetration.Check(_target, _poseVersion).Inside;
            Vector3 old = _offset.Position;
            _offset.Position = HairSetAdjusterPenetration.Search(_target, _basePose, _offset);
            ApplyButtonPose();
            Vector3 delta = (_offset.Position - old) * 100f;
            _searchResult = "探索前 " + before + " 頂点 → 探索後 " + _penetration.Inside +
                " 頂点（上下 " + delta.y.ToString("+0.0;-0.0;0.0") + " cm / 前後 " +
                delta.z.ToString("+0.0;-0.0;0.0") + " cm 動かしました）";
        }

        private void DrawCheck()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("めり込みチェック", EditorStyles.boldLabel);
            if (GUILayout.Button("チェック")) OnPoseCommitted();
            if (_penetration != null)
            {
                EditorGUILayout.LabelField(_penetration.Label);
                if (_penetration.PoseVersion != _poseVersion) EditorGUILayout.HelpBox(StalePenetrationMessage, MessageType.Info);
            }
            EditorGUI.BeginChangeCheck();
            _showPenetration = EditorGUILayout.Toggle("めり込んだ位置を表示", _showPenetration);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
        }

        private void DrawPenetration(SceneView sceneView)
        {
            if (Event.current.type != EventType.Repaint || !_showPenetration || _penetration == null || !HasTarget) return;
            Color previous = Handles.color;
            try
            {
                Handles.color = Color.red;
                foreach (Vector3 p in _penetration.InsidePoints)
                    Handles.DotHandleCap(0, p, Quaternion.identity, HandleUtility.GetHandleSize(p) * 0.012f, EventType.Repaint);
            }
            finally { Handles.color = previous; }
        }

        private void DrawColliders()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Collider の移行", EditorStyles.boldLabel);
            if (!_hasPhysBones) EditorGUILayout.HelpBox("髪に PhysBone がありません。", MessageType.Info);
            EditorGUI.BeginDisabledGroup(!_hasPhysBones);
            EditorGUILayout.LabelField(ColliderHelp, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Collider を確認")) CollectColliders();
            bool fresh = _colliderPlan != null && _colliderPlan.PoseVersion == _poseVersion;
            if (_colliderPlan != null)
            {
                if (!fresh) EditorGUILayout.HelpBox(StaleColliderMessage, MessageType.Info);
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
                    if (EditorGUI.EndChangeCheck()) UpdateMigrationConfirmation();
                }
            }
            _deleteUnusedColliders = EditorGUILayout.ToggleLeft("使われなくなった髪側の Collider を削除する", _deleteUnusedColliders);
            EditorGUI.BeginDisabledGroup(!fresh || _selectedColliderCount == 0 ||
                (HasTarget && HairSetAdjusterPose.HasRootMoved(_target, _basePose)));
            if (GUILayout.Button("移行")) MigrateColliders();
            EditorGUI.EndDisabledGroup();
            if (!string.IsNullOrEmpty(_colliderResult)) EditorGUILayout.HelpBox(_colliderResult, MessageType.Info);
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
                foreach (HairSetColliderRow row in _colliderPlan.Rows)
                    if (row.Selected && row.HairCollider != null && row.AvatarCollider != null) _selectedColliderCount++;
            _migrationConfirmation = _selectedColliderCount + " 組の Collider を移行します。よろしいですか？";
        }

        private void MigrateColliders()
        {
            if (!EditorUtility.DisplayDialog("Candy Box", _migrationConfirmation, "移行", "キャンセル")) return;
            _colliderResult = HairSetAdjusterColliders.Apply(_target, _colliderPlan, _deleteUnusedColliders);
            _colliderPlan = null;
            UpdateMigrationConfirmation();
        }
    }
}
