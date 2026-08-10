using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Poyo.CandyBox.AaoMergeBoneHelper.Editor
{
    internal sealed class AaoMergeBoneHelperWindow : EditorWindow
    {
        private static readonly GUIContent TitleContent =
            new GUIContent("03_Helper for AAO Merge Bone");
        private static readonly GUIContent IntroContent =
            new GUIContent("配下のボーンを一覧し、親へ統合するかをまとめて切り替えます。");
        private static readonly GUIContent TargetContent = new GUIContent("対象");
        private static readonly GUIContent AvatarRootContent = new GUIContent("アバタールート");
        private static readonly GUIContent ScanContent = new GUIContent("ボーンを確認");
        private static readonly GUIContent ApplyContent = new GUIContent("適用");
        private static readonly GUIContent FilterContent = new GUIContent("絞り込み");
        private static readonly GUIContent CheckDescendantsContent = new GUIContent(
            "子✓", "このボーンと配下すべてにチェックを入れます");
        private static readonly GUIContent UncheckDescendantsContent = new GUIContent(
            "子□", "このボーンと配下すべてのチェックを外します");
        private static readonly GUIContent TreeHeaderContent = new GUIContent("ボーン");
        private static readonly GUIContent ChainHeaderContent = new GUIContent("チェーンを間引く");
        private static readonly GUIContent ChainStartContent = new GUIContent("始点ボーン");
        private static readonly GUIContent ChainSelectContent = new GUIContent("ボーンから選ぶ");
        private static readonly GUIContent ChainClearContent = new GUIContent("解除");
        private static readonly GUIContent ChainStartMarkerContent = new GUIContent(
            "始点", "このボーンをチェーンの始点にします");
        private static readonly GUIContent NoChainStartContent = new GUIContent("未選択");
        private static readonly GUIContent KeepIntervalContent = new GUIContent("保持間隔");
        private static readonly GUIContent ChainApplyContent = new GUIContent("チェックに反映");

        private const string PlayingWarning =
            "再生中は実行できません。再生を停止してください。";
        private const string MissingTypeWarning =
            "AAO Merge Bone のコンポーネントが見つかりません。AAO: Avatar Optimizer のバージョンを確認してください。";
        private const string TargetWarning = "対象のオブジェクトを指定してください。";
        private const string NoMergeableWarning = "統合できるボーンがありません。";
        private const string MissingAvatarRootMessage =
            "アバタールートが見つかりません。人型ボーンの判定とアニメーションの検出を行いません。";
        private const string AnimationScannedMessage =
            "アニメーションの確認は、アバターの Animator から辿れるものだけが対象です。ビルド時に追加されるアニメーションは含まれません。";
        private const string AnimationNotScannedMessage =
            "アバタールートが見つからないため、アニメーションの確認を行っていません。";
        private const string NoFilterMatchesMessage = "一致するボーンがありません。";
        private const string ChainHelpMessage =
            "保持間隔 2 で 1 つおき、3 で 2 つおきに統合します。始点は必ず残ります。";

        private const float DepthWidth = 14f;
        private const float FoldoutWidth = 14f;
        private const float CheckboxWidth = 18f;

        private static bool _widthsMeasured;
        private static float _chainStartButtonWidth;
        private static float _checkDescendantsButtonWidth;
        private static float _uncheckDescendantsButtonWidth;

        [SerializeField] private GameObject _target;
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private string _filterText = string.Empty;
        [SerializeField] private Transform _chainStart;
        [SerializeField] private int _keepInterval = 2;
        [SerializeField] private string _chainResult = string.Empty;
        [SerializeField] private string _resultSummary = string.Empty;

        private GameObject _avatarRoot;
        private AaoMergeBoneHelperPlan _plan;
        private AaoMergeBoneNode _chainStartNode;
        private AdvancedDropdownState _chainDropdownState;
        private AaoMergeBoneStartDropdown _chainDropdown;

        internal static void Open()
        {
            var window = GetWindow<AaoMergeBoneHelperWindow>(
                false, "03_Helper for AAO Merge Bone", true);
            window.minSize = new Vector2(560f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_filterText == null)
            {
                _filterText = string.Empty;
            }

            if (_chainResult == null)
            {
                _chainResult = string.Empty;
            }

            if (_resultSummary == null)
            {
                _resultSummary = string.Empty;
            }

            _chainDropdownState = new AdvancedDropdownState();
            RefreshAvatarRoot();
        }

        private void OnGUI()
        {
            MeasureWidths();
            EditorGUILayout.LabelField(TitleContent, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(IntroContent.text, MessageType.Info);
            EditorGUILayout.Space();

            DrawTarget();
            if (_target != null && _avatarRoot == null)
            {
                EditorGUILayout.HelpBox(MissingAvatarRootMessage, MessageType.Info);
            }

            if (_plan != null)
            {
                EditorGUILayout.HelpBox(
                    _plan.AnimationScanned
                        ? AnimationScannedMessage
                        : AnimationNotScannedMessage,
                    MessageType.Info);
            }

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
                _plan = AaoMergeBoneHelperScanner.Scan(_target, _avatarRoot);
                _resultSummary = string.Empty;
                _chainResult = string.Empty;
                ResolveChainStartNode();
                RebuildChainDropdown();
                RebuildFilter();
            }

            if (_plan != null)
            {
                DrawPlan(blockedReason);
            }

            if (!string.IsNullOrEmpty(_resultSummary))
            {
                EditorGUILayout.HelpBox(_resultSummary, MessageType.Info);
            }
        }

        private void DrawTarget()
        {
            GameObject nextTarget = EditorGUILayout.ObjectField(
                TargetContent, _target, typeof(GameObject), true) as GameObject;
            if (nextTarget != _target)
            {
                _target = nextTarget;
                _plan = null;
                _chainStart = null;
                _chainStartNode = null;
                _chainDropdown = null;
                _chainResult = string.Empty;
                _resultSummary = string.Empty;
                RefreshAvatarRoot();
            }

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(
                AvatarRootContent, _avatarRoot, typeof(GameObject), true);
            EditorGUI.EndDisabledGroup();
        }

        private string GetBlockedReason()
        {
            if (EditorApplication.isPlaying)
            {
                return PlayingWarning;
            }

            if (!AaoMergeBoneType.IsAvailable)
            {
                return MissingTypeWarning;
            }

            if (_target == null)
            {
                return TargetWarning;
            }

            if (_plan != null && _plan.MergeableCount == 0)
            {
                return NoMergeableWarning;
            }

            return null;
        }

        private void DrawPlan(string blockedReason)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(_plan.CountText, EditorStyles.boldLabel);
            string nextFilter = EditorGUILayout.TextField(FilterContent, _filterText);
            if (!string.Equals(nextFilter, _filterText, StringComparison.Ordinal))
            {
                _filterText = nextFilter;
                RebuildFilter();
            }

            EditorGUILayout.LabelField(TreeHeaderContent, EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(
                _scroll, EditorStyles.helpBox, GUILayout.MinHeight(180f));
            if (_plan.IsFiltering && _plan.FilterMatchCount == 0)
            {
                EditorGUILayout.HelpBox(NoFilterMatchesMessage, MessageType.Info);
            }
            else
            {
                DrawNode(_plan.Root);
            }

            EditorGUILayout.EndScrollView();
            DrawChainPlanner();

            EditorGUILayout.LabelField(_plan.SummaryText, EditorStyles.boldLabel);
            bool canApply = string.IsNullOrEmpty(blockedReason) &&
                _plan.AddCount + _plan.RemoveCount > 0;
            EditorGUI.BeginDisabledGroup(!canApply);
            bool applyPressed = GUILayout.Button(_plan.ApplyText, GUILayout.Height(28f));
            EditorGUI.EndDisabledGroup();
            if (applyPressed)
            {
                AaoMergeBoneHelperPlan previousPlan = _plan;
                AaoMergeBoneHelperApplier.Apply(
                    _plan, out int added, out int removed);
                _chainResult = string.Empty;
                RescanAfterApply(previousPlan);
                _resultSummary = string.Format(
                    "{0} 件のボーンに設定を追加し、{1} 件から削除しました。",
                    added,
                    removed);
            }
        }

        private void DrawNode(AaoMergeBoneNode node)
        {
            if (node == null || !node.MatchesFilter)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(node.Depth * DepthWidth);
            if (node.Children.Count > 0)
            {
                node.Expanded = GUILayout.Toggle(
                    node.Expanded,
                    GUIContent.none,
                    EditorStyles.foldout,
                    GUILayout.Width(FoldoutWidth));
            }
            else
            {
                GUILayout.Space(FoldoutWidth);
            }

            EditorGUI.BeginDisabledGroup(
                node.BlockReason != AaoMergeBoneBlockReason.None);
            bool nextChecked = GUILayout.Toggle(
                node.Checked, GUIContent.none, GUILayout.Width(CheckboxWidth));
            EditorGUI.EndDisabledGroup();
            if (nextChecked != node.Checked &&
                node.BlockReason == AaoMergeBoneBlockReason.None)
            {
                node.Checked = nextChecked;
                AaoMergeBoneHelperScanner.RefreshAfterCheckChange(_plan, node);
            }

            bool selectNode = GUILayout.Button(
                node.LabelContent,
                EditorStyles.label,
                GUILayout.ExpandWidth(false));
            if (node.Transform != null)
            {
                EditorGUIUtility.AddCursorRect(
                    GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }

            if (!string.IsNullOrEmpty(node.StatusText))
            {
                GUILayout.Space(8f);
                GUILayout.Label(
                    node.StatusText,
                    EditorStyles.miniLabel,
                    GUILayout.MinWidth(0f));
            }

            GUILayout.FlexibleSpace();
            Rect chainStartRect = GUILayoutUtility.GetRect(
                _chainStartButtonWidth,
                EditorGUIUtility.singleLineHeight,
                GUILayout.Width(_chainStartButtonWidth));
            bool chainStartSelected = GUI.Toggle(
                chainStartRect,
                ReferenceEquals(node, _chainStartNode),
                ChainStartMarkerContent,
                EditorStyles.miniButton);
            bool checkDescendants = false;
            bool uncheckDescendants = false;
            if (node.Children.Count > 0)
            {
                checkDescendants = GUILayout.Button(
                    CheckDescendantsContent,
                    EditorStyles.miniButton,
                    GUILayout.Width(_checkDescendantsButtonWidth));
                uncheckDescendants = GUILayout.Button(
                    UncheckDescendantsContent,
                    EditorStyles.miniButton,
                    GUILayout.Width(_uncheckDescendantsButtonWidth));
            }

            EditorGUILayout.EndHorizontal();
            if (selectNode && node.Transform != null)
            {
                Selection.activeGameObject = node.Transform.gameObject;
            }

            if (chainStartSelected && !ReferenceEquals(node, _chainStartNode))
            {
                SetChainStart(node);
            }

            if (checkDescendants)
            {
                SetSubtreeChecked(node, true);
            }

            if (uncheckDescendants)
            {
                SetSubtreeChecked(node, false);
            }

            if ((_plan.IsFiltering || node.Expanded) && node.Children.Count > 0)
            {
                for (int childIndex = 0; childIndex < node.Children.Count; childIndex++)
                {
                    DrawNode(node.Children[childIndex]);
                }
            }
        }

        private void SetSubtreeChecked(AaoMergeBoneNode node, bool value)
        {
            SetSubtreeCheckedRecursive(node, value);
            AaoMergeBoneHelperScanner.RefreshAllDynamicState(_plan);
        }

        private static void MeasureWidths()
        {
            if (_widthsMeasured)
            {
                return;
            }

            _chainStartButtonWidth =
                EditorStyles.miniButton.CalcSize(ChainStartMarkerContent).x;
            _checkDescendantsButtonWidth =
                EditorStyles.miniButton.CalcSize(CheckDescendantsContent).x;
            _uncheckDescendantsButtonWidth =
                EditorStyles.miniButton.CalcSize(UncheckDescendantsContent).x;
            _widthsMeasured = true;
        }

        private static void SetSubtreeCheckedRecursive(
            AaoMergeBoneNode node, bool value)
        {
            node.Checked = value &&
                node.BlockReason == AaoMergeBoneBlockReason.None;

            for (int childIndex = 0; childIndex < node.Children.Count; childIndex++)
            {
                SetSubtreeCheckedRecursive(node.Children[childIndex], value);
            }
        }

        private void DrawChainPlanner()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(ChainHeaderContent, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(ChainStartContent);
            EditorGUILayout.LabelField(
                _chainStartNode != null ? _chainStartNode.Label : NoChainStartContent.text);
            EditorGUI.BeginDisabledGroup(_chainStartNode == null);
            bool clearPressed = GUILayout.Button(ChainClearContent);
            EditorGUI.EndDisabledGroup();
            bool selectPressed = GUILayout.Button(ChainSelectContent);
            Rect selectRect = GUILayoutUtility.GetLastRect();
            EditorGUILayout.EndHorizontal();
            if (clearPressed)
            {
                SetChainStart(null);
            }

            if (selectPressed && _chainDropdown != null)
            {
                _chainDropdown.Show(selectRect);
            }

            _keepInterval = Mathf.Max(
                2, EditorGUILayout.IntField(KeepIntervalContent, _keepInterval));
            EditorGUILayout.LabelField(
                ChainHelpMessage, EditorStyles.wordWrappedMiniLabel);

            bool canApply = _chainStartNode != null;
            EditorGUI.BeginDisabledGroup(!canApply);
            bool applyPressed = GUILayout.Button(ChainApplyContent);
            EditorGUI.EndDisabledGroup();
            if (applyPressed)
            {
                List<AaoMergeBoneNode> chain = AaoMergeBoneChainPlanner.CollectChain(
                    _chainStartNode, out string note);
                int checkedCount = AaoMergeBoneChainPlanner.Apply(
                    chain, _keepInterval, out int skipped);
                AaoMergeBoneHelperScanner.RefreshAllDynamicState(_plan);
                _chainResult = string.Format(
                    "チェーン {0} 件のうち {1} 件にチェックを入れました。",
                    chain.Count,
                    checkedCount);
                if (skipped > 0)
                {
                    _chainResult += string.Format(
                        " 統合できないボーンを {0} 件飛ばしました。",
                        skipped);
                }

                if (!string.IsNullOrEmpty(note))
                {
                    _chainResult += " " + note;
                }
            }

            if (!string.IsNullOrEmpty(_chainResult))
            {
                EditorGUILayout.HelpBox(_chainResult, MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void RebuildFilter()
        {
            if (_plan == null)
            {
                return;
            }

            bool isFiltering = !string.IsNullOrWhiteSpace(_filterText);
            _plan.IsFiltering = isFiltering;
            _plan.FilterMatchCount = 0;
            for (int nodeIndex = 0; nodeIndex < _plan.AllNodes.Count; nodeIndex++)
            {
                _plan.AllNodes[nodeIndex].MatchesFilter = !isFiltering;
            }

            if (!isFiltering)
            {
                return;
            }

            for (int nodeIndex = 0; nodeIndex < _plan.AllNodes.Count; nodeIndex++)
            {
                AaoMergeBoneNode node = _plan.AllNodes[nodeIndex];
                if (node.Label.IndexOf(
                        _filterText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                _plan.FilterMatchCount++;
                AaoMergeBoneNode current = node;
                while (current != null)
                {
                    current.MatchesFilter = true;
                    current = current.Parent;
                }
            }
        }

        private void RebuildChainDropdown()
        {
            _chainDropdown = _plan != null
                ? new AaoMergeBoneStartDropdown(
                    _chainDropdownState,
                    "始点ボーン",
                    _plan.StartChoicePaths,
                    SelectChainStartByIndex)
                : null;
        }

        private void SelectChainStartByIndex(int index)
        {
            if (_plan == null || index < 0 || index >= _plan.AllNodes.Count)
            {
                return;
            }

            SetChainStart(_plan.AllNodes[index]);
        }

        private void SetChainStart(AaoMergeBoneNode node)
        {
            _chainStartNode = node;
            _chainStart = node != null ? node.Transform : null;
            _chainResult = string.Empty;
            Repaint();
        }

        private void ResolveChainStartNode()
        {
            _chainStartNode = AaoMergeBoneHelperScanner.FindNode(_plan, _chainStart);
            if (_chainStartNode == null)
            {
                _chainStart = null;
            }
        }

        private void RescanAfterApply(AaoMergeBoneHelperPlan previousPlan)
        {
            _plan = null;
            _chainStartNode = null;
            _chainDropdown = null;
            try
            {
                if (_target == null)
                {
                    return;
                }

                RefreshAvatarRoot();
                AaoMergeBoneHelperPlan nextPlan =
                    AaoMergeBoneHelperScanner.Scan(_target, _avatarRoot);
                AaoMergeBoneHelperScanner.CarryOverViewState(previousPlan, nextPlan);
                _plan = nextPlan;
                ResolveChainStartNode();
                RebuildChainDropdown();
                RebuildFilter();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Candy Box: 適用後の AAO Merge Bone 再確認に失敗しました。\n" +
                    exception,
                    _target);
            }
        }

        private void RefreshAvatarRoot()
        {
            Animator animator = _target != null
                ? _target.GetComponentInParent<Animator>()
                : null;
            _avatarRoot = animator != null ? animator.gameObject : null;
        }
    }
}
