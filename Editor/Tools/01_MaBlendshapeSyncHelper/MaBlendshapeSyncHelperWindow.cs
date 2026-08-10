using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Poyo.CandyBox.MaBlendshapeSyncHelper.Editor
{
    internal sealed class MaBlendshapeSyncHelperWindow : EditorWindow
    {
        private static readonly GUIContent TitleContent =
            new GUIContent("01_Helper for MA Blendshape Sync");
        private static readonly GUIContent IntroContent = new GUIContent(
            "素体と同じ名前のシェイプキーを衣装側から探し、追従設定をまとめて作ります。");
        private static readonly GUIContent SourceHeaderContent = new GUIContent("素体メッシュ");
        private static readonly GUIContent RemoveContent = new GUIContent("×");
        private static readonly GUIContent AddContent = new GUIContent("+ 追加");
        private static readonly GUIContent CostumeRootContent = new GUIContent("衣装ルート");
        private static readonly GUIContent IncludeInactiveContent =
            new GUIContent("非アクティブなオブジェクトも含める");
        private static readonly GUIContent ScanContent = new GUIContent("対応を確認");
        private static readonly GUIContent ApplyContent = new GUIContent("適用");
        private static readonly GUIContent GroupsHeaderContent = new GUIContent("対応");
        private static readonly GUIContent SourceWeightContent = new GUIContent("素体の値");
        private static readonly GUIContent SelectAllContent = new GUIContent("すべて選択");
        private static readonly GUIContent ClearAllContent = new GUIContent("すべて解除");
        private static readonly GUIContent ManualLinkContent =
            new GUIContent("衣装側の名前で一括紐付け");
        private static readonly GUIContent LinkAllContent = new GUIContent("まとめて追加");
        private static readonly GUIContent ConfiguredContent = new GUIContent("設定済み");
        private static readonly GUIContent SearchContent = new GUIContent("名前で絞り込み");
        private static readonly GUIContent AddSourceShapeContent =
            new GUIContent("素体のシェイプキーを追加");
        private static readonly GUIContent UnselectedShapeContent =
            new GUIContent("（選択してください）");

        private const string PlayingWarning =
            "再生中は実行できません。再生を停止してください。";
        private const string SourceWarning = "素体メッシュを 1 つ以上指定してください。";
        private const string MissingMeshWarning =
            "素体メッシュにメッシュが設定されていないものがあります。";
        private const string MissingShapeWarning = "素体メッシュにシェイプキーがありません。";
        private const string CostumeRootWarning = "衣装ルートを指定してください。";
        private const string DuplicateSourceWarning =
            "Candy Box: 同じ素体メッシュが既に指定されています。";
        private const string NoVisibleGroupMessage =
            "表示できる対応がありません。絞り込みを見直すか、素体のシェイプキーを追加してください。";
        private const string SourceDropdownTitle = "素体のシェイプキー";
        private const string CostumeDropdownTitle = "衣装側のシェイプキー";
        private const string ConfirmFormat =
            "{0} 件の対応を設定し、{1} 件を削除します。よろしいですか？";
        private const string ResultFormat =
            "{0} 個のメッシュに {1} 件の対応を設定しました。（新規コンポーネント {2} 件 / 削除 {3} 件）";

        [SerializeField] private List<SkinnedMeshRenderer> _sourceRenderers =
            new List<SkinnedMeshRenderer>();
        [SerializeField] private GameObject _costumeRoot;
        [SerializeField] private bool _includeInactive = true;
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private string _resultMessage = string.Empty;
        [SerializeField] private string _searchText = string.Empty;

        private MaBlendshapeSyncPlan _plan;
        private string _scanError;
        private string _lowerSearchText = string.Empty;
        private readonly List<int> _visibleGroupIndices = new List<int>();
        private readonly List<int> _sourceDropdownGroupIndices = new List<int>();
        private string[] _sourceDropdownNames = Array.Empty<string>();
        private AdvancedDropdownState _sourceDropdownState = new AdvancedDropdownState();
        private AdvancedDropdownState _costumeDropdownState = new AdvancedDropdownState();
        private MaBlendshapeSyncGroup _costumeDropdownGroup;
        private int _hiddenGroupCount;
        private float _manualLinkLabelWidth;
        private float _configuredLabelWidth;
        private float _sourceWeightLabelWidth;

        internal static void Open()
        {
            var window = GetWindow<MaBlendshapeSyncHelperWindow>(
                false, "01_Helper for MA Blendshape Sync", true);
            window.minSize = new Vector2(620f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_sourceRenderers == null)
            {
                _sourceRenderers = new List<SkinnedMeshRenderer>();
            }

            if (_searchText == null)
            {
                _searchText = string.Empty;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(TitleContent, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(IntroContent.text, MessageType.Info);
            EditorGUILayout.Space();

            DrawSourceRenderers();
            DrawCostumeInputs();

            string blockedReason = GetBlockedReason();
            if (!string.IsNullOrEmpty(blockedReason))
            {
                EditorGUILayout.HelpBox(blockedReason, MessageType.Warning);
            }

            EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(blockedReason));
            bool scanPressed = GUILayout.Button(ScanContent, GUILayout.Height(24f));
            EditorGUI.EndDisabledGroup();
            if (scanPressed)
            {
                _plan = MaBlendshapeSyncHelperScanner.Scan(
                    _sourceRenderers, _costumeRoot, _includeInactive, out _scanError);
                _resultMessage = string.Empty;
                _lowerSearchText = string.IsNullOrEmpty(_searchText)
                    ? string.Empty
                    : _searchText.ToLowerInvariant();
                RebuildVisibleGroupIndices();
            }

            if (!string.IsNullOrEmpty(_scanError))
            {
                EditorGUILayout.HelpBox(_scanError, MessageType.Warning);
            }

            if (_plan != null)
            {
                DrawGroups();
                bool canApply = string.IsNullOrEmpty(blockedReason) &&
                    (_plan.EnabledCount > 0 || _plan.RemovalCount > 0);
                EditorGUI.BeginDisabledGroup(!canApply);
                bool applyPressed = GUILayout.Button(ApplyContent, GUILayout.Height(28f));
                EditorGUI.EndDisabledGroup();
                if (applyPressed)
                {
                    ApplyPlan();
                }
            }

            if (!string.IsNullOrEmpty(_resultMessage))
            {
                EditorGUILayout.HelpBox(_resultMessage, MessageType.Info);
            }
        }

        private void DrawSourceRenderers()
        {
            EditorGUILayout.LabelField(SourceHeaderContent, EditorStyles.boldLabel);
            int removeIndex = -1;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            for (int sourceIndex = 0; sourceIndex < _sourceRenderers.Count; sourceIndex++)
            {
                EditorGUILayout.BeginHorizontal();
                SkinnedMeshRenderer current = _sourceRenderers[sourceIndex];
                SkinnedMeshRenderer next = EditorGUILayout.ObjectField(
                    current, typeof(SkinnedMeshRenderer), true) as SkinnedMeshRenderer;
                if (next != current)
                {
                    if (next != null && ContainsSourceExcept(next, sourceIndex))
                    {
                        Debug.LogWarning(DuplicateSourceWarning);
                    }
                    else
                    {
                        _sourceRenderers[sourceIndex] = next;
                        InvalidatePlan();
                    }
                }

                if (GUILayout.Button(RemoveContent, GUILayout.Width(20f)))
                {
                    removeIndex = sourceIndex;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button(AddContent))
            {
                _sourceRenderers.Add(null);
                InvalidatePlan();
            }
            EditorGUILayout.EndVertical();

            if (removeIndex >= 0)
            {
                _sourceRenderers.RemoveAt(removeIndex);
                InvalidatePlan();
            }
        }

        private void DrawCostumeInputs()
        {
            GameObject nextRoot = EditorGUILayout.ObjectField(
                CostumeRootContent, _costumeRoot, typeof(GameObject), true) as GameObject;
            if (nextRoot != _costumeRoot)
            {
                _costumeRoot = nextRoot;
                InvalidatePlan();
            }

            bool nextIncludeInactive = EditorGUILayout.ToggleLeft(
                IncludeInactiveContent, _includeInactive);
            if (nextIncludeInactive != _includeInactive)
            {
                _includeInactive = nextIncludeInactive;
                InvalidatePlan();
            }
        }

        private string GetBlockedReason()
        {
            if (EditorApplication.isPlaying)
            {
                return PlayingWarning;
            }

            bool hasSource = false;
            bool hasShape = false;
            for (int sourceIndex = 0; sourceIndex < _sourceRenderers.Count; sourceIndex++)
            {
                SkinnedMeshRenderer source = _sourceRenderers[sourceIndex];
                if (source == null)
                {
                    continue;
                }

                hasSource = true;
                if (source.sharedMesh == null)
                {
                    return MissingMeshWarning;
                }

                if (source.sharedMesh.blendShapeCount > 0)
                {
                    hasShape = true;
                }
            }

            if (!hasSource)
            {
                return SourceWarning;
            }

            if (!hasShape)
            {
                return MissingShapeWarning;
            }

            if (_costumeRoot == null)
            {
                return CostumeRootWarning;
            }

            return null;
        }

        private void DrawGroups()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GroupsHeaderContent, EditorStyles.boldLabel);

            if (_manualLinkLabelWidth <= 0f)
            {
                // NOTE: GUI コンテキスト外では計測できないため、最初の描画時だけ求める。
                _manualLinkLabelWidth =
                    EditorStyles.label.CalcSize(ManualLinkContent).x + 8f;
                _configuredLabelWidth =
                    EditorStyles.miniLabel.CalcSize(ConfiguredContent).x + 8f;
            }

            if (_sourceWeightLabelWidth <= 0f)
            {
                _sourceWeightLabelWidth =
                    EditorStyles.label.CalcSize(SourceWeightContent).x + 4f;
            }

            EditorGUILayout.BeginHorizontal();
            string nextSearchText = EditorGUILayout.TextField(SearchContent, _searchText);
            if (!string.Equals(nextSearchText, _searchText, StringComparison.Ordinal))
            {
                _searchText = nextSearchText;
                _lowerSearchText = string.IsNullOrEmpty(_searchText)
                    ? string.Empty
                    : _searchText.ToLowerInvariant();
                RebuildVisibleGroupIndices();
            }

            EditorGUI.BeginDisabledGroup(_hiddenGroupCount == 0);
            bool addSourceShapePressed = GUILayout.Button(AddSourceShapeContent);
            Rect addSourceShapeRect = GUILayoutUtility.GetLastRect();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (addSourceShapePressed)
            {
                ShowSourceShapeDropdown(addSourceShapeRect);
            }

            if (_visibleGroupIndices.Count == 0)
            {
                EditorGUILayout.HelpBox(NoVisibleGroupMessage, MessageType.Info);
            }

            MaBlendshapeSyncGroup pendingGroup = null;
            string pendingShapeName = null;
            _scroll = EditorGUILayout.BeginScrollView(
                _scroll, GUILayout.MinHeight(200f));
            for (int visibleIndex = 0;
                 visibleIndex < _visibleGroupIndices.Count;
                 visibleIndex++)
            {
                int groupIndex = _visibleGroupIndices[visibleIndex];
                MaBlendshapeSyncGroup group = _plan.Groups[groupIndex];
                DrawGroupHeader(group);
                if (!group.Foldout)
                {
                    continue;
                }

                EditorGUI.indentLevel++;
                for (int candidateIndex = 0;
                     candidateIndex < group.Candidates.Count;
                     candidateIndex++)
                {
                    MaBlendshapeSyncCandidate candidate = group.Candidates[candidateIndex];
                    EditorGUILayout.BeginHorizontal();
                    candidate.Enabled = EditorGUILayout.ToggleLeft(
                        candidate.Label, candidate.Enabled);
                    if (candidate.AlreadyConfigured)
                    {
                        EditorGUILayout.LabelField(
                            ConfiguredContent,
                            EditorStyles.miniLabel,
                            GUILayout.Width(_configuredLabelWidth));
                    }
                    EditorGUILayout.EndHorizontal();
                }

                int previousIndentLevel = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16f);
                EditorGUILayout.LabelField(
                    ManualLinkContent, GUILayout.Width(_manualLinkLabelWidth));
                bool hasShapes = _plan.CostumeShapeNames.Length > 0;
                EditorGUI.BeginDisabledGroup(!hasShapes);
                GUIContent selectedShapeContent = group.ManualShapeIndex >= 0 &&
                    group.ManualShapeIndex < _plan.CostumeShapeContents.Length
                        ? _plan.CostumeShapeContents[group.ManualShapeIndex]
                        : UnselectedShapeContent;
                bool costumeDropdownPressed = EditorGUILayout.DropdownButton(
                    selectedShapeContent,
                    FocusType.Keyboard,
                    GUILayout.MinWidth(120f));
                Rect costumeDropdownRect = GUILayoutUtility.GetLastRect();
                EditorGUI.EndDisabledGroup();
                bool canLink = hasShapes && group.ManualShapeIndex >= 0 &&
                    group.ManualShapeIndex < _plan.CostumeShapeNames.Length;
                EditorGUI.BeginDisabledGroup(!canLink);
                bool linkPressed = GUILayout.Button(LinkAllContent, GUILayout.Width(96f));
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel = previousIndentLevel;
                if (costumeDropdownPressed && hasShapes)
                {
                    ShowCostumeShapeDropdown(group, costumeDropdownRect);
                }

                if (linkPressed && canLink)
                {
                    pendingGroup = group;
                    pendingShapeName = _plan.CostumeShapeNames[group.ManualShapeIndex];
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
            if (pendingGroup != null)
            {
                AddManualCandidates(pendingGroup, pendingShapeName);
            }
        }

        private void RebuildVisibleGroupIndices()
        {
            _visibleGroupIndices.Clear();
            _hiddenGroupCount = 0;
            if (_plan == null)
            {
                return;
            }

            for (int groupIndex = 0; groupIndex < _plan.Groups.Count; groupIndex++)
            {
                MaBlendshapeSyncGroup group = _plan.Groups[groupIndex];
                if (!group.IsVisible)
                {
                    _hiddenGroupCount++;
                    continue;
                }

                if (string.IsNullOrEmpty(_lowerSearchText) ||
                    group.SearchName.Contains(_lowerSearchText))
                {
                    _visibleGroupIndices.Add(groupIndex);
                }
            }
        }

        private void ShowSourceShapeDropdown(Rect buttonRect)
        {
            _sourceDropdownGroupIndices.Clear();
            for (int groupIndex = 0; groupIndex < _plan.Groups.Count; groupIndex++)
            {
                if (!_plan.Groups[groupIndex].IsVisible)
                {
                    _sourceDropdownGroupIndices.Add(groupIndex);
                }
            }

            _sourceDropdownNames = new string[_sourceDropdownGroupIndices.Count];
            for (int candidateIndex = 0;
                 candidateIndex < _sourceDropdownGroupIndices.Count;
                 candidateIndex++)
            {
                _sourceDropdownNames[candidateIndex] =
                    _plan.Groups[_sourceDropdownGroupIndices[candidateIndex]].HeaderLabel;
            }

            var dropdown = new MaBlendshapeSyncShapeDropdown(
                _sourceDropdownState,
                SourceDropdownTitle,
                _sourceDropdownNames,
                OnSourceShapeSelected);
            dropdown.Show(buttonRect);
        }

        private void OnSourceShapeSelected(int candidateIndex)
        {
            if (_plan == null || candidateIndex < 0 ||
                candidateIndex >= _sourceDropdownGroupIndices.Count)
            {
                return;
            }

            int groupIndex = _sourceDropdownGroupIndices[candidateIndex];
            if (groupIndex < 0 || groupIndex >= _plan.Groups.Count)
            {
                return;
            }

            MaBlendshapeSyncGroup group = _plan.Groups[groupIndex];
            group.IsVisible = true;
            group.Foldout = true;
            RebuildVisibleGroupIndices();
            Repaint();
        }

        private void ShowCostumeShapeDropdown(
            MaBlendshapeSyncGroup group, Rect buttonRect)
        {
            _costumeDropdownGroup = group;
            var dropdown = new MaBlendshapeSyncShapeDropdown(
                _costumeDropdownState,
                CostumeDropdownTitle,
                _plan.CostumeShapeNames,
                OnCostumeShapeSelected);
            dropdown.Show(buttonRect);
        }

        private void OnCostumeShapeSelected(int shapeIndex)
        {
            if (_plan == null || _costumeDropdownGroup == null || shapeIndex < 0 ||
                shapeIndex >= _plan.CostumeShapeNames.Length)
            {
                return;
            }

            _costumeDropdownGroup.ManualShapeIndex = shapeIndex;
            Repaint();
        }

        private void DrawGroupHeader(MaBlendshapeSyncGroup group)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            group.Foldout = EditorGUILayout.Foldout(
                group.Foldout, group.HeaderLabel, true);

            if (group.SourceRenderer != null && group.SourceRenderer.sharedMesh != null &&
                group.SourceIndex >= 0 &&
                group.SourceIndex < group.SourceRenderer.sharedMesh.blendShapeCount)
            {
                float current = group.SourceRenderer.GetBlendShapeWeight(group.SourceIndex);
                EditorGUILayout.LabelField(
                    SourceWeightContent, GUILayout.Width(_sourceWeightLabelWidth));
                EditorGUI.BeginChangeCheck();
                float next = EditorGUILayout.FloatField(current, GUILayout.Width(60f));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(group.SourceRenderer, "Blendshape Sync Helper");
                    group.SourceRenderer.SetBlendShapeWeight(group.SourceIndex, next);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(group.SourceRenderer);
                }
            }

            if (GUILayout.Button(SelectAllContent, GUILayout.Width(72f)))
            {
                SetGroupEnabled(group, true);
            }

            if (GUILayout.Button(ClearAllContent, GUILayout.Width(72f)))
            {
                SetGroupEnabled(group, false);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void AddManualCandidates(MaBlendshapeSyncGroup group, string shapeName)
        {
            if (!_plan.CostumeRenderersByShapeName.TryGetValue(
                    shapeName, out List<int> rendererIndices))
            {
                Debug.LogWarning("Candy Box: 追加できる対応がありませんでした。");
                return;
            }

            int addedCount = 0;
            for (int index = 0; index < rendererIndices.Count; index++)
            {
                int rendererIndex = rendererIndices[index];
                SkinnedMeshRenderer renderer = _plan.CostumeRenderers[rendererIndex];
                if (ContainsCandidate(group, renderer, shapeName))
                {
                    continue;
                }

                string rendererPath = _plan.CostumeRendererPaths[rendererIndex];
                group.Candidates.Add(new MaBlendshapeSyncCandidate
                {
                    Renderer = renderer,
                    RendererPath = rendererPath,
                    LocalName = shapeName,
                    Label = rendererPath + " : " + shapeName,
                    Enabled = true,
                    AlreadyConfigured = false,
                });
                addedCount++;
            }

            if (addedCount > 0)
            {
                Debug.Log("Candy Box: " + addedCount + " 件の対応を追加しました。");
            }
            else
            {
                Debug.LogWarning("Candy Box: 追加できる対応がありませんでした。");
            }
        }

        private void ApplyPlan()
        {
            string confirmation = string.Format(
                ConfirmFormat, _plan.EnabledCount, _plan.RemovalCount);
            if (!EditorUtility.DisplayDialog(
                    "Candy Box", confirmation, "適用", "キャンセル"))
            {
                return;
            }

            MaBlendshapeSyncApplyResult result =
                MaBlendshapeSyncHelperApplier.Apply(_plan);
            _resultMessage = string.Format(
                ResultFormat,
                result.ConfiguredRenderers,
                result.AddedBindings,
                result.AddedComponents,
                result.RemovedBindings);
            _plan = null;
            _scanError = null;
            GUIUtility.ExitGUI();
        }

        private void InvalidatePlan()
        {
            _plan = null;
            _scanError = null;
            _resultMessage = string.Empty;
            _visibleGroupIndices.Clear();
            _sourceDropdownGroupIndices.Clear();
            _costumeDropdownGroup = null;
            _hiddenGroupCount = 0;
        }

        private bool ContainsSourceExcept(
            SkinnedMeshRenderer renderer, int excludedIndex)
        {
            for (int sourceIndex = 0; sourceIndex < _sourceRenderers.Count; sourceIndex++)
            {
                if (sourceIndex != excludedIndex && _sourceRenderers[sourceIndex] == renderer)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsCandidate(
            MaBlendshapeSyncGroup group,
            SkinnedMeshRenderer renderer,
            string localName)
        {
            for (int candidateIndex = 0; candidateIndex < group.Candidates.Count; candidateIndex++)
            {
                MaBlendshapeSyncCandidate candidate = group.Candidates[candidateIndex];
                if (candidate.Renderer == renderer && string.Equals(
                        candidate.LocalName, localName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetGroupEnabled(MaBlendshapeSyncGroup group, bool enabled)
        {
            for (int candidateIndex = 0; candidateIndex < group.Candidates.Count; candidateIndex++)
            {
                group.Candidates[candidateIndex].Enabled = enabled;
            }
        }
    }
}
