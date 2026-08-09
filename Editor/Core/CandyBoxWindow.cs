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

        [SerializeField] private string _selectedToolId;
        [SerializeField] private bool[] _pendingEnabled;
        private bool[] _actualEnabled;
        private bool _wasCompiling;

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
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                _pendingEnabled[i] = EditorGUILayout.Toggle(
                    _pendingEnabled[i], GUILayout.Width(18f));
                EditorGUILayout.LabelField(tool.DisplayName, EditorStyles.boldLabel);
                if (_pendingEnabled[i] != _actualEnabled[i])
                {
                    EditorGUILayout.LabelField(
                        PendingMarkerContent, GUILayout.Width(12f));
                }
                EditorGUILayout.LabelField(tool.Summary, EditorStyles.miniLabel);

                EditorGUILayout.EndHorizontal();
                Rect rowRect = GUILayoutUtility.GetLastRect();
                if (currentEvent.type == EventType.MouseDown &&
                    rowRect.Contains(currentEvent.mousePosition))
                {
                    _selectedToolId = tool.Id;
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
                selectedTool.Description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            bool isEnabled = _actualEnabled[selectedIndex];
            bool isBusy = EditorApplication.isCompiling || EditorApplication.isUpdating;
            bool isRegistered = CandyBoxToolRegistry.TryGetOpener(
                selectedTool.Id, out Action opener);

            GUIContent buttonContent;
            bool canOpen;
            if (!isEnabled)
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
