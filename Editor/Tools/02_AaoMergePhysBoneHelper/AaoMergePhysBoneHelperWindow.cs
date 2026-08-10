using System;
using Anatawa12.AvatarOptimizer;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.AaoMergePhysBoneHelper.Editor
{
    internal sealed class AaoMergePhysBoneHelperWindow : EditorWindow
    {
        private static readonly GUIContent TitleContent =
            new GUIContent("02_Helper for AAO Merge PhysBone");
        private static readonly GUIContent IntroContent =
            new GUIContent("統合する PhysBone の値を比べ、override する値を提案します。");
        private static readonly GUIContent TargetContent = new GUIContent("対象");
        private static readonly GUIContent SelectContent = new GUIContent("Inspector で開く");
        private static readonly GUIContent ScanContent = new GUIContent("差分を確認");
        private static readonly GUIContent ApplyContent = new GUIContent("適用");
        private static readonly GUIContent SourcesHeaderContent = new GUIContent("統合対象");
        private static readonly GUIContent DifferingHeaderContent = new GUIContent("差異あり");
        private static readonly GUIContent BlockedHeaderContent = new GUIContent("統合不可");
        private static readonly GUIContent IdenticalHeaderContent = new GUIContent("差異なし");
        private static readonly GUIContent MetricContent = new GUIContent("指標");
        private static readonly GUIContent EditedSuggestionContent =
            new GUIContent("提案値を手で変更しています。指標を切り替えても、この値は保たれます。");
        private static readonly GUIContent RestoreStatisticsContent =
            new GUIContent("統計値に戻す");
        private static readonly GUIContent SuggestionContent = new GUIContent("提案値");
        private static readonly GUIContent CurveContent = new GUIContent("カーブ");
        private static readonly GUIContent CurveXContent = new GUIContent("カーブ X");
        private static readonly GUIContent CurveYContent = new GUIContent("カーブ Y");
        private static readonly GUIContent CurveZContent = new GUIContent("カーブ Z");
        private static readonly GUIContent NoCurveContent =
            new GUIContent("カーブなし（全体で同じ倍率）");
        private static readonly GUIContent SelectAllContent = new GUIContent("すべて選択");
        private static readonly GUIContent DeselectAllContent = new GUIContent("すべて解除");
        private static readonly GUIContent SelfContent = new GUIContent("Self");
        private static readonly GUIContent OthersContent = new GUIContent("Others");
        private static readonly Rect CurveRange = new Rect(0f, 0f, 1f, 1f);
        private static readonly Color CurveColor = Color.green;

        private const string PlayingWarning =
            "再生中は実行できません。再生を停止してください。";
        private const string TargetWarning =
            "AAO Merge PhysBone が付いたオブジェクトを指定してください。";
        private const string MissingComponentWarning =
            "指定したオブジェクトに AAO Merge PhysBone がありません。";
        private const string SourceCountWarning =
            "統合対象の PhysBone が 2 つ以上必要です。";
        private const string MissingReferenceWarning =
            "統合対象に失われた参照があります。AAO Merge PhysBone のインスペクターで確認してください。";
        private const string ChainLengthWarning =
            "統合対象でチェーンの長さが異なります。カーブの提案は目安として扱ってください。";

        [SerializeField] private GameObject _target;
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private string _resultSummary;

        private AaoMergePhysBoneHelperPlan _plan;
        private string _blockedReason;
        private bool _lastPlaying;

        internal static void Open()
        {
            var window = GetWindow<AaoMergePhysBoneHelperWindow>(
                false, "02_Helper for AAO Merge PhysBone", true);
            window.minSize = new Vector2(640f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_resultSummary == null)
            {
                _resultSummary = string.Empty;
            }

            _lastPlaying = EditorApplication.isPlaying;
            RebuildBlockedReason();
        }

        private void OnGUI()
        {
            bool isPlaying = EditorApplication.isPlaying;
            if (isPlaying != _lastPlaying)
            {
                _lastPlaying = isPlaying;
                RebuildBlockedReason();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField(TitleContent, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(IntroContent.text, MessageType.Info);
            EditorGUILayout.Space();

            DrawTarget();
            string blockedReason = _blockedReason;
            if (!string.IsNullOrEmpty(blockedReason))
            {
                EditorGUILayout.HelpBox(blockedReason, MessageType.Warning);
            }

            EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(blockedReason));
            bool scanPressed = GUILayout.Button(ScanContent, GUILayout.Height(24f));
            EditorGUI.EndDisabledGroup();
            if (scanPressed)
            {
                MergePhysBone mergePhysBone = _target.GetComponent<MergePhysBone>();
                _plan = AaoMergePhysBoneHelperScanner.Scan(mergePhysBone);
                _resultSummary = string.Empty;
                RebuildBlockedReason();
            }

            if (_plan != null)
            {
                DrawPlan(blockedReason);
            }

            if (!string.IsNullOrEmpty(_resultSummary))
            {
                EditorGUILayout.HelpBox(_resultSummary, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTarget()
        {
            EditorGUILayout.BeginHorizontal();
            GameObject nextTarget = EditorGUILayout.ObjectField(
                TargetContent, _target, typeof(GameObject), true) as GameObject;
            if (nextTarget != _target)
            {
                _target = nextTarget;
                _plan = null;
                _resultSummary = string.Empty;
                RebuildBlockedReason();
            }

            EditorGUI.BeginDisabledGroup(_target == null);
            bool selectPressed = GUILayout.Button(SelectContent);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (selectPressed)
            {
                Selection.activeGameObject = _target;
            }
        }

        private void RebuildBlockedReason()
        {
            if (_lastPlaying)
            {
                _blockedReason = PlayingWarning;
                return;
            }

            if (_target == null)
            {
                _blockedReason = TargetWarning;
                return;
            }

            MergePhysBone mergePhysBone = _target.GetComponent<MergePhysBone>();
            if (mergePhysBone == null)
            {
                _blockedReason = MissingComponentWarning;
                return;
            }

            int sourceCount = 0;
            bool hasMissingReference = false;
            foreach (VRC.Dynamics.VRCPhysBoneBase physBone in mergePhysBone.PhysBones)
            {
                sourceCount++;
                hasMissingReference |= physBone == null;
            }

            if (sourceCount < 2)
            {
                _blockedReason = SourceCountWarning;
                return;
            }

            if (hasMissingReference)
            {
                _blockedReason = MissingReferenceWarning;
                return;
            }

            _blockedReason = null;
        }

        private void DrawPlan(string blockedReason)
        {
            EditorGUILayout.Space();
            DrawSources();
            if (_plan.ChainLengthDiffers)
            {
                EditorGUILayout.HelpBox(ChainLengthWarning, MessageType.Warning);
            }

            if (!string.IsNullOrEmpty(_plan.MissingPropertyText))
            {
                EditorGUILayout.HelpBox(_plan.MissingPropertyText, MessageType.Info);
            }

            DrawDiffering();
            DrawBlocked();
            DrawIdentical();

            int selectedCount = GetSelectedCount();
            EditorGUI.BeginDisabledGroup(
                !string.IsNullOrEmpty(blockedReason) || selectedCount == 0);
            bool applyPressed = GUILayout.Button(_plan.ApplyText, GUILayout.Height(28f));
            EditorGUI.EndDisabledGroup();
            if (applyPressed)
            {
                AaoMergePhysBoneHelperPlan previousPlan = _plan;
                int appliedCount = AaoMergePhysBoneHelperApplier.Apply(_plan);
                RebuildBlockedReason();
                RescanAfterApply(previousPlan);
                _resultSummary = string.Format(
                    "{0} 件のプロパティを override しました。統合対象の値そのものは変わらないため、一覧には引き続き差異が表示されます。",
                    appliedCount);
            }
        }

        private void RescanAfterApply(AaoMergePhysBoneHelperPlan previousPlan)
        {
            _plan = null;
            try
            {
                if (_target == null)
                {
                    return;
                }

                MergePhysBone mergePhysBone = _target.GetComponent<MergePhysBone>();
                if (mergePhysBone == null)
                {
                    return;
                }

                AaoMergePhysBoneHelperPlan nextPlan =
                    AaoMergePhysBoneHelperScanner.Scan(mergePhysBone);
                AaoMergePhysBoneHelperScanner.CarryOverViewState(
                    previousPlan, nextPlan);
                _plan = nextPlan;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Candy Box: 適用後の AAO Merge PhysBone 再確認に失敗しました。\n" +
                    exception,
                    _target);
            }
        }

        private void DrawSources()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(_plan.SourcesHeaderText, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            for (int sourceIndex = 0; sourceIndex < _plan.Sources.Count; sourceIndex++)
            {
                AaoMergePhysBoneSource source = _plan.Sources[sourceIndex];
                EditorGUILayout.BeginHorizontal();
                DrawSelectableLabel(source.LabelContent, source.GameObject);
                GUILayout.Space(8f);
                GUILayout.Label(
                    source.ChainLengthText, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private void DrawDiffering()
        {
            EditorGUILayout.BeginHorizontal();
            _plan.DifferingExpanded = EditorGUILayout.Foldout(
                _plan.DifferingExpanded, _plan.DifferingHeaderText, true);
            if (GUILayout.Button(SelectAllContent))
            {
                SetAllSelected(true);
            }

            if (GUILayout.Button(DeselectAllContent))
            {
                SetAllSelected(false);
            }

            EditorGUILayout.EndHorizontal();
            if (!_plan.DifferingExpanded)
            {
                return;
            }

            for (int propertyIndex = 0; propertyIndex < _plan.Differing.Count; propertyIndex++)
            {
                DrawDifferingProperty(_plan.Differing[propertyIndex]);
            }
        }

        private void DrawDifferingProperty(AaoMergePhysBonePropertyPlan plan)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            bool selected = GUILayout.Toggle(
                plan.Selected, GUIContent.none, GUILayout.Width(18f));
            if (selected != plan.Selected)
            {
                plan.Selected = selected;
                AaoMergePhysBoneHelperScanner.RefreshPlanDisplayText(_plan);
            }

            plan.Expanded = EditorGUILayout.Foldout(plan.Expanded, plan.HeaderText, true);
            EditorGUILayout.EndHorizontal();
            if (plan.Expanded)
            {
                EditorGUI.indentLevel++;
                for (int valueIndex = 0; valueIndex < plan.Values.Count; valueIndex++)
                {
                    AaoMergePhysBoneValue value = plan.Values[valueIndex];
                    AaoMergePhysBoneSource source =
                        _plan.Sources[value.SourceIndex];
                    EditorGUILayout.BeginHorizontal();
                    DrawSelectableLabel(source.LabelContent, source.GameObject);
                    GUILayout.Space(8f);
                    GUILayout.Label(
                        value.DisplayText, GUILayout.ExpandWidth(false));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }

                if (!string.IsNullOrEmpty(plan.OutlierText))
                {
                    EditorGUILayout.HelpBox(plan.OutlierText, MessageType.Info);
                }

                if (!string.IsNullOrEmpty(plan.StatisticsText))
                {
                    EditorGUILayout.LabelField(plan.StatisticsText, EditorStyles.wordWrappedMiniLabel);
                }

                DrawMetric(plan);
                DrawSuggestion(plan);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawSelectableLabel(
            GUIContent content, GameObject target)
        {
            bool clicked = GUILayout.Button(
                content, EditorStyles.label, GUILayout.ExpandWidth(false));
            if (target != null)
            {
                EditorGUIUtility.AddCursorRect(
                    GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }

            if (clicked && target != null)
            {
                Selection.activeGameObject = target;
            }
        }

        private void DrawMetric(AaoMergePhysBonePropertyPlan plan)
        {
            AaoMergePhysBoneMetric[] metrics =
                AaoMergePhysBoneStatistics.GetAvailableMetrics(plan.Property.Kind);
            string[] names =
                AaoMergePhysBoneStatistics.GetAvailableMetricNames(plan.Property.Kind);
            int currentIndex = 0;
            if (metrics.Length > 1 && plan.Suggestion != null)
            {
                currentIndex = (int)plan.Suggestion.Metric;
            }

            EditorGUI.BeginChangeCheck();
            int nextIndex = EditorGUILayout.Popup(MetricContent, currentIndex, names);
            if (EditorGUI.EndChangeCheck() && nextIndex >= 0 && nextIndex < metrics.Length)
            {
                AaoMergePhysBoneMetric nextMetric = metrics[nextIndex];
                if (plan.Edited && plan.Suggestion != null)
                {
                    plan.Suggestion.Metric = nextMetric;
                }
                else
                {
                    AaoMergePhysBoneStatistics.Recompute(plan, nextMetric);
                    AaoMergePhysBoneHelperScanner.RefreshPlanDisplayText(_plan);
                }
            }

            if (plan.Edited)
            {
                EditorGUILayout.HelpBox(
                    EditedSuggestionContent.text, MessageType.Info);
                if (GUILayout.Button(RestoreStatisticsContent))
                {
                    AaoMergePhysBoneMetric metric = plan.Suggestion != null
                        ? plan.Suggestion.Metric
                        : AaoMergePhysBoneMetric.Mode;
                    plan.Edited = false;
                    AaoMergePhysBoneStatistics.Recompute(plan, metric);
                    AaoMergePhysBoneHelperScanner.RefreshPlanDisplayText(_plan);
                }
            }
        }

        private void DrawSuggestion(AaoMergePhysBonePropertyPlan plan)
        {
            if (plan.Suggestion == null)
            {
                EditorGUILayout.HelpBox(plan.BlockedReason, MessageType.Warning);
                return;
            }

            AaoMergePhysBoneSuggestion suggestion = plan.Suggestion;
            EditorGUI.BeginChangeCheck();
            switch (plan.Property.Kind)
            {
                case AaoMergePhysBoneValueKind.Float:
                    suggestion.Float = plan.Property.HasRange
                        ? EditorGUILayout.Slider(
                            SuggestionContent,
                            suggestion.Float,
                            plan.Property.RangeMin,
                            plan.Property.RangeMax)
                        : EditorGUILayout.FloatField(SuggestionContent, suggestion.Float);
                    break;
                case AaoMergePhysBoneValueKind.Vector3:
                    suggestion.Vector = EditorGUILayout.Vector3Field(
                        SuggestionContent, suggestion.Vector);
                    break;
                case AaoMergePhysBoneValueKind.Bool:
                    suggestion.Int = EditorGUILayout.Toggle(
                        SuggestionContent, suggestion.Int != 0) ? 1 : 0;
                    break;
                case AaoMergePhysBoneValueKind.Enum:
                    suggestion.Int = EditorGUILayout.Popup(
                        SuggestionContent, suggestion.Int, plan.EnumDisplayNames);
                    break;
                case AaoMergePhysBoneValueKind.Permission:
                    DrawPermissionSuggestion(plan);
                    break;
            }

            bool changed = EditorGUI.EndChangeCheck();
            if (changed)
            {
                if (!suggestion.NormalizePending)
                {
                    plan.Edited = true;
                }

                AaoMergePhysBoneStatistics.RefreshDisplayText(plan);
            }
            else
            {
                suggestion.NormalizePending = false;
            }

            DrawCurves(plan);
            if (!string.IsNullOrEmpty(suggestion.Warning))
            {
                EditorGUILayout.HelpBox(suggestion.Warning, MessageType.Warning);
            }
        }

        private static void DrawPermissionSuggestion(AaoMergePhysBonePropertyPlan plan)
        {
            AaoMergePhysBoneSuggestion suggestion = plan.Suggestion;
            suggestion.Int = EditorGUILayout.Popup(
                SuggestionContent, suggestion.Int, plan.EnumDisplayNames);
            if (!IsFiltered(plan, suggestion.Int))
            {
                return;
            }

            bool allowSelf = (suggestion.FilterInt & 1) != 0;
            bool allowOthers = (suggestion.FilterInt & 2) != 0;
            allowSelf = EditorGUILayout.Toggle(SelfContent, allowSelf);
            allowOthers = EditorGUILayout.Toggle(OthersContent, allowOthers);
            suggestion.FilterInt = (allowSelf ? 1 : 0) | (allowOthers ? 2 : 0);
        }

        private static bool IsFiltered(AaoMergePhysBonePropertyPlan plan, int index)
        {
            return plan.EnumNames != null && index >= 0 && index < plan.EnumNames.Length &&
                (string.Equals(plan.EnumNames[index], "Filtered", StringComparison.Ordinal) ||
                 string.Equals(plan.EnumNames[index], "Other", StringComparison.Ordinal));
        }

        private static void DrawCurves(AaoMergePhysBonePropertyPlan plan)
        {
            if (plan.Property.CurveFieldName == null)
            {
                return;
            }

            AaoMergePhysBoneSuggestion suggestion = plan.Suggestion;
            if (plan.Property.Kind == AaoMergePhysBoneValueKind.Vector3)
            {
                DrawReadOnlyCurve(CurveXContent, suggestion.Curve);
                DrawReadOnlyCurve(CurveYContent, suggestion.CurveY);
                DrawReadOnlyCurve(CurveZContent, suggestion.CurveZ);
                return;
            }

            if (suggestion.Curve == null || suggestion.Curve.length == 0)
            {
                EditorGUILayout.LabelField(CurveContent, NoCurveContent);
                return;
            }

            DrawReadOnlyCurve(CurveContent, suggestion.Curve);
        }

        private static void DrawReadOnlyCurve(GUIContent label, AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
            {
                EditorGUILayout.LabelField(label, NoCurveContent);
                return;
            }

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.CurveField(label, curve, CurveColor, CurveRange);
            EditorGUI.EndDisabledGroup();
        }

        private void DrawBlocked()
        {
            if (_plan.Blocked.Count == 0)
            {
                return;
            }

            _plan.BlockedExpanded = EditorGUILayout.Foldout(
                _plan.BlockedExpanded, _plan.BlockedHeaderText, true);
            if (!_plan.BlockedExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            for (int propertyIndex = 0; propertyIndex < _plan.Blocked.Count; propertyIndex++)
            {
                AaoMergePhysBonePropertyPlan plan = _plan.Blocked[propertyIndex];
                EditorGUILayout.HelpBox(plan.BlockedDisplayText, MessageType.Warning);
            }

            EditorGUI.indentLevel--;
        }

        private void DrawIdentical()
        {
            _plan.IdenticalExpanded = EditorGUILayout.Foldout(
                _plan.IdenticalExpanded, _plan.IdenticalHeaderText, true);
            if (!_plan.IdenticalExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            for (int propertyIndex = 0; propertyIndex < _plan.Identical.Count; propertyIndex++)
            {
                EditorGUILayout.LabelField(
                    _plan.Identical[propertyIndex].Property.DisplayName);
            }

            EditorGUI.indentLevel--;
        }

        private void SetAllSelected(bool selected)
        {
            for (int propertyIndex = 0; propertyIndex < _plan.Differing.Count; propertyIndex++)
            {
                AaoMergePhysBonePropertyPlan plan = _plan.Differing[propertyIndex];
                plan.Selected = selected && !plan.Blocked;
            }

            AaoMergePhysBoneHelperScanner.RefreshPlanDisplayText(_plan);
        }

        private int GetSelectedCount()
        {
            int count = 0;
            for (int propertyIndex = 0; propertyIndex < _plan.Differing.Count; propertyIndex++)
            {
                AaoMergePhysBonePropertyPlan plan = _plan.Differing[propertyIndex];
                if (plan.Selected && !plan.Blocked)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
