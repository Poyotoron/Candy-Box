using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BoneWeightCollapser.Editor
{
    internal sealed class BoneWeightCollapserWindow : EditorWindow
    {
        private static readonly GUIContent TitleContent =
            new GUIContent("05_Bone Weight Collapser");
        private static readonly GUIContent TargetHeaderContent = new GUIContent("対象");
        private static readonly GUIContent RootContent = new GUIContent("対象オブジェクト");
        private static readonly GUIContent CollectContent = new GUIContent("メッシュを収集");
        private static readonly GUIContent SelectAllContent = new GUIContent("すべて選択");
        private static readonly GUIContent ClearAllContent = new GUIContent("すべて解除");
        private static readonly GUIContent SourceHeaderContent = new GUIContent("移動元ボーン");
        private static readonly GUIContent SourceModeContent = new GUIContent("移動元の指定");
        private static readonly GUIContent ExplicitBonesContent = new GUIContent("移動元ボーン");
        private static readonly GUIContent DescendantsRootContent = new GUIContent("起点ボーン");
        private static readonly GUIContent IncludeRootContent =
            new GUIContent("起点自身も移動元にする");
        private static readonly GUIContent RemoveContent = new GUIContent("×");
        private static readonly GUIContent AddContent = new GUIContent("追加");
        private static readonly GUIContent DestinationHeaderContent =
            new GUIContent("移動先ボーン");
        private static readonly GUIContent DestinationContent = new GUIContent("移動先ボーン");
        private static readonly GUIContent OptionsHeaderContent = new GUIContent("オプション");
        private static readonly GUIContent BlendRatioContent = new GUIContent("移動比率");
        private static readonly GUIContent NormalizeContent =
            new GUIContent("ウェイトを正規化する");
        private static readonly GUIContent OutputHeaderContent = new GUIContent("出力");
        private static readonly GUIContent OutputFolderContent = new GUIContent("出力フォルダ");
        private static readonly GUIContent OutputPathContent = new GUIContent("パス");
        private static readonly GUIContent SelectFolderContent = new GUIContent("選択");
        private static readonly GUIContent SuffixContent = new GUIContent("サフィックス");
        private static readonly GUIContent ScanContent = new GUIContent("影響を確認");
        private static readonly GUIContent ApplyContent = new GUIContent("適用");
        private static readonly GUIContent RevertContent = new GUIContent("元に戻す");
        private static readonly GUIContent BreakdownContent = new GUIContent("移動元の内訳");
        private static readonly GUIContent ScanSummaryContent = new GUIContent("走査結果");
        private static readonly GUIContent AffectedVerticesContent = new GUIContent("影響頂点数");
        private static readonly GUIContent MovedWeightContent = new GUIContent("移動ウェイト");
        private static readonly GUIContent ResultHeaderContent = new GUIContent("結果");
        private static readonly GUIContent AppliedCountContent = new GUIContent("適用した対象");
        private static readonly GUIContent CreatedCountContent = new GUIContent("生成アセット");
        private static readonly GUIContent SkippedCountContent = new GUIContent("スキップ");

        private const string IntroMessage =
            "新しいメッシュを出力し、対象のメッシュ参照を差し替えます。元のメッシュは変更しません。";
        private const string ChangedInputMessage =
            "入力が変わりました。もう一度『影響を確認』を押してください。";
        private const string MissingRootMessage = "対象オブジェクトを指定してください。";
        private const string PlayingMessage = "再生中は実行できません。再生を停止してください。";
        private const string MissingScanMessage = "先に『影響を確認』を実行してください。";
        private const string MissingSelectionMessage =
            "処理できる対象を 1 件以上選択してください。";
        private const string NoAffectedVertexMessage = "移動するウェイトがありません。";
        private const string MissingOutputFolderMessage = "出力フォルダを指定してください。";
        private const string InvalidOutputFolderMessage =
            "出力フォルダが見つかりません。Project 内のフォルダを指定してください。";
        private const string MissingSuffixMessage = "サフィックスを入力してください。";
        private const string NotAFolderMessage = "フォルダを指定してください。";
        private const string OutsideProjectMessage =
            "Project の Assets 内にあるフォルダを指定してください。";
        private const string EmptyOutputPathLabel = "（未指定）";
        private const string CreatedAssetNotice =
            "生成したメッシュは「元に戻す」でも Undo でも削除されません。不要になったら手動で削除してください。";
        private const string RevertResultFormat = "{0} 件のメッシュ参照を元に戻しました。";

        [SerializeField] private GameObject _root;
        [SerializeField] private BoneWeightSourceMode _sourceMode =
            BoneWeightSourceMode.Explicit;
        [SerializeField] private List<Transform> _explicitBones = new List<Transform>();
        [SerializeField] private Transform _descendantsRoot;
        [SerializeField] private bool _includeDescendantsRoot;
        [SerializeField] private Transform _destination;
        [SerializeField] private float _blendRatio = 1f;
        [SerializeField] private bool _normalize = true;
        [SerializeField] private DefaultAsset _outputFolderAsset;
        [SerializeField] private string _outputFolderPath = string.Empty;
        [SerializeField] private string _suffix = "_Collapsed";
        [SerializeField] private Vector2 _pageScroll;
        [SerializeField] private Vector2 _targetScroll;

        private BoneWeightCollapserPlan _plan;
        private BoneWeightCollapseResult _result;
        private bool _hasScanned;
        private bool _scanValid;
        private string _outputFolderError = string.Empty;
        private string _statusMessage = string.Empty;
        private float _smallButtonWidth;
        private float _normalButtonWidth;
        private float _selectAllButtonWidth;
        private float _clearAllButtonWidth;

        internal static void Open()
        {
            BoneWeightCollapserWindow window =
                GetWindow<BoneWeightCollapserWindow>(TitleContent.text);
            window.titleContent = TitleContent;
            window.minSize = new Vector2(660f, 700f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = TitleContent;
            minSize = new Vector2(660f, 700f);
            if (_explicitBones == null)
            {
                _explicitBones = new List<Transform>();
            }

            if (_explicitBones.Count == 0)
            {
                _explicitBones.Add(null);
            }

            if (_outputFolderAsset == null &&
                AssetDatabase.IsValidFolder(_outputFolderPath))
            {
                _outputFolderAsset =
                    AssetDatabase.LoadAssetAtPath<DefaultAsset>(_outputFolderPath);
            }

            // NOTE: 表示言語やエディタースタイルで必要幅が変わるため、
            // ボタン幅は固定値にせず実際の表示内容から求める。
            _smallButtonWidth = Mathf.Max(
                EditorStyles.miniButton.CalcSize(RemoveContent).x,
                EditorStyles.miniButton.CalcSize(AddContent).x) + 12f;
            _normalButtonWidth = Mathf.Max(
                EditorStyles.miniButton.CalcSize(SelectFolderContent).x,
                EditorStyles.miniButton.CalcSize(CollectContent).x) + 12f;
            _selectAllButtonWidth =
                EditorStyles.miniButton.CalcSize(SelectAllContent).x + 12f;
            _clearAllButtonWidth =
                EditorStyles.miniButton.CalcSize(ClearAllContent).x + 12f;
        }

        private void OnGUI()
        {
            _pageScroll = EditorGUILayout.BeginScrollView(_pageScroll);
            EditorGUILayout.LabelField(TitleContent, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(IntroMessage, MessageType.Info);

            DrawTargets();
            DrawSources();
            DrawDestination();
            DrawOptions();
            DrawOutput();

            if (_hasScanned && !_scanValid)
            {
                EditorGUILayout.HelpBox(ChangedInputMessage, MessageType.Warning);
            }

            DrawActions();
            DrawScanSummary();
            DrawResult();
            EditorGUILayout.EndScrollView();
        }

        private void DrawTargets()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(TargetHeaderContent, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            GameObject nextRoot = EditorGUILayout.ObjectField(
                RootContent, _root, typeof(GameObject), true) as GameObject;
            EditorGUI.BeginDisabledGroup(_root == null);
            bool collectPressed = GUILayout.Button(
                CollectContent, GUILayout.Width(_normalButtonWidth));
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (nextRoot != _root)
            {
                _root = nextRoot;
                InvalidateScan();
            }

            if (collectPressed)
            {
                CollectMeshes();
            }

            if (_plan == null || _plan.Targets.Count == 0)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    SelectAllContent,
                    GUILayout.Width(_selectAllButtonWidth)))
            {
                SetAllSelections(true);
            }

            if (GUILayout.Button(
                    ClearAllContent,
                    GUILayout.Width(_clearAllButtonWidth)))
            {
                SetAllSelections(false);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            _targetScroll = EditorGUILayout.BeginScrollView(
                _targetScroll, GUILayout.Height(180f));
            for (int targetIndex = 0; targetIndex < _plan.Targets.Count; targetIndex++)
            {
                DrawTarget(_plan.Targets[targetIndex]);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTarget(BoneWeightCollapseTarget target)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Rect rowRect = EditorGUILayout.GetControlRect(
                false, EditorGUIUtility.singleLineHeight);
            Rect toggleRect = rowRect;
            toggleRect.width = EditorGUIUtility.singleLineHeight;
            Rect labelRect = rowRect;
            labelRect.xMin = toggleRect.xMax + 2f;

            bool selectable = !_hasScanned ||
                target.BlockReason == BoneWeightBlockReason.None;
            EditorGUI.BeginDisabledGroup(!selectable);
            bool nextSelected = EditorGUI.Toggle(toggleRect, target.IsSelected);
            EditorGUI.EndDisabledGroup();
            if (nextSelected != target.IsSelected && selectable)
            {
                target.IsSelected = nextSelected;
                RefreshTotals();
            }

            // NOTE: LabelField はラベル欄の幅で行を切るため、一覧の横幅全体へ描画する。
            GUI.Label(labelRect, target.RowContent, EditorStyles.label);
            EditorGUIUtility.AddCursorRect(labelRect, MouseCursor.Link);
            // NOTE: 選択リンクをチェック欄から分け、行クリックがチェック操作を奪わないようにする。
            if (GUI.Button(labelRect, GUIContent.none, GUIStyle.none) &&
                target.Renderer != null)
            {
                Selection.activeObject = target.Renderer.gameObject;
                EditorGUIUtility.PingObject(target.Renderer.gameObject);
            }

            if (_hasScanned && target.BlockReason != BoneWeightBlockReason.None)
            {
                EditorGUILayout.HelpBox(target.BlockedLabel, MessageType.Warning);
            }

            if (_scanValid && target.SourceBones.Count > 0)
            {
                target.DetailsExpanded = EditorGUILayout.Foldout(
                    target.DetailsExpanded, BreakdownContent, true);
                if (target.DetailsExpanded)
                {
                    EditorGUI.indentLevel++;
                    for (int sourceIndex = 0;
                         sourceIndex < target.SourceBones.Count;
                         sourceIndex++)
                    {
                        GUILayout.Label(
                            target.SourceBones[sourceIndex].RowLabel,
                            EditorStyles.wordWrappedLabel);
                    }

                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSources()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(SourceHeaderContent, EditorStyles.boldLabel);
            BoneWeightSourceMode nextMode = (BoneWeightSourceMode)
                EditorGUILayout.EnumPopup(SourceModeContent, _sourceMode);
            if (nextMode != _sourceMode)
            {
                _sourceMode = nextMode;
                InvalidateScan();
            }

            if (_sourceMode == BoneWeightSourceMode.Descendants)
            {
                Transform nextRoot = EditorGUILayout.ObjectField(
                    DescendantsRootContent,
                    _descendantsRoot,
                    typeof(Transform),
                    true) as Transform;
                bool nextIncludeRoot = EditorGUILayout.ToggleLeft(
                    IncludeRootContent, _includeDescendantsRoot);
                if (nextRoot != _descendantsRoot ||
                    nextIncludeRoot != _includeDescendantsRoot)
                {
                    _descendantsRoot = nextRoot;
                    _includeDescendantsRoot = nextIncludeRoot;
                    InvalidateScan();
                }

                return;
            }

            EditorGUILayout.LabelField(ExplicitBonesContent, EditorStyles.miniBoldLabel);
            int removeIndex = -1;
            for (int boneIndex = 0; boneIndex < _explicitBones.Count; boneIndex++)
            {
                EditorGUILayout.BeginHorizontal();
                Transform currentBone = _explicitBones[boneIndex];
                Transform nextBone = EditorGUILayout.ObjectField(
                    currentBone, typeof(Transform), true) as Transform;
                if (nextBone != currentBone)
                {
                    _explicitBones[boneIndex] = nextBone;
                    InvalidateScan();
                }

                if (GUILayout.Button(
                        RemoveContent, GUILayout.Width(_smallButtonWidth)))
                {
                    removeIndex = boneIndex;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0)
            {
                _explicitBones.RemoveAt(removeIndex);
                InvalidateScan();
            }

            if (GUILayout.Button(AddContent, GUILayout.Width(_smallButtonWidth)))
            {
                _explicitBones.Add(null);
                InvalidateScan();
            }
        }

        private void DrawDestination()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(DestinationHeaderContent, EditorStyles.boldLabel);
            Transform nextDestination = EditorGUILayout.ObjectField(
                DestinationContent, _destination, typeof(Transform), true) as Transform;
            if (nextDestination != _destination)
            {
                _destination = nextDestination;
                InvalidateScan();
            }
        }

        private void DrawOptions()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(OptionsHeaderContent, EditorStyles.boldLabel);
            float nextBlendRatio = EditorGUILayout.Slider(
                BlendRatioContent, _blendRatio, 0f, 1f);
            bool nextNormalize = EditorGUILayout.ToggleLeft(
                NormalizeContent, _normalize);
            if (!Mathf.Approximately(nextBlendRatio, _blendRatio) ||
                nextNormalize != _normalize)
            {
                _blendRatio = nextBlendRatio;
                _normalize = nextNormalize;
                InvalidateScan();
            }
        }

        private void DrawOutput()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(OutputHeaderContent, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            DefaultAsset nextFolder = EditorGUILayout.ObjectField(
                OutputFolderContent,
                _outputFolderAsset,
                typeof(DefaultAsset),
                false) as DefaultAsset;
            Rect folderRect = GUILayoutUtility.GetLastRect();
            bool selectPressed = GUILayout.Button(
                SelectFolderContent, GUILayout.Width(_normalButtonWidth));
            EditorGUILayout.EndHorizontal();
            if (nextFolder != _outputFolderAsset)
            {
                SetOutputFolderAsset(nextFolder);
            }

            HandleOutputFolderDrop(folderRect);
            if (selectPressed)
            {
                OpenOutputFolder();
            }

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(
                OutputPathContent,
                string.IsNullOrEmpty(_outputFolderPath)
                    ? EmptyOutputPathLabel
                    : _outputFolderPath);
            EditorGUI.EndDisabledGroup();
            if (!string.IsNullOrEmpty(_outputFolderError))
            {
                EditorGUILayout.HelpBox(_outputFolderError, MessageType.Warning);
            }

            string nextSuffix = EditorGUILayout.TextField(SuffixContent, _suffix);
            if (!string.Equals(nextSuffix, _suffix, StringComparison.Ordinal))
            {
                _suffix = nextSuffix;
            }
        }

        private void DrawActions()
        {
            string applyBlockedReason = GetApplyBlockedReason();
            if (!string.IsNullOrEmpty(applyBlockedReason))
            {
                EditorGUILayout.HelpBox(applyBlockedReason, MessageType.Warning);
            }

            bool canRevert = HasRevertableTarget();
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_root == null);
            bool scanPressed = GUILayout.Button(ScanContent);
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(applyBlockedReason));
            bool applyPressed = GUILayout.Button(ApplyContent);
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!canRevert);
            bool revertPressed = GUILayout.Button(RevertContent);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (scanPressed)
            {
                RunScan();
            }

            if (applyPressed)
            {
                ApplyPlan();
            }

            if (revertPressed)
            {
                RevertPlan();
            }
        }

        private void DrawScanSummary()
        {
            if (!_scanValid || _plan == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(ScanSummaryContent, EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.IntField(
                AffectedVerticesContent, _plan.TotalAffectedVertexCount);
            EditorGUILayout.FloatField(MovedWeightContent, _plan.TotalMovedWeight);
            EditorGUI.EndDisabledGroup();
            for (int warningIndex = 0;
                 warningIndex < _plan.Warnings.Count;
                 warningIndex++)
            {
                EditorGUILayout.HelpBox(
                    _plan.Warnings[warningIndex], MessageType.Warning);
            }
        }

        private void DrawResult()
        {
            if (_result == null && string.IsNullOrEmpty(_statusMessage))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(ResultHeaderContent, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }

            if (_result == null)
            {
                return;
            }

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.IntField(AppliedCountContent, _result.AppliedCount);
            EditorGUILayout.IntField(CreatedCountContent, _result.CreatedAssetCount);
            EditorGUILayout.IntField(SkippedCountContent, _result.SkippedCount);
            EditorGUI.EndDisabledGroup();
            for (int lineIndex = 0; lineIndex < _result.Lines.Count; lineIndex++)
            {
                GUILayout.Label(_result.Lines[lineIndex], EditorStyles.wordWrappedLabel);
            }

            for (int errorIndex = 0; errorIndex < _result.Errors.Count; errorIndex++)
            {
                EditorGUILayout.HelpBox(_result.Errors[errorIndex], MessageType.Error);
            }

            if (_result.CreatedAssetCount > 0)
            {
                EditorGUILayout.HelpBox(CreatedAssetNotice, MessageType.Info);
            }
        }

        private void CollectMeshes()
        {
            var nextPlan = new BoneWeightCollapserPlan
            {
                Root = _root,
                Targets = BoneWeightCollapserScanner.CollectTargets(_root),
            };
            CarrySelections(_plan, nextPlan);
            _plan = nextPlan;
            _hasScanned = false;
            _scanValid = false;
            _result = null;
            _statusMessage = string.Empty;
            Repaint();
        }

        private void RunScan()
        {
            if (_root == null)
            {
                return;
            }

            if (_plan == null || _plan.Root != _root)
            {
                var nextPlan = new BoneWeightCollapserPlan
                {
                    Root = _root,
                    Targets = BoneWeightCollapserScanner.CollectTargets(_root),
                };
                CarrySelections(_plan, nextPlan);
                _plan = nextPlan;
            }

            _plan.Warnings.Clear();
            _plan.Destination = _destination;
            _plan.SourceBones = BoneWeightCollapserScanner.ResolveSourceBones(
                _sourceMode,
                _explicitBones,
                _descendantsRoot,
                _includeDescendantsRoot,
                _destination,
                _plan.Warnings);
            _plan.BlendRatio = Mathf.Clamp01(_blendRatio);
            _plan.Normalize = _normalize;
            BoneWeightCollapserScanner.Scan(_plan);
            _hasScanned = true;
            _scanValid = true;
            _result = null;
            _statusMessage = string.Empty;
            Repaint();
        }

        private void ApplyPlan()
        {
            _result = BoneWeightCollapserApplier.Apply(
                _plan, _outputFolderPath, _suffix);
            _statusMessage = string.Empty;
            BoneWeightCollapserScanner.Scan(_plan);
            _hasScanned = true;
            _scanValid = true;
            Repaint();
        }

        private void RevertPlan()
        {
            int reverted = BoneWeightCollapserApplier.Revert(_plan);
            _result = null;
            _statusMessage = string.Format(RevertResultFormat, reverted);
            BoneWeightCollapserScanner.Scan(_plan);
            _hasScanned = true;
            _scanValid = true;
            Repaint();
        }

        private void SetAllSelections(bool selected)
        {
            for (int targetIndex = 0; targetIndex < _plan.Targets.Count; targetIndex++)
            {
                BoneWeightCollapseTarget target = _plan.Targets[targetIndex];
                if (!_hasScanned ||
                    target.BlockReason == BoneWeightBlockReason.None)
                {
                    target.IsSelected = selected;
                }
            }

            RefreshTotals();
        }

        private void RefreshTotals()
        {
            if (_plan == null)
            {
                return;
            }

            _plan.TotalAffectedVertexCount = 0;
            _plan.TotalMovedWeight = 0f;
            for (int targetIndex = 0; targetIndex < _plan.Targets.Count; targetIndex++)
            {
                BoneWeightCollapseTarget target = _plan.Targets[targetIndex];
                if (!target.IsSelected ||
                    target.BlockReason != BoneWeightBlockReason.None)
                {
                    continue;
                }

                _plan.TotalAffectedVertexCount += target.AffectedVertexCount;
                _plan.TotalMovedWeight += target.MovedWeightTotal;
            }
        }

        private string GetApplyBlockedReason()
        {
            if (EditorApplication.isPlaying)
            {
                return PlayingMessage;
            }

            if (_root == null)
            {
                return MissingRootMessage;
            }

            if (!_scanValid || _plan == null)
            {
                return MissingScanMessage;
            }

            if (_plan.BlendRatio <= 0f)
            {
                return NoAffectedVertexMessage;
            }

            bool hasSelectedTarget = false;
            bool hasSelectedNoAffectedTarget = false;
            for (int targetIndex = 0; targetIndex < _plan.Targets.Count; targetIndex++)
            {
                BoneWeightCollapseTarget target = _plan.Targets[targetIndex];
                if (target.IsSelected &&
                    target.BlockReason == BoneWeightBlockReason.None)
                {
                    hasSelectedTarget = true;
                    break;
                }

                if (target.IsSelected &&
                    target.BlockReason == BoneWeightBlockReason.NoAffectedVertex)
                {
                    hasSelectedNoAffectedTarget = true;
                }
            }

            if (!hasSelectedTarget)
            {
                return hasSelectedNoAffectedTarget
                    ? NoAffectedVertexMessage
                    : MissingSelectionMessage;
            }

            if (_plan.TotalAffectedVertexCount <= 0)
            {
                return NoAffectedVertexMessage;
            }

            if (string.IsNullOrEmpty(_outputFolderPath))
            {
                return MissingOutputFolderMessage;
            }

            if (!AssetDatabase.IsValidFolder(_outputFolderPath))
            {
                return InvalidOutputFolderMessage;
            }

            if (string.IsNullOrEmpty(_suffix))
            {
                return MissingSuffixMessage;
            }

            return null;
        }

        private bool HasRevertableTarget()
        {
            if (_plan == null)
            {
                return false;
            }

            for (int targetIndex = 0; targetIndex < _plan.Targets.Count; targetIndex++)
            {
                BoneWeightCollapseTarget target = _plan.Targets[targetIndex];
                if (target.Renderer != null && target.PreviousMesh != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void InvalidateScan()
        {
            if (_hasScanned)
            {
                _scanValid = false;
            }
        }

        private static void CarrySelections(
            BoneWeightCollapserPlan previous, BoneWeightCollapserPlan next)
        {
            if (previous == null || next == null)
            {
                return;
            }

            for (int nextIndex = 0; nextIndex < next.Targets.Count; nextIndex++)
            {
                BoneWeightCollapseTarget nextTarget = next.Targets[nextIndex];
                for (int previousIndex = 0;
                     previousIndex < previous.Targets.Count;
                     previousIndex++)
                {
                    BoneWeightCollapseTarget previousTarget =
                        previous.Targets[previousIndex];
                    if (nextTarget.Renderer == previousTarget.Renderer)
                    {
                        nextTarget.IsSelected = previousTarget.IsSelected;
                        nextTarget.PreviousMesh = previousTarget.PreviousMesh;
                        nextTarget.OutputPath = previousTarget.OutputPath;
                        nextTarget.ResultLabel = previousTarget.ResultLabel;
                        break;
                    }
                }
            }
        }

        private void OpenOutputFolder()
        {
            string selectedPath = EditorUtility.OpenFolderPanel(
                "出力先のフォルダを選択", "Assets", string.Empty);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                if (TryConvertToAssetPath(selectedPath, out string assetPath))
                {
                    SetOutputFolderAsset(
                        AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath));
                }
                else
                {
                    _outputFolderAsset = null;
                    _outputFolderPath = string.Empty;
                    _outputFolderError = OutsideProjectMessage;
                }
            }

            GUIUtility.ExitGUI();
        }

        private void HandleOutputFolderDrop(Rect dropArea)
        {
            Event currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition) ||
                (currentEvent.type != EventType.DragUpdated &&
                 currentEvent.type != EventType.DragPerform))
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                UnityEngine.Object[] references = DragAndDrop.objectReferences;
                string assetPath = references.Length > 0
                    ? NormalizePath(AssetDatabase.GetAssetPath(references[0]))
                    : string.Empty;
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    SetOutputFolderAsset(
                        AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath));
                }
                else
                {
                    _outputFolderAsset = null;
                    _outputFolderPath = string.Empty;
                    _outputFolderError = NotAFolderMessage;
                }
            }

            currentEvent.Use();
        }

        private void SetOutputFolderAsset(DefaultAsset folderAsset)
        {
            if (folderAsset == null)
            {
                _outputFolderAsset = null;
                _outputFolderPath = string.Empty;
                _outputFolderError = string.Empty;
                return;
            }

            string assetPath = NormalizePath(AssetDatabase.GetAssetPath(folderAsset));
            if (!AssetDatabase.IsValidFolder(assetPath))
            {
                _outputFolderAsset = null;
                _outputFolderPath = string.Empty;
                _outputFolderError = NotAFolderMessage;
                return;
            }

            _outputFolderAsset = folderAsset;
            _outputFolderPath = assetPath;
            _outputFolderError = string.Empty;
        }

        private static bool TryConvertToAssetPath(
            string absolutePath, out string assetPath)
        {
            assetPath = null;
            string normalizedSelection = NormalizePath(absolutePath).TrimEnd('/');
            string projectRelativePath = NormalizePath(
                FileUtil.GetProjectRelativePath(normalizedSelection)).TrimEnd('/');
            if (!string.IsNullOrEmpty(projectRelativePath))
            {
                assetPath = projectRelativePath;
            }

            string normalizedAssets = NormalizePath(Application.dataPath).TrimEnd('/');
            if (string.IsNullOrEmpty(assetPath) && string.Equals(
                    normalizedSelection,
                    normalizedAssets,
                    StringComparison.OrdinalIgnoreCase))
            {
                assetPath = "Assets";
            }
            else if (string.IsNullOrEmpty(assetPath) && normalizedSelection.StartsWith(
                         normalizedAssets + "/", StringComparison.OrdinalIgnoreCase))
            {
                assetPath = "Assets" +
                    normalizedSelection.Substring(normalizedAssets.Length);
            }

            if (string.IsNullOrEmpty(assetPath) ||
                !AssetDatabase.IsValidFolder(assetPath))
            {
                assetPath = null;
                return false;
            }

            return true;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
