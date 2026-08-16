using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal sealed class HairToneMatcherWindow : EditorWindow
    {
        internal const int SampleSize = 256;
        private const float DefaultAlphaThreshold = 0.5f;
        private const float PreviewHeight = 220f;

        [Serializable]
        private sealed class RendererCandidate
        {
            [SerializeField] internal Renderer Renderer;
            [SerializeField] internal bool IsSelected;
            [SerializeField] internal string Label;
        }

        private static readonly GUIContent TitleContent = new GUIContent("04_Hair Tone Matcher");
        private static readonly GUIContent UserMaskContent = new GUIContent("追加の除外マスク");
        private static readonly GUIContent AlphaContent = new GUIContent("アルファ閾値");
        private static readonly GUIContent UvContent = new GUIContent("サブメッシュの UV で絞り込む");
        private static readonly GUIContent MeasureContent = new GUIContent();
        private static readonly GUIContent[] SamplingContents =
        {
            new GUIContent("統計"), new GUIContent("スポイト"),
        };
        private static readonly GUIContent[] MethodContents =
        {
            new GUIContent("色調の補正"), new GUIContent("階調を合わせる"),
        };
        private static readonly string[] OutputModeLabels =
        {
            "複製して差し替え", "上書き", "テクスチャを作り直す",
        };
        private static readonly Color SelectedRowColor = new Color(0.22f, 0.45f, 0.75f, 0.28f);
        private static readonly Color MarkerWhite = Color.white;
        private static readonly Color MarkerBlack = Color.black;

        private const string IntroMessage =
            "元の髪と新しい髪の色を比べ、新しい髪へ書き込む補正値を提案します。";
        private const string OutsideMaskMessage =
            "この位置には補正が効きません。適用範囲の内側を選んでください。";
        private const string PickerFailureMessage =
            "透明な領域のため、色を拾えませんでした。";
        private const string BakeHelpMessage =
            "補正を適用したテクスチャを新しく作り、マテリアルの複製へ設定します。元のテクスチャは変更しません。";
        private const string PropertyCopyHelp =
            "反映されない場合は、そのシェーダーのインスペクターで該当の機能を一度有効にしてください。";

        [SerializeField] private List<HairToneSourceInput> _sourceInputs =
            new List<HairToneSourceInput>();
        [SerializeField] private Renderer _sourceRendererToAdd;
        [SerializeField] private int _sourceSlotToAdd;
        [SerializeField] private GameObject _destinationRoot;
        [SerializeField] private List<RendererCandidate> _rootCandidates =
            new List<RendererCandidate>();
        [SerializeField] private List<Renderer> _destinationRenderers =
            new List<Renderer>();
        [SerializeField] private Renderer _destinationRendererToAdd;
        [SerializeField] private Texture2D _userMask;
        [SerializeField] private float _alphaThreshold = DefaultAlphaThreshold;
        [SerializeField] private bool _useSubmeshUv = true;
        [SerializeField] private HairToneSampling _sampling = HairToneSampling.Statistics;
        [SerializeField] private HairToneMethod _method = HairToneMethod.ToneAdjust;
        [SerializeField] private bool _showCorrected = true;
        [SerializeField] private HairToneOutputMode _outputMode = HairToneOutputMode.DuplicateAndReplace;
        [SerializeField] private bool _writeMask;
        [SerializeField] private DefaultAsset _outputFolderAsset;
        [SerializeField] private string _outputFolderPath = string.Empty;
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private int _selectedTargetIndex;
        [SerializeField] private string _propertyFilter = string.Empty;
        [SerializeField] private string _resultSummary = string.Empty;

        private HairToneMatcherPlan _plan;
        private Texture2D _sourcePreview;
        private Texture2D _destinationPreview;
        private Texture2D _rawDestinationPreview;
        private Texture2D _gradationPreviewLut;
        private Rect _sourcePreviewRect;
        private Rect _destinationPreviewRect;
        private HairTonePickedPoint _sourcePick;
        private HairTonePickedPoint _destinationPick;
        private string _sourcePickLabel = "未選択";
        private string _destinationPickLabel = "未選択";
        private string _pickerMessage = string.Empty;
        private string _outputFolderError = string.Empty;
        private string _overwriteWarning = string.Empty;
        private int _measuredTargetIndex = -1;
        private float _valueColumnWidth;

        internal static void Open()
        {
            HairToneMatcherWindow window = GetWindow<HairToneMatcherWindow>(
                false, "04_Hair Tone Matcher", true);
            window.titleContent = TitleContent;
            window.minSize = new Vector2(640f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_sourceInputs == null) _sourceInputs = new List<HairToneSourceInput>();
            if (_destinationRenderers == null) _destinationRenderers = new List<Renderer>();
            if (_rootCandidates == null) _rootCandidates = new List<RendererCandidate>();
            RestoreOutputFolder();
            if (_destinationRoot != null && _rootCandidates.Count == 0)
            {
                RebuildRootCandidates();
            }
        }

        private void OnDisable()
        {
            DestroyPreviews();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField(TitleContent, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(IntroMessage, MessageType.Info);
            DrawInputs();

            string scanBlocked = GetScanBlockedReason();
            if (!string.IsNullOrEmpty(scanBlocked))
            {
                EditorGUILayout.HelpBox(scanBlocked, MessageType.Warning);
            }

            EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(scanBlocked));
            bool scanPressed = GUILayout.Button("色を比べる", GUILayout.Height(26f));
            EditorGUI.EndDisabledGroup();
            if (scanPressed) Scan();

            if (_plan != null)
            {
                DrawPlan();
            }

            DrawOutput();
            if (!string.IsNullOrEmpty(_resultSummary))
            {
                EditorGUILayout.HelpBox(_resultSummary, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawInputs()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("入力", EditorStyles.boldLabel);
            DrawSourceInputs();
            DrawDestinationInputs();

            Texture2D nextMask = EditorGUILayout.ObjectField(
                UserMaskContent, _userMask, typeof(Texture2D), false) as Texture2D;
            float nextAlpha = EditorGUILayout.Slider(AlphaContent, _alphaThreshold, 0f, 1f);
            bool nextUv = EditorGUILayout.Toggle(UvContent, _useSubmeshUv);
            if (nextMask != _userMask || !Mathf.Approximately(nextAlpha, _alphaThreshold) ||
                nextUv != _useSubmeshUv)
            {
                _userMask = nextMask;
                _alphaThreshold = nextAlpha;
                _useSubmeshUv = nextUv;
                InvalidatePlan();
            }
        }

        private void DrawSourceInputs()
        {
            EditorGUILayout.LabelField("改変元マテリアル", EditorStyles.miniBoldLabel);
            int removeIndex = -1;
            for (int i = 0; i < _sourceInputs.Count; i++)
            {
                HairToneSourceInput input = _sourceInputs[i];
                EditorGUILayout.BeginHorizontal();
                Material next = EditorGUILayout.ObjectField(
                    input.Material, typeof(Material), false) as Material;
                if (GUILayout.Button("×", GUILayout.Width(24f))) removeIndex = i;
                EditorGUILayout.EndHorizontal();
                if (next != input.Material)
                {
                    input.Material = next;
                    input.Renderer = null;
                    input.MaterialSlot = 0;
                    InvalidatePlan();
                }
            }

            if (removeIndex >= 0)
            {
                _sourceInputs.RemoveAt(removeIndex);
                InvalidatePlan();
            }

            if (GUILayout.Button("+ マテリアルを追加"))
            {
                _sourceInputs.Add(new HairToneSourceInput());
                InvalidatePlan();
            }

            EditorGUILayout.BeginHorizontal();
            _sourceRendererToAdd = EditorGUILayout.ObjectField(
                "Renderer から追加", _sourceRendererToAdd, typeof(Renderer), true) as Renderer;
            string[] slots = BuildSlotLabels(_sourceRendererToAdd);
            _sourceSlotToAdd = Mathf.Clamp(_sourceSlotToAdd, 0, slots.Length - 1);
            _sourceSlotToAdd = EditorGUILayout.Popup(_sourceSlotToAdd, slots, GUILayout.MinWidth(120f));
            EditorGUI.BeginDisabledGroup(_sourceRendererToAdd == null);
            bool add = GUILayout.Button("+", GUILayout.Width(28f));
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (add)
            {
                Material[] materials = _sourceRendererToAdd.sharedMaterials;
                if (_sourceSlotToAdd >= 0 && _sourceSlotToAdd < materials.Length)
                {
                    _sourceInputs.Add(new HairToneSourceInput
                    {
                        Material = materials[_sourceSlotToAdd],
                        Renderer = _sourceRendererToAdd,
                        MaterialSlot = _sourceSlotToAdd,
                    });
                    InvalidatePlan();
                }
            }
        }

        private void DrawDestinationInputs()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("改変先", EditorStyles.miniBoldLabel);
            GameObject nextRoot = EditorGUILayout.ObjectField(
                "GameObject / Prefab", _destinationRoot, typeof(GameObject), true) as GameObject;
            if (nextRoot != _destinationRoot)
            {
                _destinationRoot = nextRoot;
                RebuildRootCandidates();
                InvalidatePlan();
            }

            for (int i = 0; i < _rootCandidates.Count; i++)
            {
                RendererCandidate candidate = _rootCandidates[i];
                bool next = EditorGUILayout.ToggleLeft(candidate.Label, candidate.IsSelected);
                if (next != candidate.IsSelected)
                {
                    candidate.IsSelected = next;
                    if (next) AddDestinationRenderer(candidate.Renderer);
                    else RemoveDestinationRenderer(candidate.Renderer);
                    InvalidatePlan();
                }
            }

            EditorGUILayout.BeginHorizontal();
            _destinationRendererToAdd = EditorGUILayout.ObjectField(
                "Renderer を直接追加", _destinationRendererToAdd,
                typeof(Renderer), true) as Renderer;
            EditorGUI.BeginDisabledGroup(_destinationRendererToAdd == null);
            bool add = GUILayout.Button("+", GUILayout.Width(28f));
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (add)
            {
                AddDestinationRenderer(_destinationRendererToAdd);
                InvalidatePlan();
            }

            Rect dropRect = GUILayoutUtility.GetRect(10f, 30f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Renderer をここへドラッグ＆ドロップ");
            HandleRendererDrop(dropRect);

            int removeIndex = -1;
            for (int i = 0; i < _destinationRenderers.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(_destinationRenderers[i], typeof(Renderer), true);
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("×", GUILayout.Width(24f))) removeIndex = i;
                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0)
            {
                RemoveDestinationRenderer(_destinationRenderers[removeIndex]);
                InvalidatePlan();
            }
        }

        private void DrawPlan()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("比較結果", EditorStyles.boldLabel);
            DrawTargetList();
            HairToneTarget target = SelectedTarget;
            if (target == null || !string.IsNullOrEmpty(target.BlockedReason)) return;

            EditorGUILayout.LabelField(target.SelectedHeader, EditorStyles.boldLabel);
            DrawStatsSwatches(target);
            GUILayout.Label(BuildStatsLabel(target), EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label(BuildMaskLabel(_plan.SourceMaskCounts, target.MaskCounts),
                EditorStyles.wordWrappedMiniLabel);
            for (int i = 0; i < _plan.Warnings.Length; i++)
            {
                EditorGUILayout.HelpBox(_plan.Warnings[i], MessageType.Warning);
            }

            DrawAdjustment(target);
            DrawTextureComparison(target);
            DrawPropertyDiffs(target);
        }

        private void DrawTargetList()
        {
            if (_plan.Targets == null) return;
            if (CountSelectedTargets() == 0)
            {
                EditorGUILayout.HelpBox(
                    "対象にする行にチェックを入れてください。", MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("すべて選択")) SetAllTargetsSelected(true);
            if (GUILayout.Button("すべて解除")) SetAllTargetsSelected(false);
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < _plan.Targets.Count; i++)
            {
                HairToneTarget target = _plan.Targets[i];
                Rect row = GUILayoutUtility.GetRect(10f, 38f, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint && i == _selectedTargetIndex)
                {
                    EditorGUI.DrawRect(row, SelectedRowColor);
                }

                Rect markerRect = new Rect(row.x + 2f, row.y + 4f, 4f, row.height - 8f);
                if (Event.current.type == EventType.Repaint && i == _selectedTargetIndex)
                {
                    EditorGUI.DrawRect(markerRect, new Color(0.3f, 0.65f, 1f));
                }

                Rect toggleRect = new Rect(row.x + 10f, row.y + 4f, 18f, 18f);
                EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(target.BlockedReason));
                bool wasSelected = target.IsSelected;
                target.IsSelected = EditorGUI.Toggle(toggleRect, target.IsSelected);
                EditorGUI.EndDisabledGroup();
                if (wasSelected != target.IsSelected &&
                    _outputMode == HairToneOutputMode.Overwrite)
                    _overwriteWarning = BuildOverwriteWarning();

                Rect buttonRect = new Rect(row.x + 32f, row.y, row.width - 32f, row.height);
                EditorGUIUtility.AddCursorRect(buttonRect, MouseCursor.Link);
                string detail = !string.IsNullOrEmpty(target.BlockedReason)
                    ? target.BlockedReason : target.AdjustmentSummary;
                if (GUI.Button(buttonRect, target.RowContent, GUIStyle.none))
                {
                    SelectTarget(i);
                }
                GUI.Label(new Rect(buttonRect.x, buttonRect.y, buttonRect.width, 19f),
                    target.Label, EditorStyles.label);
                GUI.Label(new Rect(buttonRect.x, buttonRect.y + 19f, buttonRect.width, 17f),
                    detail, EditorStyles.miniLabel);
            }
        }

        private void DrawStatsSwatches(HairToneTarget target)
        {
            Rect row = GUILayoutUtility.GetRect(10f, 62f, GUILayout.ExpandWidth(true));
            float width = row.width / 3f;
            DrawSwatch(new Rect(row.x, row.y, width - 4f, 38f),
                _plan.SourceStats.Representative, "改変元");
            DrawSwatch(new Rect(row.x + width, row.y, width - 4f, 38f),
                GetVisibleDestinationStats(target).Representative, "改変先");
            DrawSwatch(new Rect(row.x + width * 2f, row.y, width - 4f, 38f),
                GetCorrectedRepresentative(target), "補正後");
        }

        private void DrawAdjustment(HairToneTarget target)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("補正の設定", EditorStyles.boldLabel);
            int nextSampling = GUILayout.Toolbar((int)_sampling, SamplingContents);
            if (nextSampling != (int)_sampling) SetSampling((HairToneSampling)nextSampling);

            bool gradationAvailable = CanUseGradation(target);
            EditorGUI.BeginDisabledGroup(!gradationAvailable);
            int nextMethod = GUILayout.Toolbar((int)_method, MethodContents);
            EditorGUI.EndDisabledGroup();
            if (nextMethod != (int)_method &&
                (nextMethod == (int)HairToneMethod.ToneAdjust || gradationAvailable))
            {
                _method = (HairToneMethod)nextMethod;
                RebuildDestinationPreview();
            }

            if (!gradationAvailable)
            {
                EditorGUILayout.HelpBox(_sampling == HairToneSampling.Picker
                    ? "スポイトでは階調を合わせる方式は選べません。"
                    : "このマテリアルには階調を書き込むプロパティがありません。",
                    MessageType.Info);
            }

            if (_method == HairToneMethod.ToneAdjust)
            {
                HairToneAdjustment value = target.Adjustment;
                EditorGUI.BeginChangeCheck();
                value.Hue = EditorGUILayout.FloatField("色相（加算）", value.Hue);
                value.Saturation = EditorGUILayout.FloatField("彩度（倍率）", value.Saturation);
                value.Value = EditorGUILayout.FloatField("明度（倍率）", value.Value);
                value.Gamma = EditorGUILayout.FloatField("ガンマ", value.Gamma);
                if (EditorGUI.EndChangeCheck())
                {
                    target.Adjustment = value;
                    target.IsAdjustmentEdited = true;
                    UpdateAdjustmentSummary(target);
                    RebuildDestinationPreview();
                }

                if (target.IsAdjustmentEdited)
                    EditorGUILayout.LabelField("編集済み", EditorStyles.miniBoldLabel);
                if (GUILayout.Button("算出値に戻す")) RestoreSuggestedAdjustment(target);
                EditorGUILayout.HelpBox(
                    "彩度と明度は 1.0 で丸められるため、元の色まで届かないことがあります。",
                    MessageType.Info);
            }

            if (_sampling == HairToneSampling.Picker) DrawPickerStatus();
        }

        private void DrawPickerStatus()
        {
            EditorGUILayout.BeginHorizontal();
            DrawPickedColor("改変元", _sourcePick, _sourcePickLabel, Color.white);
            DrawPickedColor("改変先", _destinationPick, _destinationPickLabel,
                SelectedTarget != null ? SelectedTarget.MainColor : Color.white);
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("拾い直す")) ClearPicks();
            if (!string.IsNullOrEmpty(_pickerMessage))
                EditorGUILayout.HelpBox(_pickerMessage, MessageType.Warning);
            else if (_destinationPick.HasValue && !_destinationPick.InsideMask)
                EditorGUILayout.HelpBox(OutsideMaskMessage, MessageType.Warning);
        }

        private static void DrawPickedColor(string label, HairTonePickedPoint point,
            string valueLabel, Color mainColor)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(label, EditorStyles.miniBoldLabel);
            Rect rect = GUILayoutUtility.GetRect(80f, 28f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, point.HasValue
                    ? HairToneShaderProfile.MultiplyMainColor(point.Color, mainColor)
                    : Color.gray);
            GUILayout.Label(valueLabel, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawTextureComparison(HairToneTarget target)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("テクスチャの比較", EditorStyles.boldLabel);
            _showCorrected = EditorGUILayout.Toggle("改変先を補正後で表示", _showCorrected);
            Rect area = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
            float half = (area.width - 8f) * 0.5f;
            _sourcePreviewRect = FitRect(new Rect(area.x, area.y, half, area.height), _sourcePreview);
            _destinationPreviewRect = FitRect(
                new Rect(area.x + half + 8f, area.y, half, area.height),
                _showCorrected ? _destinationPreview : _rawDestinationPreview);
            EditorGUIUtility.AddCursorRect(_sourcePreviewRect,
                _sampling == HairToneSampling.Picker ? MouseCursor.ArrowPlus : MouseCursor.Arrow);
            EditorGUIUtility.AddCursorRect(_destinationPreviewRect,
                _sampling == HairToneSampling.Picker ? MouseCursor.ArrowPlus : MouseCursor.Arrow);
            if (Event.current.type == EventType.Repaint)
            {
                if (_sourcePreview != null)
                    GUI.DrawTexture(_sourcePreviewRect, _sourcePreview, ScaleMode.ScaleToFit, false);
                Texture2D destination = _showCorrected
                    ? _destinationPreview : _rawDestinationPreview;
                if (destination != null)
                    GUI.DrawTexture(_destinationPreviewRect, destination, ScaleMode.ScaleToFit, false);
                if (_sampling == HairToneSampling.Picker)
                {
                    DrawMarker(_sourcePreviewRect, _sourcePick);
                    DrawMarker(_destinationPreviewRect, _destinationPick);
                }
            }

            HandlePickerClick(target);
        }

        private void DrawPropertyDiffs(HairToneTarget target)
        {
            if (target.PropertyDiffGroups == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(target.PropertyDiffHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(PropertyCopyHelp, MessageType.Info);

            string nextFilter = EditorGUILayout.TextField("絞り込み", _propertyFilter);
            if (!string.Equals(nextFilter, _propertyFilter, StringComparison.Ordinal))
            {
                _propertyFilter = nextFilter;
                ApplyPropertyFilter(target);
            }

            EditorGUILayout.BeginHorizontal();
            bool selectAll = GUILayout.Button("すべて選択");
            bool selectNone = GUILayout.Button("すべて解除");
            EditorGUILayout.EndHorizontal();
            if (selectAll || selectNone) SetVisibleProperties(target, selectAll);

            EnsureValueColumnWidth(target);
            for (int groupIndex = 0; groupIndex < target.PropertyDiffGroups.Count; groupIndex++)
            {
                HairTonePropertyDiffGroup group = target.PropertyDiffGroups[groupIndex];
                int visibleCount = CountVisible(group);
                if (visibleCount == 0) continue;

                bool all = true;
                bool any = false;
                for (int i = 0; i < group.Entries.Count; i++)
                {
                    if (!group.Entries[i].IsVisible) continue;
                    any |= group.Entries[i].IsSelected;
                    all &= group.Entries[i].IsSelected;
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUI.showMixedValue = any && !all;
                bool nextAll = EditorGUILayout.Toggle(all, GUILayout.Width(18f));
                EditorGUI.showMixedValue = false;
                group.IsExpanded = EditorGUILayout.Foldout(
                    group.IsExpanded, group.Header, true);
                EditorGUILayout.EndHorizontal();
                if (nextAll != all || (any && !all && nextAll))
                {
                    for (int i = 0; i < group.Entries.Count; i++)
                        if (group.Entries[i].IsVisible) group.Entries[i].IsSelected = nextAll;
                }

                if (!group.IsExpanded) continue;
                for (int i = 0; i < group.Entries.Count; i++)
                {
                    HairTonePropertyDiffEntry entry = group.Entries[i];
                    if (!entry.IsVisible) continue;
                    EditorGUILayout.BeginHorizontal();
                    entry.IsSelected = EditorGUILayout.Toggle(entry.IsSelected, GUILayout.Width(18f));
                    EditorGUILayout.LabelField(entry.DisplayName, GUILayout.ExpandWidth(true));
                    EditorGUILayout.LabelField(entry.DestinationValueLabel,
                        GUILayout.Width(_valueColumnWidth));
                    EditorGUILayout.LabelField(entry.SourceValueLabel,
                        GUILayout.Width(_valueColumnWidth));
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private void DrawOutput()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("出力", EditorStyles.boldLabel);
            HairToneOutputMode nextMode = (HairToneOutputMode)EditorGUILayout.Popup(
                "書き込み先", (int)_outputMode, OutputModeLabels);
            if (nextMode != _outputMode)
            {
                _outputMode = nextMode;
                _overwriteWarning = _outputMode == HairToneOutputMode.Overwrite && _plan != null
                    ? BuildOverwriteWarning() : string.Empty;
            }
            if (_outputMode == HairToneOutputMode.BakeTexture)
                EditorGUILayout.HelpBox(BakeHelpMessage, MessageType.Info);
            else if (_outputMode == HairToneOutputMode.Overwrite && _plan != null)
                EditorGUILayout.HelpBox(_overwriteWarning, MessageType.Warning);

            _writeMask = EditorGUILayout.Toggle("マスクを書き出す", _writeMask);
            if (NeedsOutputFolder()) DrawOutputFolder();
            else EditorGUILayout.HelpBox(
                "上書きで LUT とマスクを作らないため、出力フォルダは不要です。",
                MessageType.Info);

            string blocked = GetApplyBlockedReason();
            EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(blocked));
            bool apply = GUILayout.Button("適用", GUILayout.Height(30f));
            EditorGUI.EndDisabledGroup();
            if (!string.IsNullOrEmpty(blocked))
                GUILayout.Label(blocked, EditorStyles.wordWrappedMiniLabel);
            if (apply) Apply();
        }

        private void DrawOutputFolder()
        {
            EditorGUILayout.BeginHorizontal();
            DefaultAsset next = EditorGUILayout.ObjectField(
                "出力フォルダ", _outputFolderAsset, typeof(DefaultAsset), false) as DefaultAsset;
            Rect dropRect = GUILayoutUtility.GetLastRect();
            bool select = GUILayout.Button("選択", GUILayout.Width(52f));
            EditorGUILayout.EndHorizontal();
            if (next != _outputFolderAsset) SetOutputFolder(next);
            HandleFolderDrop(dropRect);
            if (select) OpenOutputFolder();
            if (!string.IsNullOrEmpty(_outputFolderError))
                EditorGUILayout.HelpBox(_outputFolderError, MessageType.Warning);
        }

        private void Scan()
        {
            _resultSummary = string.Empty;
            _pickerMessage = string.Empty;
            var combinedPixels = new List<Color>();
            var combinedMask = new List<bool>();
            HairToneMaskCounts combinedCounts = default;
            Color[] sourcePreviewPixels = null;
            bool[] sourcePreviewMask = null;
            Material primarySource = null;
            HairToneShaderProfile primarySourceProfile = null;
            var validSources = new List<HairToneSourceInput>();

            for (int sourceIndex = 0; sourceIndex < _sourceInputs.Count; sourceIndex++)
            {
                HairToneSourceInput input = _sourceInputs[sourceIndex];
                if (input == null || input.Material == null) continue;
                string property = HairToneShaderProfile.ResolveMainTexPropertyName(input.Material);
                Texture texture = !string.IsNullOrEmpty(property)
                    ? input.Material.GetTexture(property) : null;
                if (texture == null) continue;

                HairToneShaderProfile profile = HairToneShaderProfile.Resolve(input.Material);
                Color[] pixels = HairTonePixelSampler.Read(texture, SampleSize);
                HairToneAdjustment adjustment = HairToneShaderProfile.Read(input.Material, profile);
                Color mainColor = HairToneShaderProfile.ReadMainColor(input.Material, profile);
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = HairToneShaderProfile.ApplyToPixel(pixels[i], adjustment, profile);
                    pixels[i] = HairToneShaderProfile.MultiplyMainColor(pixels[i], mainColor);
                }

                bool[] mask = HairToneRegionMask.BuildSource(
                    input.Renderer, input.MaterialSlot, pixels, _alphaThreshold,
                    _useSubmeshUv, SampleSize, out HairToneMaskCounts counts);
                if (sourcePreviewPixels == null)
                {
                    sourcePreviewPixels = pixels;
                    sourcePreviewMask = mask;
                    primarySource = input.Material;
                    primarySourceProfile = profile;
                }

                validSources.Add(input);
                combinedPixels.AddRange(pixels);
                combinedMask.AddRange(mask);
                AddMaskCounts(ref combinedCounts, counts);
            }

            Color[] allSourcePixels = combinedPixels.ToArray();
            bool[] allSourceMask = combinedMask.ToArray();
            if (!HairToneStatistics.TryCompute(allSourcePixels, allSourceMask,
                    _alphaThreshold, out HairToneStats sourceStats))
            {
                _resultSummary = "改変元の対象画素が少なすぎます。";
                InvalidatePlan(false);
                return;
            }

            HairToneStatistics.ComputeCdf(allSourcePixels, allSourceMask,
                _alphaThreshold, out float[] sourceR, out float[] sourceG, out float[] sourceB);
            List<HairToneTarget> targets = BuildTargets(validSources, primarySource,
                primarySourceProfile, sourceStats);
            if (targets.Count == 0)
            {
                _resultSummary = "改変先の対象がありません。";
                InvalidatePlan(false);
                return;
            }

            var warnings = new List<string>();
            for (int i = 0; i < targets.Count; i++)
            {
                ScanTarget(targets[i], validSources, primarySource,
                    primarySourceProfile, sourceStats, warnings);
            }

            _plan = new HairToneMatcherPlan
            {
                Sources = validSources,
                Targets = targets,
                SourceMaterial = primarySource,
                SourceProfile = primarySourceProfile,
                SourceStats = sourceStats,
                SourceMaskCounts = combinedCounts,
                SourcePixels = allSourcePixels,
                SourceMask = allSourceMask,
                SourcePreviewPixels = sourcePreviewPixels,
                SourcePreviewMask = sourcePreviewMask,
                SourceCdf = new HairToneCdf { R = sourceR, G = sourceG, B = sourceB },
                Warnings = warnings.ToArray(),
                UserMask = _userMask,
                AlphaThreshold = _alphaThreshold,
                UseSubmeshUv = _useSubmeshUv,
            };
            _overwriteWarning = _outputMode == HairToneOutputMode.Overwrite
                ? BuildOverwriteWarning() : string.Empty;

            _selectedTargetIndex = FindFirstUsableTarget(targets);
            _sourcePick = default;
            _destinationPick = default;
            RefreshPickLabels();
            if (_sampling == HairToneSampling.Picker) _method = HairToneMethod.ToneAdjust;
            if (_method == HairToneMethod.GradationMatch && !CanUseGradation(SelectedTarget))
                _method = HairToneMethod.ToneAdjust;
            ApplyPropertyFilter(SelectedTarget);
            BuildPreviews();
        }

        private List<HairToneTarget> BuildTargets(List<HairToneSourceInput> sources,
            Material primarySource, HairToneShaderProfile primarySourceProfile,
            HairToneStats sourceStats)
        {
            var result = new List<HairToneTarget>();
            var byMaterial = new Dictionary<Material, HairToneTarget>();
            for (int rendererIndex = 0; rendererIndex < _destinationRenderers.Count; rendererIndex++)
            {
                Renderer renderer = _destinationRenderers[rendererIndex];
                if (renderer == null) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null) continue;
                    if (!byMaterial.TryGetValue(material, out HairToneTarget target))
                    {
                        target = new HairToneTarget { Material = material };
                        byMaterial.Add(material, target);
                        result.Add(target);
                    }

                    target.RendererSlots.Add(new HairToneRendererSlot
                    {
                        Renderer = renderer,
                        MaterialSlot = slot,
                    });
                }
            }

            for (int i = 0; i < result.Count; i++)
            {
                HairToneTarget target = result[i];
                HairToneRendererSlot first = target.RendererSlots[0];
                string shared = target.RendererSlots.Count > 1
                    ? string.Format(" (共有 {0})", target.RendererSlots.Count) : string.Empty;
                string prefab = first.Renderer != null &&
                    !first.Renderer.gameObject.scene.IsValid() ? " [Prefab アセット]" : string.Empty;
                target.Label = string.Format("{0} / {1}: {2}{3}{4}",
                    first.Renderer != null ? first.Renderer.name : "なし",
                    first.MaterialSlot, target.Material.name, shared, prefab);
                target.SelectedHeader = string.Format("選択中: {0}", target.Label);
                target.IsSelected = WasTargetSelected(target.Material);
                UpdateAdjustmentSummary(target);
            }

            return result;
        }

        private bool WasTargetSelected(Material material)
        {
            if (_plan == null || _plan.Targets == null || material == null) return false;
            for (int i = 0; i < _plan.Targets.Count; i++)
            {
                HairToneTarget previous = _plan.Targets[i];
                if (previous.Material == material) return previous.IsSelected;
            }

            return false;
        }

        private void ScanTarget(HairToneTarget target,
            List<HairToneSourceInput> sources, Material primarySource,
            HairToneShaderProfile primarySourceProfile, HairToneStats sourceStats,
            List<string> warnings)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].Material == target.Material)
                {
                    BlockTarget(target, "改変元と同じマテリアルです。");
                    return;
                }
            }

            target.Profile = HairToneShaderProfile.Resolve(target.Material);
            if (target.Profile == null)
            {
                BlockTarget(target, "対応シェーダーではありません。");
                return;
            }

            if (HairToneShaderProfile.IsLocked(target.Material, target.Profile))
            {
                BlockTarget(target, "マテリアルがロックされています。");
                return;
            }

            string mainProperty = HairToneShaderProfile.ResolveMainTexPropertyName(target.Material);
            Texture texture = !string.IsNullOrEmpty(mainProperty)
                ? target.Material.GetTexture(mainProperty) : null;
            if (texture == null)
            {
                BlockTarget(target, "メインテクスチャが見つかりません。");
                return;
            }

            target.Pixels = HairTonePixelSampler.Read(texture, SampleSize);
            target.MainColor = HairToneShaderProfile.ReadMainColor(
                target.Material, target.Profile);

            Color[] existingMask = null;
            if (!string.IsNullOrEmpty(target.Profile.RegionMaskProperty) &&
                target.Material.HasProperty(target.Profile.RegionMaskProperty))
            {
                existingMask = HairTonePixelSampler.Read(
                    target.Material.GetTexture(target.Profile.RegionMaskProperty), SampleSize);
            }

            HairToneRendererSlot representative = target.RendererSlots[0];
            target.DestinationMask = HairToneRegionMask.Build(
                representative.Renderer, representative.MaterialSlot, target.Pixels,
                existingMask, HairTonePixelSampler.Read(_userMask, SampleSize),
                _alphaThreshold, _useSubmeshUv, SampleSize, out target.MaskCounts);
            if (!HairToneStatistics.TryCompute(target.Pixels, target.DestinationMask,
                    _alphaThreshold, out target.Stats))
            {
                BlockTarget(target, "対象の画素が少なすぎます。");
                return;
            }

            HairToneStatistics.ComputeCdf(target.Pixels, target.DestinationMask,
                _alphaThreshold, out float[] r, out float[] g, out float[] b);
            target.Cdf = new HairToneCdf { R = r, G = g, B = b };
            target.SuggestedAdjustment = HairToneStatistics.Solve(
                sourceStats, target.Stats, target.MainColor, target.Profile);
            target.Adjustment = target.SuggestedAdjustment;
            target.IsAdjustmentEdited = false;
            UpdateAdjustmentSummary(target);

            if (primarySourceProfile != null &&
                string.Equals(primarySourceProfile.Id, target.Profile.Id, StringComparison.Ordinal))
            {
                target.PropertyDiffGroups = HairTonePropertyDiff.Collect(
                    primarySource, target.Material, target.Profile,
                    out target.IdenticalPropertyCount);
                int differenceCount = CountEntries(target.PropertyDiffGroups);
                target.PropertyDiffHeader = string.Format(
                    "マテリアル設定のコピー  差分 {0} 件 / 一致 {1} 件",
                    differenceCount, target.IdenticalPropertyCount);
                RestoreGroupFoldouts(target);
            }

            AddWarnings(warnings, target);
        }

        private void Apply()
        {
            List<HairToneApplyResult> results = HairToneMatcherApplier.Apply(
                _plan, _method, _outputMode, _outputFolderPath, _writeMask);
            _resultSummary = BuildResultSummary(results);
            InvalidatePlan(false);
        }

        private string GetScanBlockedReason()
        {
            if (EditorApplication.isPlaying) return "Play Mode 中は実行できません。";
            bool hasSource = false;
            for (int i = 0; i < _sourceInputs.Count; i++)
                hasSource |= _sourceInputs[i] != null && _sourceInputs[i].Material != null;
            if (!hasSource) return "改変元マテリアルを 1 つ以上指定してください。";
            if (_destinationRenderers.Count == 0) return "改変先 Renderer を 1 つ以上指定してください。";
            return null;
        }

        private string GetApplyBlockedReason()
        {
            if (_plan == null) return "先に色を比べてください。";
            int selected = 0;
            for (int i = 0; i < _plan.Targets.Count; i++)
            {
                HairToneTarget target = _plan.Targets[i];
                if (!target.IsSelected || !string.IsNullOrEmpty(target.BlockedReason)) continue;
                selected++;
                if (_outputMode == HairToneOutputMode.Overwrite &&
                    !IsOverwriteAvailable(target.Material, out string reason)) return reason;
            }

            if (selected == 0) return "処理する対象を 1 件以上選んでください。";
            if (_sampling == HairToneSampling.Picker &&
                (!_sourcePick.HasValue || !_destinationPick.HasValue))
                return "改変元と改変先から色を 1 点ずつ選んでください。";
            if (_sampling == HairToneSampling.Picker && !_destinationPick.InsideMask)
                return OutsideMaskMessage;
            if (NeedsOutputFolder() && !IsWritableOutputFolder(_outputFolderPath))
                return "出力フォルダを指定してください。";
            return null;
        }

        private bool CanUseGradation(HairToneTarget target)
        {
            return target != null && _sampling == HairToneSampling.Statistics &&
                target.Profile != null &&
                !string.IsNullOrEmpty(target.Profile.GradationTexProperty) &&
                !string.IsNullOrEmpty(target.Profile.GradationStrengthProperty) &&
                target.Material.HasProperty(target.Profile.GradationTexProperty) &&
                target.Material.HasProperty(target.Profile.GradationStrengthProperty);
        }

        private bool NeedsOutputFolder()
        {
            return _outputMode != HairToneOutputMode.Overwrite ||
                _method == HairToneMethod.GradationMatch || _writeMask;
        }

        private static bool IsOverwriteAvailable(Material material, out string reason)
        {
            reason = null;
            if (material == null) return false;
            string path = AssetDatabase.GetAssetPath(material).Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(path), ".mat", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Packages 配下またはモデル内のマテリアルには上書きできません。";
                return false;
            }

            if (!AssetDatabase.IsOpenForEdit(material, StatusQueryOptions.UseCachedIfPossible))
            {
                reason = "読み取り専用のマテリアルには上書きできません。";
                return false;
            }

            return true;
        }

        private string BuildOverwriteWarning()
        {
            var materials = new HashSet<Material>();
            for (int i = 0; i < _plan.Targets.Count; i++)
            {
                HairToneTarget target = _plan.Targets[i];
                if (target.IsSelected && target.Material != null) materials.Add(target.Material);
            }

            int otherRendererCount = 0;
            Renderer[] renderers = Resources.FindObjectsOfTypeAll<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.gameObject.scene.IsValid() ||
                    _destinationRenderers.Contains(renderer)) continue;
                Material[] rendererMaterials = renderer.sharedMaterials;
                for (int slot = 0; slot < rendererMaterials.Length; slot++)
                {
                    if (!materials.Contains(rendererMaterials[slot])) continue;
                    otherRendererCount++;
                    break;
                }
            }

            return otherRendererCount == 0
                ? "選択したマテリアルを参照する他の Renderer はありません。"
                : string.Format(
                    "選択したマテリアルを参照する他の Renderer が {0} 件あります。",
                    otherRendererCount);
        }

        private void SetSampling(HairToneSampling sampling)
        {
            _sampling = sampling;
            HairToneTarget target = SelectedTarget;
            if (target == null) return;
            if (_sampling == HairToneSampling.Statistics)
            {
                target.Adjustment = target.SuggestedAdjustment;
                target.IsAdjustmentEdited = false;
            }
            else
            {
                if (_method == HairToneMethod.GradationMatch)
                    _method = HairToneMethod.ToneAdjust;
                RecomputePickerAdjustment(target);
            }

            UpdateAdjustmentSummary(target);
            RebuildDestinationPreview();
        }

        private void RestoreSuggestedAdjustment(HairToneTarget target)
        {
            if (_sampling == HairToneSampling.Picker) RecomputePickerAdjustment(target);
            else
            {
                target.Adjustment = target.SuggestedAdjustment;
                target.IsAdjustmentEdited = false;
            }

            UpdateAdjustmentSummary(target);
            RebuildDestinationPreview();
        }

        private void RecomputePickerAdjustment(HairToneTarget target)
        {
            if (target == null) return;
            target.Adjustment = HairToneAdjustment.Neutral;
            target.IsAdjustmentEdited = false;
            if (_sourcePick.HasValue && _destinationPick.HasValue)
            {
                target.Adjustment = HairToneStatistics.Solve(
                    StatsFromPick(_sourcePick), StatsFromPick(_destinationPick),
                    target.MainColor, target.Profile);
            }

            UpdateAdjustmentSummary(target);
        }

        private static HairToneStats StatsFromPick(HairTonePickedPoint point)
        {
            Color.RGBToHSV(point.Color, out float h, out float s, out float v);
            return new HairToneStats
            {
                Hue = h,
                Saturation = s,
                Value = v,
                Representative = point.Color,
                SampleCount = 25,
            };
        }

        private void HandlePickerClick(HairToneTarget target)
        {
            Event current = Event.current;
            if (_sampling != HairToneSampling.Picker || current.type != EventType.MouseDown ||
                current.button != 0) return;

            _pickerMessage = string.Empty;
            bool attempted = false;
            bool picked = false;
            if (HairTonePixelPicker.TryGetUv(_sourcePreviewRect,
                    current.mousePosition, out Vector2 sourceUv))
            {
                attempted = true;
                picked = HairTonePixelPicker.TryPick(_plan.SourcePreviewPixels,
                    _plan.SourcePreviewMask, SampleSize, sourceUv, _alphaThreshold,
                    out HairTonePickedPoint point);
                if (picked) _sourcePick = point;
            }
            else if (HairTonePixelPicker.TryGetUv(_destinationPreviewRect,
                         current.mousePosition, out Vector2 destinationUv))
            {
                attempted = true;
                picked = HairTonePixelPicker.TryPick(target.Pixels,
                    target.DestinationMask, SampleSize, destinationUv, _alphaThreshold,
                    out HairTonePickedPoint point);
                if (picked) _destinationPick = point;
            }

            if (!attempted) return;
            if (!picked) _pickerMessage = PickerFailureMessage;
            RefreshPickLabels();
            RecomputePickerAdjustment(target);
            RebuildDestinationPreview();
            current.Use();
            Repaint();
        }

        private void ClearPicks()
        {
            _sourcePick = default;
            _destinationPick = default;
            _pickerMessage = string.Empty;
            RefreshPickLabels();
            RecomputePickerAdjustment(SelectedTarget);
            RebuildDestinationPreview();
        }

        private void SelectTarget(int index)
        {
            if (_plan == null || index < 0 || index >= _plan.Targets.Count) return;
            _selectedTargetIndex = index;
            _measuredTargetIndex = -1;
            _sourcePick = default;
            _destinationPick = default;
            _pickerMessage = string.Empty;
            RefreshPickLabels();
            ApplyPropertyFilter(SelectedTarget);
            BuildPreviews();
        }

        private void BuildPreviews()
        {
            DestroyPreviews();
            HairToneTarget target = SelectedTarget;
            if (_plan == null || target == null || target.Pixels == null) return;
            _sourcePreview = CreatePreviewTexture(_plan.SourcePreviewPixels);
            _rawDestinationPreview = CreateVisiblePreviewTexture(
                target.Pixels, target.MainColor);
            _gradationPreviewLut = HairToneGradationLut.Build(
                _plan.SourceCdf.R, _plan.SourceCdf.G, _plan.SourceCdf.B,
                target.Cdf.R, target.Cdf.G, target.Cdf.B);
            _gradationPreviewLut.hideFlags = HideFlags.HideAndDontSave;
            RebuildDestinationPreview();
        }

        private void RebuildDestinationPreview()
        {
            HairToneTarget target = SelectedTarget;
            if (target == null || target.Pixels == null) return;
            DestroyPreview(ref _destinationPreview);
            var pixels = new Color[target.Pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color color = target.Pixels[i];
                if (target.DestinationMask == null ||
                    (i < target.DestinationMask.Length && target.DestinationMask[i]))
                {
                    color = _method == HairToneMethod.GradationMatch
                        ? HairToneGradationLut.ApplyToPixel(color, _gradationPreviewLut)
                        : HairToneShaderProfile.ApplyToPixel(
                            color, target.Adjustment, target.Profile);
                }

                pixels[i] = HairToneShaderProfile.MultiplyMainColor(
                    color, target.MainColor);
            }

            _destinationPreview = CreatePreviewTexture(pixels);
            Repaint();
        }

        private static Texture2D CreatePreviewTexture(Color[] pixels)
        {
            if (pixels == null || pixels.Length < SampleSize * SampleSize) return null;
            var texture = new Texture2D(SampleSize, SampleSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D CreateVisiblePreviewTexture(Color[] rawPixels,
            Color mainColor)
        {
            if (rawPixels == null) return null;
            var visiblePixels = new Color[rawPixels.Length];
            for (int i = 0; i < rawPixels.Length; i++)
            {
                visiblePixels[i] = HairToneShaderProfile.MultiplyMainColor(
                    rawPixels[i], mainColor);
            }

            return CreatePreviewTexture(visiblePixels);
        }

        private void DestroyPreviews()
        {
            DestroyPreview(ref _sourcePreview);
            DestroyPreview(ref _destinationPreview);
            DestroyPreview(ref _rawDestinationPreview);
            DestroyPreview(ref _gradationPreviewLut);
        }

        private static void DestroyPreview(ref Texture2D texture)
        {
            if (texture == null) return;
            DestroyImmediate(texture);
            texture = null;
        }

        private void InvalidatePlan(bool clearResult = true)
        {
            _plan = null;
            _sourcePick = default;
            _destinationPick = default;
            RefreshPickLabels();
            DestroyPreviews();
            if (clearResult) _resultSummary = string.Empty;
        }

        private void RebuildRootCandidates()
        {
            _rootCandidates.Clear();
            if (_destinationRoot == null) return;
            Renderer[] renderers = _destinationRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                _rootCandidates.Add(new RendererCandidate
                {
                    Renderer = renderer,
                    IsSelected = false,
                    Label = renderer.name,
                });
            }
        }

        private void AddDestinationRenderer(Renderer renderer)
        {
            if (renderer == null || _destinationRenderers.Contains(renderer)) return;
            _destinationRenderers.Add(renderer);
        }

        private void RemoveDestinationRenderer(Renderer renderer)
        {
            _destinationRenderers.Remove(renderer);
            for (int i = 0; i < _rootCandidates.Count; i++)
            {
                if (_rootCandidates[i].Renderer == renderer)
                    _rootCandidates[i].IsSelected = false;
            }
        }

        private void HandleRendererDrop(Rect rect)
        {
            Event current = Event.current;
            if (!rect.Contains(current.mousePosition) ||
                (current.type != EventType.DragUpdated && current.type != EventType.DragPerform))
                return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                {
                    UnityEngine.Object item = DragAndDrop.objectReferences[i];
                    Renderer renderer = item as Renderer;
                    if (renderer != null) AddDestinationRenderer(renderer);
                    GameObject gameObject = item as GameObject;
                    if (gameObject != null)
                    {
                        Renderer[] children = gameObject.GetComponentsInChildren<Renderer>(true);
                        for (int j = 0; j < children.Length; j++) AddDestinationRenderer(children[j]);
                    }
                }

                InvalidatePlan();
            }

            current.Use();
        }

        private static string[] BuildSlotLabels(Renderer renderer)
        {
            if (renderer == null || renderer.sharedMaterials.Length == 0)
                return new[] { "マテリアルなし" };
            Material[] materials = renderer.sharedMaterials;
            var labels = new string[materials.Length];
            for (int i = 0; i < labels.Length; i++)
                labels[i] = string.Format("{0}: {1}", i,
                    materials[i] != null ? materials[i].name : "なし");
            return labels;
        }

        private void SetAllTargetsSelected(bool selected)
        {
            for (int i = 0; i < _plan.Targets.Count; i++)
            {
                HairToneTarget target = _plan.Targets[i];
                target.IsSelected = selected && string.IsNullOrEmpty(target.BlockedReason);
            }
            if (_outputMode == HairToneOutputMode.Overwrite)
                _overwriteWarning = BuildOverwriteWarning();
        }

        private int CountSelectedTargets()
        {
            if (_plan == null || _plan.Targets == null) return 0;
            int count = 0;
            for (int i = 0; i < _plan.Targets.Count; i++)
            {
                HairToneTarget target = _plan.Targets[i];
                if (target.IsSelected && string.IsNullOrEmpty(target.BlockedReason)) count++;
            }

            return count;
        }

        private void ApplyPropertyFilter(HairToneTarget target)
        {
            if (target == null || target.PropertyDiffGroups == null) return;
            string filter = (_propertyFilter ?? string.Empty).Trim().ToLowerInvariant();
            for (int groupIndex = 0; groupIndex < target.PropertyDiffGroups.Count; groupIndex++)
            {
                List<HairTonePropertyDiffEntry> entries =
                    target.PropertyDiffGroups[groupIndex].Entries;
                for (int i = 0; i < entries.Count; i++)
                    entries[i].IsVisible = filter.Length == 0 || entries[i].SearchText.Contains(filter);
            }
        }

        private static void SetVisibleProperties(HairToneTarget target, bool selected)
        {
            for (int groupIndex = 0; groupIndex < target.PropertyDiffGroups.Count; groupIndex++)
            {
                List<HairTonePropertyDiffEntry> entries =
                    target.PropertyDiffGroups[groupIndex].Entries;
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i].IsVisible) entries[i].IsSelected = selected;
            }
        }

        private void EnsureValueColumnWidth(HairToneTarget target)
        {
            if (_measuredTargetIndex == _selectedTargetIndex) return;
            _valueColumnWidth = 60f;
            GUIStyle style = EditorStyles.label;
            for (int groupIndex = 0; groupIndex < target.PropertyDiffGroups.Count; groupIndex++)
            {
                List<HairTonePropertyDiffEntry> entries =
                    target.PropertyDiffGroups[groupIndex].Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    MeasureContent.text = entries[i].SourceValueLabel;
                    float sourceWidth = style.CalcSize(MeasureContent).x + 8f;
                    MeasureContent.text = entries[i].DestinationValueLabel;
                    float destinationWidth = style.CalcSize(MeasureContent).x + 8f;
                    _valueColumnWidth = Mathf.Max(_valueColumnWidth,
                        sourceWidth, destinationWidth);
                }
            }

            _valueColumnWidth = Mathf.Min(_valueColumnWidth, 210f);
            _measuredTargetIndex = _selectedTargetIndex;
        }

        private void RestoreGroupFoldouts(HairToneTarget target)
        {
            if (_plan == null || _plan.Targets == null || target.PropertyDiffGroups == null) return;
            HairToneTarget previous = null;
            for (int i = 0; i < _plan.Targets.Count; i++)
                if (_plan.Targets[i].Material == target.Material) previous = _plan.Targets[i];
            if (previous == null || previous.PropertyDiffGroups == null) return;
            for (int i = 0; i < target.PropertyDiffGroups.Count; i++)
            {
                for (int j = 0; j < previous.PropertyDiffGroups.Count; j++)
                {
                    if (target.PropertyDiffGroups[i].DisplayName ==
                        previous.PropertyDiffGroups[j].DisplayName)
                        target.PropertyDiffGroups[i].IsExpanded =
                            previous.PropertyDiffGroups[j].IsExpanded;
                }
            }
        }

        private void RestoreOutputFolder()
        {
            if (_outputFolderAsset == null && IsWritableOutputFolder(_outputFolderPath))
                _outputFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_outputFolderPath);
        }

        private void SetOutputFolder(DefaultAsset folder)
        {
            string path = folder != null
                ? AssetDatabase.GetAssetPath(folder).Replace('\\', '/') : string.Empty;
            if (folder != null && !IsWritableOutputFolder(path))
            {
                _outputFolderAsset = null;
                _outputFolderPath = string.Empty;
                _outputFolderError = "Assets 配下のフォルダを指定してください。";
                return;
            }

            _outputFolderAsset = folder;
            _outputFolderPath = path;
            _outputFolderError = string.Empty;
        }

        private void OpenOutputFolder()
        {
            string selected = EditorUtility.OpenFolderPanel(
                "出力先のフォルダを選択", "Assets", string.Empty);
            if (!string.IsNullOrEmpty(selected))
            {
                string assetPath = FileUtil.GetProjectRelativePath(selected)
                    .Replace('\\', '/').TrimEnd('/');
                if (IsWritableOutputFolder(assetPath))
                    SetOutputFolder(AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath));
                else _outputFolderError = "Assets 配下のフォルダを指定してください。";
            }

            GUIUtility.ExitGUI();
        }

        private void HandleFolderDrop(Rect rect)
        {
            Event current = Event.current;
            if (!rect.Contains(current.mousePosition) ||
                (current.type != EventType.DragUpdated && current.type != EventType.DragPerform))
                return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                SetOutputFolder(DragAndDrop.objectReferences.Length > 0
                    ? DragAndDrop.objectReferences[0] as DefaultAsset : null);
            }

            current.Use();
        }

        private static bool IsWritableOutputFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string normalized = path.Replace('\\', '/').TrimEnd('/');
            return (normalized == "Assets" ||
                    normalized.StartsWith("Assets/", StringComparison.Ordinal)) &&
                AssetDatabase.IsValidFolder(normalized);
        }

        private static void AddWarnings(List<string> warnings, HairToneTarget target)
        {
            for (int i = 0; i < target.Profile.LayeredTexProperties.Length; i++)
            {
                string property = target.Profile.LayeredTexProperties[i];
                if (target.Material.HasProperty(property) &&
                    target.Material.GetFloat(property) >= 0.5f)
                {
                    string message = target.Label + ": 重ね塗りが有効です。メインテクスチャだけでは見た目が合わないことがあります。";
                    if (!warnings.Contains(message)) warnings.Add(message);
                    break;
                }
            }

            if (!IsNeutral(HairToneShaderProfile.Read(target.Material, target.Profile)))
            {
                string message = target.Label + ": 既に入っている色調補正は適用時に置き換わります。";
                if (!warnings.Contains(message)) warnings.Add(message);
            }
        }

        private static bool IsNeutral(HairToneAdjustment value)
        {
            return Mathf.Approximately(value.Hue, 0f) &&
                Mathf.Approximately(value.Saturation, 1f) &&
                Mathf.Approximately(value.Value, 1f) &&
                Mathf.Approximately(value.Gamma, 1f);
        }

        private HairToneTarget SelectedTarget
        {
            get
            {
                if (_plan == null || _plan.Targets == null ||
                    _selectedTargetIndex < 0 || _selectedTargetIndex >= _plan.Targets.Count)
                    return null;
                return _plan.Targets[_selectedTargetIndex];
            }
        }

        private static int FindFirstUsableTarget(List<HairToneTarget> targets)
        {
            for (int i = 0; i < targets.Count; i++)
                if (string.IsNullOrEmpty(targets[i].BlockedReason)) return i;
            return targets.Count > 0 ? 0 : -1;
        }

        private static void BlockTarget(HairToneTarget target, string reason)
        {
            target.BlockedReason = reason;
            target.IsSelected = false;
            target.AdjustmentSummary = "実行不可";
            UpdateRowContent(target);
        }

        private static void UpdateAdjustmentSummary(HairToneTarget target)
        {
            HairToneAdjustment value = target.Adjustment;
            target.AdjustmentSummary = string.Format("H {0:+0.00;-0.00;0.00} / S x{1:0.00} / V x{2:0.00}{3}",
                value.Hue, value.Saturation, value.Value,
                target.IsAdjustmentEdited ? " *" : string.Empty);
            UpdateRowContent(target);
        }

        private static void UpdateRowContent(HairToneTarget target)
        {
            string tooltip = !string.IsNullOrEmpty(target.BlockedReason)
                ? target.BlockedReason : target.AdjustmentSummary;
            target.RowContent = new GUIContent(target.Label, tooltip);
        }

        private static int CountEntries(List<HairTonePropertyDiffGroup> groups)
        {
            if (groups == null) return 0;
            int count = 0;
            for (int i = 0; i < groups.Count; i++) count += groups[i].Entries.Count;
            return count;
        }

        private static int CountVisible(HairTonePropertyDiffGroup group)
        {
            int count = 0;
            for (int i = 0; i < group.Entries.Count; i++)
                if (group.Entries[i].IsVisible) count++;
            return count;
        }

        private static void AddMaskCounts(ref HairToneMaskCounts total,
            HairToneMaskCounts value)
        {
            total.Total += value.Total;
            total.DroppedByAlpha += value.DroppedByAlpha;
            total.DroppedByUv += value.DroppedByUv;
            total.DroppedByExistingMask += value.DroppedByExistingMask;
            total.DroppedByUserMask += value.DroppedByUserMask;
            total.Selected += value.Selected;
        }

        private string BuildStatsLabel(HairToneTarget target)
        {
            HairToneStats visibleDestination = GetVisibleDestinationStats(target);
            return string.Format(
                "改変元 HSV ({0:F3}, {1:F3}, {2:F3}) / 改変先 HSV ({3:F3}, {4:F3}, {5:F3})",
                _plan.SourceStats.Hue, _plan.SourceStats.Saturation, _plan.SourceStats.Value,
                visibleDestination.Hue, visibleDestination.Saturation,
                visibleDestination.Value);
        }

        private static HairToneStats GetVisibleDestinationStats(HairToneTarget target)
        {
            return HairToneStatistics.StatsAfter(target.Stats,
                HairToneAdjustment.Neutral, target.MainColor, target.Profile);
        }

        private string BuildMaskLabel(HairToneMaskCounts source,
            HairToneMaskCounts destination)
        {
            return string.Format(
                "改変元: 対象 {0} / 全体 {1}（アルファ {2}、UV {3} を除外）\n" +
                "改変先: 対象 {4} / 全体 {5}（アルファ {6}、UV {7}、既存マスク {8}、追加マスク {9} を除外）",
                source.Selected, source.Total, source.DroppedByAlpha, source.DroppedByUv,
                destination.Selected, destination.Total, destination.DroppedByAlpha,
                destination.DroppedByUv, destination.DroppedByExistingMask,
                destination.DroppedByUserMask);
        }

        private Color GetCorrectedRepresentative(HairToneTarget target)
        {
            if (_method == HairToneMethod.GradationMatch)
            {
                Color color = HairToneGradationLut.ApplyToPixel(
                    target.Stats.Representative, _gradationPreviewLut);
                return HairToneShaderProfile.MultiplyMainColor(
                    color, target.MainColor);
            }
            return HairToneStatistics.PreviewColor(
                target.Stats, target.Adjustment, target.MainColor, target.Profile);
        }

        private static void DrawSwatch(Rect rect, Color color, string label)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(rect, color);
            GUI.Label(new Rect(rect.x, rect.yMax + 2f, rect.width, 18f), label,
                EditorStyles.centeredGreyMiniLabel);
        }

        private static Rect FitRect(Rect area, Texture texture)
        {
            if (texture == null || area.width <= 0f || area.height <= 0f) return area;
            float scale = Mathf.Min(area.width / texture.width, area.height / texture.height);
            float width = texture.width * scale;
            float height = texture.height * scale;
            return new Rect(area.x + (area.width - width) * 0.5f,
                area.y + (area.height - height) * 0.5f, width, height);
        }

        private static void DrawMarker(Rect rect, HairTonePickedPoint point)
        {
            if (!point.HasValue) return;
            float x = rect.x + point.Uv.x * rect.width;
            float y = rect.y + (1f - point.Uv.y) * rect.height;
            DrawMarkerLines(x, y, MarkerBlack, 3f, 15f);
            DrawMarkerLines(x, y, MarkerWhite, 1f, 13f);
        }

        private static void DrawMarkerLines(float x, float y, Color color,
            float thickness, float length)
        {
            EditorGUI.DrawRect(new Rect(x - thickness * 0.5f,
                y - length, thickness, length * 2f), color);
            EditorGUI.DrawRect(new Rect(x - length,
                y - thickness * 0.5f, length * 2f, thickness), color);
            EditorGUI.DrawRect(new Rect(x - 7f, y - 7f, 14f, thickness), color);
            EditorGUI.DrawRect(new Rect(x - 7f, y + 7f - thickness, 14f, thickness), color);
            EditorGUI.DrawRect(new Rect(x - 7f, y - 7f, thickness, 14f), color);
            EditorGUI.DrawRect(new Rect(x + 7f - thickness, y - 7f, thickness, 14f), color);
        }

        private void RefreshPickLabels()
        {
            _sourcePickLabel = _sourcePick.HasValue ? _sourcePick.Color.ToString("F3") : "未選択";
            if (_destinationPick.HasValue && SelectedTarget != null)
            {
                Color visible = HairToneShaderProfile.MultiplyMainColor(
                    _destinationPick.Color, SelectedTarget.MainColor);
                _destinationPickLabel = visible.ToString("F3");
            }
            else
            {
                _destinationPickLabel = "未選択";
            }
        }

        private static string BuildResultSummary(List<HairToneApplyResult> results)
        {
            var lines = new List<string>();
            int succeeded = 0;
            int failed = 0;
            for (int i = 0; i < results.Count; i++)
            {
                HairToneApplyResult result = results[i];
                if (!result.Succeeded)
                {
                    failed++;
                    lines.Add(result.TargetLabel + ": 失敗 — " + result.Error);
                    continue;
                }

                succeeded++;
                string path = !string.IsNullOrEmpty(result.TexturePath)
                    ? result.TexturePath : result.MaterialPath;
                lines.Add(result.TargetLabel + ": 成功 — " + path);
            }

            lines.Add(string.Format("成功 {0} 件 / 失敗 {1} 件", succeeded, failed));
            lines.Add("Ctrl+Z ではマテリアルと Renderer の変更をまとめて戻せます。生成したファイルは残ります。");
            return string.Join("\n", lines);
        }
    }
}
