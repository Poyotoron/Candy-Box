using System;
using System.Collections.Generic;
using Poyo.CandyBox.Editor;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BlendShapeBackuper.Editor
{
    internal sealed class BlendShapeBackuperWindow : EditorWindow
    {
        private static readonly GUIContent TitleContent =
            new GUIContent("06_BlendShape Backuper");
        private static readonly GUIContent TargetHeaderContent = new GUIContent("対象");
        private static readonly GUIContent TargetContent = new GUIContent("対象メッシュ");
        private static readonly GUIContent SaveHeaderContent = new GUIContent("保存");
        private static readonly GUIContent DisplayNameContent = new GUIContent("表示名");
        private static readonly GUIContent FolderContent = new GUIContent("保存フォルダ");
        private static readonly GUIContent SubFolderContent = new GUIContent("サブフォルダ");
        private static readonly GUIContent SelectFolderContent = new GUIContent("選択");
        private static readonly GUIContent SaveContent =
            new GUIContent("バックアップを保存");
        private static readonly GUIContent BackupHeaderContent =
            new GUIContent("バックアップ");
        private static readonly GUIContent IncludeAllContent =
            new GUIContent("すべてのバックアップを表示");
        private static readonly GUIContent RefreshContent = new GUIContent("一覧を更新");
        private static readonly GUIContent SelectAssetContent =
            new GUIContent("アセットを選択");
        private static readonly GUIContent CompareHeaderContent = new GUIContent("比較");
        private static readonly GUIContent SelectAllContent = new GUIContent("すべて選択");
        private static readonly GUIContent ClearAllContent = new GUIContent("すべて解除");
        private static readonly GUIContent DuplicateCountContent =
            new GUIContent("名前が重複している項目");
        private static readonly GUIContent ActionsHeaderContent = new GUIContent("操作");
        private static readonly GUIContent PreviewContent = new GUIContent("プレビュー");
        private static readonly GUIContent EndPreviewContent =
            new GUIContent("プレビューを終了");
        private static readonly GUIContent RestoreContent = new GUIContent("復元");

        private static readonly Color SeparatorColor =
            new Color(0.45f, 0.45f, 0.45f, 0.55f);
        private static readonly Color SelectedRowColor =
            EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.38f, 0.58f, 0.65f)
                : new Color(0.55f, 0.75f, 0.95f, 0.65f);
        private static readonly GUILayoutOption SelectButtonWidth = GUILayout.Width(56f);
        private static readonly GUILayoutOption ActionButtonHeight = GUILayout.Height(30f);
        private static readonly GUILayoutOption ExpandWidth = GUILayout.ExpandWidth(true);

        private const string IntroLabel =
            "現在のブレンドシェイプ値を保存し、キー名で照らし合わせて復元します。";
        private const string PlayingLabel =
            "再生中は保存・復元・プレビューできません。";
        private const string PreviewingLabel =
            "プレビュー中です。値は一時的に書き換えられています。";
        private const string InvalidSubFolderLabel =
            "サブフォルダ名に使えない文字が含まれています。";
        private const string EmptyBackupListLabel =
            "保存されたバックアップがありません。";
        private const string EmptyMatchingBackupListLabel =
            "保存されたバックアップがありません。\n「すべてのバックアップを表示」で、他のメッシュから保存したものも表示できます。";
        private const string SelectBackupLabel =
            "一覧から比較するバックアップを選んでください。";
        private const string MetaMismatchLabel =
            "別のメッシュから保存されたバックアップです。キー名で照らし合わせて復元します。";
        private const string InvalidDroppedFolderLabel =
            "Assets 配下のフォルダを指定してください。";
        private const float BackupRowHeight = 44f;
        private const float AssetButtonRectWidth = 108f;

        [SerializeField] private SkinnedMeshRenderer _renderer;
        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private string _folderPath = BlendShapeBackupWriter.DefaultFolder;
        [SerializeField] private string _subFolderName = string.Empty;
        [SerializeField] private bool _includeAllBackups;
        [SerializeField] private Vector2 _scroll;

        private BlendShapeBackupTarget _target;
        private List<BlendShapeBackupListItem> _backups =
            new List<BlendShapeBackupListItem>();
        private BlendShapeBackupListItem _selectedBackup;
        private BlendShapeCompareResult _compareResult;
        private BlendShapeBackupPreview _preview = new BlendShapeBackupPreview();
        private string _saveMessage = string.Empty;
        private string _restoreMessage = string.Empty;
        private string _folderMessage = string.Empty;
        private bool _saveSucceeded;
        private bool _restoreSucceeded;

        internal static void Open()
        {
            BlendShapeBackuperWindow window =
                GetWindow<BlendShapeBackuperWindow>(TitleContent.text);
            window.titleContent = TitleContent;
            window.minSize = new Vector2(620f, 720f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = TitleContent;
            minSize = new Vector2(620f, 720f);
            if (string.IsNullOrEmpty(_folderPath))
            {
                _folderPath = BlendShapeBackupWriter.DefaultFolder;
            }

            if (_preview == null)
            {
                _preview = new BlendShapeBackupPreview();
            }

            _target = BlendShapeBackupTargetResolver.Resolve(_renderer);
            _backups = BlendShapeBackupLocator.Collect(_target, _includeAllBackups);
            _selectedBackup = null;
            _compareResult = null;

            AssemblyReloadEvents.beforeAssemblyReload -= EndPreview;
            AssemblyReloadEvents.beforeAssemblyReload += EndPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= EndPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EndPreview();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField(TitleContent, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(IntroLabel, MessageType.Info);

            if (IsPreviewActive)
            {
                EditorGUILayout.HelpBox(PreviewingLabel, MessageType.Warning);
            }

            if (IsPlayModeUnavailable)
            {
                EditorGUILayout.HelpBox(PlayingLabel, MessageType.Warning);
            }

            DrawTarget();

            EditorGUI.BeginDisabledGroup(!TargetIsValid);
            DrawSave();
            DrawBackups();
            DrawComparison();
            DrawActions();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndScrollView();
        }

        private void DrawTarget()
        {
            DrawSeparator();
            EditorGUILayout.LabelField(TargetHeaderContent, EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(IsPreviewActive);
            SkinnedMeshRenderer nextRenderer = EditorGUILayout.ObjectField(
                TargetContent,
                _renderer,
                typeof(SkinnedMeshRenderer),
                true) as SkinnedMeshRenderer;
            EditorGUI.EndDisabledGroup();

            if (nextRenderer != _renderer)
            {
                ChangeTarget(nextRenderer);
            }

            if (TargetIsValid)
            {
                EditorGUILayout.HelpBox(_target.SummaryLabel, MessageType.Info);
            }
            else if (_target != null && !string.IsNullOrEmpty(_target.BlockedLabel))
            {
                EditorGUILayout.HelpBox(_target.BlockedLabel, MessageType.Info);
            }
        }

        private void DrawSave()
        {
            DrawSeparator();
            EditorGUILayout.LabelField(SaveHeaderContent, EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(IsPreviewActive);
            string nextDisplayName =
                EditorGUILayout.TextField(DisplayNameContent, _displayName);
            if (!string.Equals(nextDisplayName, _displayName, StringComparison.Ordinal))
            {
                _displayName = nextDisplayName;
            }

            EditorGUILayout.BeginHorizontal();
            string nextFolderPath = EditorGUILayout.TextField(FolderContent, _folderPath);
            Rect folderRect = GUILayoutUtility.GetLastRect();
            bool selectFolder = GUILayout.Button(SelectFolderContent, SelectButtonWidth);
            EditorGUILayout.EndHorizontal();
            if (!string.Equals(nextFolderPath, _folderPath, StringComparison.Ordinal))
            {
                SetFolderPath(nextFolderPath);
            }

            if (TargetIsValid && !IsPreviewActive)
            {
                HandleFolderDrop(folderRect);
            }
            if (selectFolder)
            {
                OpenFolderPicker();
            }

            string nextSubFolder =
                EditorGUILayout.TextField(SubFolderContent, _subFolderName);
            if (!string.Equals(nextSubFolder, _subFolderName, StringComparison.Ordinal))
            {
                _subFolderName = nextSubFolder;
            }
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(_folderMessage))
            {
                EditorGUILayout.HelpBox(_folderMessage, MessageType.Warning);
            }

            bool validSubFolder =
                BlendShapeBackupWriter.IsValidSubFolderName(_subFolderName);
            if (!validSubFolder)
            {
                EditorGUILayout.HelpBox(InvalidSubFolderLabel, MessageType.Warning);
            }

            EditorGUI.BeginDisabledGroup(
                IsPreviewActive || IsPlayModeUnavailable || !validSubFolder);
            bool save = GUILayout.Button(SaveContent, ActionButtonHeight);
            EditorGUI.EndDisabledGroup();
            if (save)
            {
                SaveBackup();
            }

            if (!string.IsNullOrEmpty(_saveMessage))
            {
                EditorGUILayout.HelpBox(
                    _saveMessage,
                    _saveSucceeded ? MessageType.Info : MessageType.Warning);
            }
        }

        private void DrawBackups()
        {
            DrawSeparator();
            EditorGUILayout.LabelField(BackupHeaderContent, EditorStyles.boldLabel);

            bool nextIncludeAll =
                EditorGUILayout.ToggleLeft(IncludeAllContent, _includeAllBackups);
            if (nextIncludeAll != _includeAllBackups)
            {
                ChangeIncludeAll(nextIncludeAll);
            }

            if (GUILayout.Button(RefreshContent))
            {
                RefreshBackupList();
            }

            if (_backups == null || _backups.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    _includeAllBackups
                        ? EmptyBackupListLabel
                        : EmptyMatchingBackupListLabel,
                    MessageType.Info);
                return;
            }

            for (int i = 0; i < _backups.Count; i++)
            {
                DrawBackupRow(_backups[i]);
            }
        }

        private void DrawBackupRow(BlendShapeBackupListItem item)
        {
            Rect rowRect = GUILayoutUtility.GetRect(
                0f, BackupRowHeight, GUIStyle.none, ExpandWidth);
            bool selected = _selectedBackup != null &&
                _selectedBackup.Asset == item.Asset;
            if (Event.current.type == EventType.Repaint && selected)
            {
                EditorGUI.DrawRect(rowRect, SelectedRowColor);
            }

            Rect assetButtonRect = new Rect(
                rowRect.xMax - AssetButtonRectWidth,
                rowRect.y + 10f,
                AssetButtonRectWidth,
                22f);
            Rect contentRect = new Rect(
                rowRect.x + 6f,
                rowRect.y,
                rowRect.width - AssetButtonRectWidth - 12f,
                rowRect.height);
            Rect labelRect = new Rect(
                contentRect.x,
                contentRect.y + 3f,
                contentRect.width,
                20f);
            Rect subLabelRect = new Rect(
                contentRect.x,
                contentRect.y + 23f,
                contentRect.width,
                18f);

            EditorGUIUtility.AddCursorRect(contentRect, MouseCursor.Link);
            if (GUI.Button(contentRect, GUIContent.none, GUIStyle.none))
            {
                SelectBackup(item);
            }

            GUI.Label(labelRect, item.RowLabel, EditorStyles.label);
            GUI.Label(subLabelRect, item.SubLabel, EditorStyles.miniLabel);
            if (GUI.Button(assetButtonRect, SelectAssetContent, EditorStyles.miniButton))
            {
                Selection.activeObject = item.Asset;
                EditorGUIUtility.PingObject(item.Asset);
            }
        }

        private void DrawComparison()
        {
            DrawSeparator();
            EditorGUILayout.LabelField(CompareHeaderContent, EditorStyles.boldLabel);
            if (_compareResult == null || _compareResult.Backup == null)
            {
                EditorGUILayout.HelpBox(SelectBackupLabel, MessageType.Info);
                return;
            }

            if (_compareResult.MetaMismatch)
            {
                EditorGUILayout.HelpBox(MetaMismatchLabel, MessageType.Warning);
            }

            EditorGUILayout.LabelField(_compareResult.SummaryLabel);
            DrawCompareGroup(
                _compareResult.Changed,
                ref _compareResult.ChangedExpanded,
                _compareResult.ChangedHeader,
                true);
            DrawCompareGroup(
                _compareResult.Unchanged,
                ref _compareResult.UnchangedExpanded,
                _compareResult.UnchangedHeader,
                true);
            DrawCompareGroup(
                _compareResult.BackupOnly,
                ref _compareResult.BackupOnlyExpanded,
                _compareResult.BackupOnlyHeader,
                false);
            DrawCompareGroup(
                _compareResult.MeshOnly,
                ref _compareResult.MeshOnlyExpanded,
                _compareResult.MeshOnlyHeader,
                false);

            if (_compareResult.Duplicated.Count > 0)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.IntField(
                    DuplicateCountContent, _compareResult.Duplicated.Count);
                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel++;
                DrawCompareRows(_compareResult.Duplicated, false);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawCompareGroup(
            List<BlendShapeCompareRow> rows,
            ref bool expanded,
            string header,
            bool showSelectionButtons)
        {
            if (rows.Count == 0)
            {
                return;
            }

            expanded = EditorGUILayout.Foldout(expanded, header, true);
            if (!expanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            if (showSelectionButtons)
            {
                DrawSelectionButtons(rows);
            }

            DrawCompareRows(rows, showSelectionButtons);
            EditorGUI.indentLevel--;
        }

        private void DrawSelectionButtons(List<BlendShapeCompareRow> rows)
        {
            EditorGUILayout.BeginHorizontal();
            bool selectAll = GUILayout.Button(SelectAllContent);
            bool clearAll = GUILayout.Button(ClearAllContent);
            EditorGUILayout.EndHorizontal();
            if (selectAll)
            {
                ChangeGroupSelection(rows, true);
            }
            else if (clearAll)
            {
                ChangeGroupSelection(rows, false);
            }
        }

        private void DrawCompareRows(
            List<BlendShapeCompareRow> rows, bool selectable)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                BlendShapeCompareRow row = rows[i];
                if (selectable && row.IsSelectable)
                {
                    bool nextSelected =
                        EditorGUILayout.ToggleLeft(row.RowLabel, row.IsSelected);
                    if (nextSelected != row.IsSelected)
                    {
                        ChangeRowSelection(row, nextSelected);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(row.RowLabel);
                }
            }
        }

        private void DrawActions()
        {
            DrawSeparator();
            EditorGUILayout.LabelField(ActionsHeaderContent, EditorStyles.boldLabel);

            bool hasSelection = _compareResult != null &&
                _compareResult.SelectedCount > 0;
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(
                !IsPreviewActive && (IsPlayModeUnavailable || !hasSelection));
            bool preview = GUILayout.Button(
                IsPreviewActive ? EndPreviewContent : PreviewContent,
                ActionButtonHeight);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(
                IsPreviewActive || IsPlayModeUnavailable || !hasSelection);
            bool restore = GUILayout.Button(RestoreContent, ActionButtonHeight);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (preview)
            {
                TogglePreview();
            }

            if (restore)
            {
                RestoreBackup();
            }

            if (!string.IsNullOrEmpty(_restoreMessage))
            {
                EditorGUILayout.HelpBox(
                    _restoreMessage,
                    _restoreSucceeded ? MessageType.Info : MessageType.Warning);
            }
        }

        private static void DrawSeparator()
        {
            EditorGUILayout.Space();
            Rect rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, SeparatorColor);
            EditorGUILayout.Space();
        }

        private void ChangeTarget(SkinnedMeshRenderer renderer)
        {
            EndPreview();
            _renderer = renderer;
            _target = BlendShapeBackupTargetResolver.Resolve(_renderer);
            _displayName = BlendShapeBackupWriter.BuildDefaultName(_target);
            _backups = BlendShapeBackupLocator.Collect(_target, _includeAllBackups);
            _selectedBackup = null;
            _compareResult = null;
            _saveMessage = string.Empty;
            _restoreMessage = string.Empty;
        }

        private void ChangeIncludeAll(bool includeAll)
        {
            EndPreview();
            _includeAllBackups = includeAll;
            RecollectBackups(_selectedBackup == null ? null : _selectedBackup.Asset);
        }

        private void RefreshBackupList()
        {
            EndPreview();
            RecollectBackups(_selectedBackup == null ? null : _selectedBackup.Asset);
        }

        private void RecollectBackups(BlendShapeBackupAsset preferredAsset)
        {
            _backups = BlendShapeBackupLocator.Collect(_target, _includeAllBackups);
            _selectedBackup = FindBackupItem(preferredAsset);
            _compareResult = _selectedBackup == null
                ? null
                : BlendShapeBackupComparer.Compare(_target, _selectedBackup);
        }

        private BlendShapeBackupListItem FindBackupItem(BlendShapeBackupAsset asset)
        {
            if (asset == null || _backups == null)
            {
                return null;
            }

            for (int i = 0; i < _backups.Count; i++)
            {
                if (_backups[i].Asset == asset)
                {
                    return _backups[i];
                }
            }

            return null;
        }

        private void SelectBackup(BlendShapeBackupListItem item)
        {
            EndPreview();
            _selectedBackup = item;
            _compareResult = BlendShapeBackupComparer.Compare(_target, item);
            _restoreMessage = string.Empty;
        }

        private void ChangeRowSelection(
            BlendShapeCompareRow row, bool selected)
        {
            EndPreview();
            row.IsSelected = selected;
            BlendShapeBackupComparer.RefreshLabels(_compareResult);
            _restoreMessage = string.Empty;
        }

        private void ChangeGroupSelection(
            List<BlendShapeCompareRow> rows, bool selected)
        {
            EndPreview();
            BlendShapeBackupComparer.SetSelection(rows, selected);
            BlendShapeBackupComparer.RefreshLabels(_compareResult);
            _restoreMessage = string.Empty;
        }

        private void SaveBackup()
        {
            BlendShapeBackupSaveResult result = BlendShapeBackupWriter.Save(
                _target,
                _folderPath,
                _subFolderName,
                _displayName);
            _saveSucceeded = result.Succeeded;
            _saveMessage = result.MessageLabel;
            if (result.Succeeded)
            {
                RecollectBackups(result.Asset);
            }
        }

        private void TogglePreview()
        {
            if (IsPreviewActive)
            {
                EndPreview();
                return;
            }

            _preview.Begin(_target, _compareResult);
            Repaint();
        }

        private void RestoreBackup()
        {
            BlendShapeRestoreResult result =
                BlendShapeBackupRestorer.Restore(_target, _compareResult);
            _restoreSucceeded = result.Succeeded;
            _restoreMessage = result.MessageLabel;
            if (result.Succeeded && _selectedBackup != null)
            {
                _compareResult = BlendShapeBackupComparer.Compare(
                    _target, _selectedBackup);
            }
        }

        private void EndPreview()
        {
            if (_preview != null)
            {
                _preview.End();
            }

            Repaint();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                EndPreview();
            }
        }

        private void SetFolderPath(string path)
        {
            _folderPath = NormalizePath(path);
            _folderMessage = string.Empty;
        }

        private void OpenFolderPicker()
        {
            string selectedPath = EditorUtility.OpenFolderPanel(
                "保存フォルダを選択", Application.dataPath, string.Empty);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                string assetPath = NormalizePath(
                    FileUtil.GetProjectRelativePath(selectedPath));
                if (IsAssetsFolder(assetPath))
                {
                    SetFolderPath(assetPath);
                }
                else
                {
                    _folderMessage = InvalidDroppedFolderLabel;
                }
            }

            GUIUtility.ExitGUI();
        }

        private void HandleFolderDrop(Rect dropArea)
        {
            Event current = Event.current;
            if (!dropArea.Contains(current.mousePosition) ||
                (current.type != EventType.DragUpdated &&
                 current.type != EventType.DragPerform))
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                UnityEngine.Object[] references = DragAndDrop.objectReferences;
                string assetPath = references.Length == 0
                    ? string.Empty
                    : NormalizePath(AssetDatabase.GetAssetPath(references[0]));
                if (IsAssetsFolder(assetPath))
                {
                    SetFolderPath(assetPath);
                }
                else
                {
                    _folderMessage = InvalidDroppedFolderLabel;
                }
            }

            current.Use();
        }

        private static bool IsAssetsFolder(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                (path == "Assets" ||
                 path.StartsWith("Assets/", StringComparison.Ordinal)) &&
                AssetDatabase.IsValidFolder(path);
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.Trim().Replace('\\', '/').TrimEnd('/');
        }

        private bool TargetIsValid
        {
            get { return _target != null && _target.IsValid; }
        }

        private bool IsPreviewActive
        {
            get { return _preview != null && _preview.IsActive; }
        }

        private static bool IsPlayModeUnavailable
        {
            get
            {
                return EditorApplication.isPlaying ||
                    EditorApplication.isPlayingOrWillChangePlaymode;
            }
        }
    }
}
