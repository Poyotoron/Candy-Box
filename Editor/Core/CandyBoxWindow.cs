using System;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.Editor
{
    internal sealed class CandyBoxWindow : EditorWindow
    {
        private static readonly string[] ToolSymbols = CreateToolSymbols();
        private static readonly GUIContent HeaderContent =
            new GUIContent(CandyBoxInfo.DisplayName + "  v" + CandyBoxInfo.Version);
        private static readonly GUIContent RecompileNoticeContent = new GUIContent(
            "ツールの切り替えを適用するとスクリプトが再コンパイルされます。");
        private static readonly GUIContent PendingNoticeContent = new GUIContent(
            "未適用の変更があります。適用するとスクリプトが再コンパイルされます。");
        private static readonly GUIContent PendingMarkerContent = new GUIContent("*");
        private static readonly GUIContent ApplyContent = new GUIContent("適用");
        private static readonly GUIContent DiscardContent = new GUIContent("破棄");
        private static readonly GUIContent DisabledButtonContent = new GUIContent("無効です");
        private static readonly GUIContent CompilingButtonContent = new GUIContent("コンパイル中…");
        private static readonly GUIContent OpenButtonContent = new GUIContent("開く");
        private static readonly GUIContent UnavailableMarkerContent = new GUIContent("(未導入)");
        private static readonly GUIContent UnavailableButtonContent =
            new GUIContent("必要なパッケージがありません");
        private static readonly GUIContent SelectionMarkerContent = new GUIContent("▶");
        private static readonly GUIContent DependencyHeaderContent =
            new GUIContent("必要なパッケージ");
        private static readonly Color SelectedRowColor =
            new Color(0.45f, 0.65f, 1f, 1f);
        private const string NoDependencyLabel = "なし（追加の導入は不要です）";
        private const string NotInstalledFormat =
            "{0} が見つかりません。導入すると有効化できます。";

        [SerializeField] private string _selectedToolId;
        [SerializeField] private bool[] _pendingEnabled;
        private bool[] _actualEnabled;
        private bool _wasCompiling;
        private float _dependencyHeaderWidth;
        private string _selectedDependencyWarning = string.Empty;

        [MenuItem(CandyBoxInfo.MenuPath, false, CandyBoxInfo.MenuPriority)]
        private static void Open()
        {
            var window = GetWindow<CandyBoxWindow>(
                false, CandyBoxInfo.DisplayName, true);
            window.minSize = new Vector2(480f, 320f);
            window.Show();
        }

        private void OnEnable()
        {
            _wasCompiling = EditorApplication.isCompiling;
            RefreshActual();
            if (CandyBoxToolCatalog.Find(_selectedToolId) == null &&
                CandyBoxToolCatalog.Tools.Length > 0)
            {
                _selectedToolId = CandyBoxToolCatalog.Tools[0].Id;
            }

            RefreshSelectedToolDetails();
        }

        private void Update()
        {
            bool isCompiling = EditorApplication.isCompiling;
            if (isCompiling == _wasCompiling)
            {
                return;
            }

            bool compilationFinished = _wasCompiling && !isCompiling;
            _wasCompiling = isCompiling;
            if (compilationFinished)
            {
                RefreshActual();
                Array.Copy(_actualEnabled, _pendingEnabled, _actualEnabled.Length);
            }

            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(HeaderContent, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(RecompileNoticeContent.text, MessageType.Info);
            EditorGUILayout.Space();

            DrawToolList();
            DrawPendingControls();
            EditorGUILayout.Space();
            DrawSelectedTool();
        }

        private void DrawToolList()
        {
            Event currentEvent = Event.current;

            for (int i = 0; i < CandyBoxToolCatalog.Tools.Length; i++)
            {
                CandyBoxToolEntry tool = CandyBoxToolCatalog.Tools[i];
                bool isSelected = string.Equals(
                    _selectedToolId, tool.Id, StringComparison.Ordinal);
                Color previousBackground = GUI.backgroundColor;
                if (isSelected)
                {
                    GUI.backgroundColor = SelectedRowColor;
                }

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUI.backgroundColor = previousBackground;

                if (isSelected)
                {
                    EditorGUILayout.LabelField(
                        SelectionMarkerContent, GUILayout.Width(14f));
                }
                else
                {
                    EditorGUILayout.LabelField(
                        GUIContent.none, GUILayout.Width(14f));
                }

                EditorGUI.BeginDisabledGroup(!tool.IsAvailable);
                _pendingEnabled[i] = EditorGUILayout.Toggle(
                    _pendingEnabled[i], GUILayout.Width(18f));
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.LabelField(tool.DisplayName, EditorStyles.boldLabel);
                if (!tool.IsAvailable)
                {
                    EditorGUILayout.LabelField(
                        UnavailableMarkerContent, GUILayout.Width(54f));
                }
                if (_pendingEnabled[i] != _actualEnabled[i])
                {
                    EditorGUILayout.LabelField(
                        PendingMarkerContent, GUILayout.Width(12f));
                }
                EditorGUILayout.LabelField(tool.Summary, EditorStyles.miniLabel);

                EditorGUILayout.EndHorizontal();
                Rect rowRect = GUILayoutUtility.GetLastRect();
                EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);
                if (currentEvent.type == EventType.MouseDown &&
                    rowRect.Contains(currentEvent.mousePosition))
                {
                    if (!string.Equals(
                            _selectedToolId, tool.Id, StringComparison.Ordinal))
                    {
                        _selectedToolId = tool.Id;
                        RefreshSelectedToolDetails();
                    }

                    currentEvent.Use();
                    Repaint();
                }
            }
        }

        private void DrawPendingControls()
        {
            bool hasPendingChanges = HasPendingChanges();
            if (hasPendingChanges)
            {
                EditorGUILayout.HelpBox(PendingNoticeContent.text, MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!hasPendingChanges);
            bool applyPressed = GUILayout.Button(ApplyContent, GUILayout.Height(24f));
            bool discardPressed = GUILayout.Button(DiscardContent, GUILayout.Height(24f));
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (applyPressed)
            {
                ApplyPendingChanges();
            }
            else if (discardPressed)
            {
                Array.Copy(_actualEnabled, _pendingEnabled, _actualEnabled.Length);
            }
        }

        private void DrawSelectedTool()
        {
            int selectedIndex = FindToolIndex(_selectedToolId);
            if (selectedIndex < 0)
            {
                return;
            }

            CandyBoxToolEntry selectedTool = CandyBoxToolCatalog.Tools[selectedIndex];
            EditorGUILayout.LabelField(
                selectedTool.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                selectedTool.Description, EditorStyles.wordWrappedLabel);
            if (_dependencyHeaderWidth <= 0f)
            {
                // NOTE: GUI コンテキスト外では計測できないため、最初の描画時だけ求める。
                _dependencyHeaderWidth =
                    EditorStyles.label.CalcSize(DependencyHeaderContent).x + 4f;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                DependencyHeaderContent, GUILayout.Width(_dependencyHeaderWidth));
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(selectedTool.RequirementLabel)
                    ? NoDependencyLabel
                    : selectedTool.RequirementLabel);
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_selectedDependencyWarning))
            {
                EditorGUILayout.HelpBox(
                    _selectedDependencyWarning, MessageType.Warning);
            }
            EditorGUILayout.Space();

            bool isEnabled = _actualEnabled[selectedIndex];
            bool isBusy = EditorApplication.isCompiling || EditorApplication.isUpdating;
            bool isRegistered = CandyBoxToolRegistry.TryGetOpener(
                selectedTool.Id, out Action opener);

            GUIContent buttonContent;
            bool canOpen;
            if (!selectedTool.IsAvailable)
            {
                buttonContent = UnavailableButtonContent;
                canOpen = false;
            }
            else if (!isEnabled)
            {
                buttonContent = DisabledButtonContent;
                canOpen = false;
            }
            else if (isBusy || !isRegistered)
            {
                buttonContent = CompilingButtonContent;
                canOpen = false;
            }
            else
            {
                buttonContent = OpenButtonContent;
                canOpen = true;
            }

            EditorGUI.BeginDisabledGroup(!canOpen);
            bool openPressed = GUILayout.Button(buttonContent, GUILayout.Height(28f));
            EditorGUI.EndDisabledGroup();
            if (openPressed)
            {
                opener();
            }
        }

        private void RefreshActual()
        {
            int toolCount = CandyBoxToolCatalog.Tools.Length;
            if (_actualEnabled == null || _actualEnabled.Length != toolCount)
            {
                _actualEnabled = new bool[toolCount];
            }

            bool initializePending =
                _pendingEnabled == null || _pendingEnabled.Length != toolCount;
            if (initializePending)
            {
                _pendingEnabled = new bool[toolCount];
            }

            CandyBoxDefines.GetDefined(ToolSymbols, _actualEnabled);
            if (initializePending)
            {
                Array.Copy(_actualEnabled, _pendingEnabled, toolCount);
            }

            // NOTE: 要件を満たさない行は操作できないため、解消不能な保留差分を残さない。
            for (int toolIndex = 0; toolIndex < toolCount; toolIndex++)
            {
                if (!CandyBoxToolCatalog.Tools[toolIndex].IsAvailable)
                {
                    _pendingEnabled[toolIndex] = _actualEnabled[toolIndex];
                }
            }
        }

        private void RefreshSelectedToolDetails()
        {
            CandyBoxToolEntry selectedTool =
                CandyBoxToolCatalog.Find(_selectedToolId);
            _selectedDependencyWarning =
                selectedTool != null &&
                !selectedTool.IsAvailable &&
                !string.IsNullOrEmpty(selectedTool.RequirementLabel)
                    ? string.Format(
                        NotInstalledFormat, selectedTool.RequirementLabel)
                    : string.Empty;
        }

        private bool HasPendingChanges()
        {
            for (int i = 0; i < _actualEnabled.Length; i++)
            {
                if (_actualEnabled[i] != _pendingEnabled[i])
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyPendingChanges()
        {
            int changeCount = 0;
            for (int i = 0; i < _actualEnabled.Length; i++)
            {
                if (_actualEnabled[i] != _pendingEnabled[i])
                {
                    changeCount++;
                }
            }

            var symbols = new string[changeCount];
            var enabled = new bool[changeCount];
            int destinationIndex = 0;
            for (int i = 0; i < _actualEnabled.Length; i++)
            {
                if (_actualEnabled[i] == _pendingEnabled[i])
                {
                    continue;
                }

                symbols[destinationIndex] = ToolSymbols[i];
                enabled[destinationIndex] = _pendingEnabled[i];
                destinationIndex++;
            }

            // NOTE: シンボルの書き換えによる再コンパイルで GUI のレイアウト処理を
            //       中断しないよう、描画が完了してから変更する。
            EditorApplication.delayCall += () =>
                CandyBoxDefines.SetDefined(symbols, enabled);
        }

        private static int FindToolIndex(string toolId)
        {
            for (int i = 0; i < CandyBoxToolCatalog.Tools.Length; i++)
            {
                if (CandyBoxToolCatalog.Tools[i].Id == toolId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string[] CreateToolSymbols()
        {
            var symbols = new string[CandyBoxToolCatalog.Tools.Length];
            for (int i = 0; i < symbols.Length; i++)
            {
                symbols[i] = CandyBoxToolCatalog.Tools[i].DefineSymbol;
            }

            return symbols;
        }
    }
}
