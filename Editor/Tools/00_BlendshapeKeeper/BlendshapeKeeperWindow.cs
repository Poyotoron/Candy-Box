using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BlendshapeKeeper.Editor
{
    internal sealed class BlendshapeKeeperWindow : EditorWindow
    {
        private static readonly GUIContent TitleContent =
            new GUIContent("00_Blendshape Keeper");
        private static readonly GUIContent IntroContent = new GUIContent(
            "対象メッシュとアニメーションを指定して、ブレンドシェイプの現在値をアニメーションへ反映します。");
        private static readonly GUIContent TargetMeshHeaderContent = new GUIContent("対象メッシュ");
        private static readonly GUIContent AvatarRootContent = new GUIContent("アバタールート");
        private static readonly GUIContent MissingAnimatorContent = new GUIContent(
            "Animator が見つかりませんでした。カーブの起点になるアバタールートを指定してください。");
        private static readonly GUIContent ClipHeaderContent = new GUIContent("対象アニメーション");
        private static readonly GUIContent OutputHeaderContent = new GUIContent("出力");
        private static readonly GUIContent OutputModeContent = new GUIContent("書き込み先");
        private static readonly GUIContent[] OutputModeContents =
        {
            new GUIContent("元のアニメーションを上書き"),
            new GUIContent("別のアニメーションとして保存"),
        };
        private static readonly GUIContent OutputFolderContent = new GUIContent("出力フォルダ");
        private static readonly GUIContent OutputFolderPathContent = new GUIContent("パス");
        private static readonly GUIContent SelectFolderContent = new GUIContent("選択");
        private static readonly GUIContent SuffixContent = new GUIContent("サフィックス");
        private static readonly GUIContent CopyWithoutChangesContent =
            new GUIContent("変更が無いアニメーションも複製する");
        private static readonly GUIContent IncludeSubfoldersContent =
            new GUIContent("サブフォルダを含める");
        private static readonly GUIContent RemoveContent = new GUIContent("×");
        private static readonly GUIContent AddContent = new GUIContent("+ 追加");
        private static readonly GUIContent AddFolderContent = new GUIContent("フォルダから追加");
        private static readonly GUIContent ScanContent = new GUIContent("差分を確認");
        private static readonly GUIContent PreviewContent = new GUIContent("変更内容");
        private static readonly GUIContent SelectAllContent = new GUIContent("すべて選択");
        private static readonly GUIContent ClearAllContent = new GUIContent("すべて解除");
        private static readonly GUIContent SkipContent = new GUIContent("スキップ");
        private static readonly GUIContent ApplyContent = new GUIContent("適用");
        private static readonly GUIContent PreviewButtonContent = new GUIContent("変更をプレビュー");

        private const string PlayingWarning =
            "再生中は実行できません。再生を停止してください。";
        private const string AnimationPreviewWarning =
            "Animation ウィンドウのプレビュー中は実行できません。プレビューを終了してください。";
        private const string TargetMeshWarning = "対象メッシュを 1 つ以上指定してください。";
        private const string AvatarRootWarning = "アバタールートを指定してください。";
        private const string RootMismatchWarning =
            "指定したメッシュのアバタールートが一致しません。別々に実行してください。";
        private const string ClipWarning = "対象アニメーションを 1 つ以上指定してください。";
        private const string OutputFolderWarning = "出力フォルダを指定してください。";
        private const string OutputFolderMissingWarning =
            "出力フォルダが見つかりません。指定し直してください。";
        private const string NoChangesMessage = "引き上げるキーはありませんでした。";
        private const string DuplicateMeshWarning =
            "Candy Box: 同じ対象メッシュが既に指定されています。";
        private const string DuplicateClipWarning =
            "Candy Box: 同じアニメーションが既に指定されています。";
        private const string OutsideProjectWarning =
            "Candy Box: プロジェクト内のフォルダを選択してください。";
        private const string OutsideProjectMessage =
            "プロジェクト内のフォルダを選択してください。";
        private const string NotAFolderMessage = "フォルダを指定してください。";
        private const string EmptyOutputPathLabel = "未指定";
        private const string ConfirmFormat = "{0} 件のキーを書き換えます。よろしいですか？";
        private const string CopyConfirmFormat =
            "{0} 件のキーを反映したアニメーションを新しく保存します。よろしいですか？";
        private const string ResultFormat =
            "{0} 件のキーを {1} 個のアニメーションに反映しました。";
        private const string CopyResultFormat =
            "{0} 件のキーを反映した {1} 個のアニメーションを {2} に保存しました。";
        private const string RenamedFormat =
            "\n同名のファイルがあったため、{0} 件は別の名前で保存しました。";

        [SerializeField] private List<SkinnedMeshRenderer> _targetMeshes =
            new List<SkinnedMeshRenderer>();
        [SerializeField] private GameObject _manualAvatarRoot;
        [SerializeField] private List<AnimationClip> _clips = new List<AnimationClip>();
        [SerializeField] private bool _includeSubfolders = true;
        [SerializeField] private BlendshapeKeeperOutputMode _outputMode =
            BlendshapeKeeperOutputMode.Overwrite;
        [SerializeField] private string _outputFolderPath = string.Empty;
        [SerializeField] private DefaultAsset _outputFolderAsset;
        [SerializeField] private string _suffix = "_Kept";
        [SerializeField] private bool _copyWithoutChanges;
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private string _resultMessage = string.Empty;

        private BlendshapeKeeperPlan _plan;
        private GameObject _resolvedAvatarRoot;
        private bool _hasMeshWithoutAnimator;
        private bool _hasRootMismatch;
        private bool _avatarRootDirty = true;
        private string _outputFolderError = string.Empty;

        internal static void Open()
        {
            var window = GetWindow<BlendshapeKeeperWindow>(
                false, "00_Blendshape Keeper", true);
            window.minSize = new Vector2(600f, 600f);
            window.Show();
        }

        private void OnEnable()
        {
            _avatarRootDirty = true;
            if (_targetMeshes == null)
            {
                _targetMeshes = new List<SkinnedMeshRenderer>();
            }

            if (_clips == null)
            {
                _clips = new List<AnimationClip>();
            }

            if (_outputFolderAsset == null &&
                AssetDatabase.IsValidFolder(_outputFolderPath))
            {
                _outputFolderAsset =
                    AssetDatabase.LoadAssetAtPath<DefaultAsset>(_outputFolderPath);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(TitleContent, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(IntroContent.text, MessageType.Info);
            EditorGUILayout.Space();

            DrawTargetMeshes();
            if (_avatarRootDirty)
            {
                ResolveAvatarRoot();
                _avatarRootDirty = false;
            }
            DrawAvatarRoot();
            DrawClipInputs();
            DrawOutput();

            string blockedReason = GetBlockedReason();
            bool isBlocked = !string.IsNullOrEmpty(blockedReason);
            if (isBlocked)
            {
                EditorGUILayout.HelpBox(blockedReason, MessageType.Warning);
            }

            EditorGUI.BeginDisabledGroup(isBlocked);
            bool scanPressed = GUILayout.Button(ScanContent, GUILayout.Height(24f));
            EditorGUI.EndDisabledGroup();
            if (scanPressed)
            {
                BlendshapeKeeperPreviewWindow.CloseIfOpen();
                _plan = BlendshapeKeeperScanner.Scan(
                    _resolvedAvatarRoot, _targetMeshes, _clips, _outputMode);
                _resultMessage = string.Empty;
            }

            if (_plan != null)
            {
                DrawPreview();

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(_plan.EnabledChangeCount == 0);
                bool previewPressed = GUILayout.Button(
                    PreviewButtonContent, GUILayout.Height(28f));
                EditorGUI.EndDisabledGroup();
                bool canCreateUnchangedCopies =
                    _outputMode == BlendshapeKeeperOutputMode.SaveAsCopy &&
                    _copyWithoutChanges &&
                    _plan.Clips.Count > 0;
                EditorGUI.BeginDisabledGroup(
                    isBlocked ||
                    (_plan.EnabledChangeCount == 0 && !canCreateUnchangedCopies));
                bool applyPressed = GUILayout.Button(ApplyContent, GUILayout.Height(28f));
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
                if (previewPressed)
                {
                    BlendshapeKeeperPreviewWindow.Open(_resolvedAvatarRoot, _plan);
                }

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

        private void DrawTargetMeshes()
        {
            EditorGUILayout.LabelField(TargetMeshHeaderContent, EditorStyles.boldLabel);

            int removeIndex = -1;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            for (int meshIndex = 0; meshIndex < _targetMeshes.Count; meshIndex++)
            {
                EditorGUILayout.BeginHorizontal();
                SkinnedMeshRenderer currentMesh = _targetMeshes[meshIndex];
                SkinnedMeshRenderer nextMesh = EditorGUILayout.ObjectField(
                    currentMesh, typeof(SkinnedMeshRenderer), true) as SkinnedMeshRenderer;
                if (nextMesh != currentMesh)
                {
                    if (nextMesh != null && ContainsMeshExcept(nextMesh, meshIndex))
                    {
                        Debug.LogWarning(DuplicateMeshWarning);
                    }
                    else
                    {
                        _targetMeshes[meshIndex] = nextMesh;
                        InvalidatePlan();
                        _avatarRootDirty = true;
                    }
                }

                if (GUILayout.Button(RemoveContent, GUILayout.Width(20f)))
                {
                    removeIndex = meshIndex;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button(AddContent))
            {
                _targetMeshes.Add(null);
                InvalidatePlan();
                _avatarRootDirty = true;
            }
            EditorGUILayout.EndVertical();

            if (removeIndex >= 0)
            {
                _targetMeshes.RemoveAt(removeIndex);
                InvalidatePlan();
                _avatarRootDirty = true;
            }
        }

        private void ResolveAvatarRoot()
        {
            GameObject candidate = null;
            bool hasCandidate = false;
            _hasMeshWithoutAnimator = false;
            _hasRootMismatch = false;

            for (int meshIndex = 0; meshIndex < _targetMeshes.Count; meshIndex++)
            {
                SkinnedMeshRenderer renderer = _targetMeshes[meshIndex];
                if (renderer == null)
                {
                    continue;
                }

                Animator animator = renderer.GetComponentInParent<Animator>();
                if (animator == null)
                {
                    _hasMeshWithoutAnimator = true;
                    continue;
                }

                GameObject rootCandidate = animator.gameObject;
                if (!hasCandidate)
                {
                    candidate = rootCandidate;
                    hasCandidate = true;
                }
                else if (candidate != rootCandidate)
                {
                    _hasRootMismatch = true;
                }
            }

            if (_hasRootMismatch)
            {
                _resolvedAvatarRoot = null;
            }
            else if (_hasMeshWithoutAnimator)
            {
                _resolvedAvatarRoot = _manualAvatarRoot;
            }
            else
            {
                _resolvedAvatarRoot = candidate;
            }
        }

        private void DrawAvatarRoot()
        {
            if (_hasMeshWithoutAnimator)
            {
                EditorGUILayout.HelpBox(MissingAnimatorContent.text, MessageType.Info);
                GameObject nextRoot = EditorGUILayout.ObjectField(
                    AvatarRootContent, _manualAvatarRoot, typeof(GameObject), true) as GameObject;
                if (nextRoot != _manualAvatarRoot)
                {
                    _manualAvatarRoot = nextRoot;
                    _resolvedAvatarRoot = nextRoot;
                    InvalidatePlan();
                    _avatarRootDirty = true;
                }
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(
                    AvatarRootContent, _resolvedAvatarRoot, typeof(GameObject), true);
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space();
        }

        private void DrawClipInputs()
        {
            EditorGUILayout.LabelField(ClipHeaderContent, EditorStyles.boldLabel);
            bool nextIncludeSubfolders = EditorGUILayout.ToggleLeft(
                IncludeSubfoldersContent, _includeSubfolders);
            if (nextIncludeSubfolders != _includeSubfolders)
            {
                _includeSubfolders = nextIncludeSubfolders;
                InvalidatePlan();
            }

            int removeIndex = -1;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            for (int clipIndex = 0; clipIndex < _clips.Count; clipIndex++)
            {
                EditorGUILayout.BeginHorizontal();
                AnimationClip currentClip = _clips[clipIndex];
                AnimationClip nextClip = EditorGUILayout.ObjectField(
                    currentClip, typeof(AnimationClip), false) as AnimationClip;
                if (nextClip != currentClip)
                {
                    if (nextClip != null && ContainsClipExcept(nextClip, clipIndex))
                    {
                        Debug.LogWarning(DuplicateClipWarning);
                    }
                    else
                    {
                        _clips[clipIndex] = nextClip;
                        InvalidatePlan();
                        _avatarRootDirty = true;
                    }
                }

                if (GUILayout.Button(RemoveContent, GUILayout.Width(20f)))
                {
                    removeIndex = clipIndex;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button(AddContent))
            {
                _clips.Add(null);
                InvalidatePlan();
                _avatarRootDirty = true;
            }
            EditorGUILayout.EndVertical();
            Rect dropArea = GUILayoutUtility.GetLastRect();

            if (removeIndex >= 0)
            {
                _clips.RemoveAt(removeIndex);
                InvalidatePlan();
                _avatarRootDirty = true;
            }

            HandleClipDrop(dropArea);

            bool addFolderPressed = GUILayout.Button(AddFolderContent);
            if (addFolderPressed)
            {
                OpenAnimationFolder();
            }
        }

        private void HandleClipDrop(Rect dropArea)
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
                bool changed = false;
                UnityEngine.Object[] references = DragAndDrop.objectReferences;
                for (int referenceIndex = 0; referenceIndex < references.Length; referenceIndex++)
                {
                    UnityEngine.Object reference = references[referenceIndex];
                    AnimationClip clip = reference as AnimationClip;
                    if (clip != null)
                    {
                        if (!_clips.Contains(clip))
                        {
                            _clips.Add(clip);
                            changed = true;
                        }

                        continue;
                    }

                    string assetPath = AssetDatabase.GetAssetPath(reference);
                    if (AssetDatabase.IsValidFolder(assetPath))
                    {
                        AddClipsFromFolder(assetPath, _clips);
                    }
                }

                if (changed)
                {
                    InvalidatePlan();
                    _avatarRootDirty = true;
                }
            }

            currentEvent.Use();
        }

        private void AddClipsFromFolder(
            string folderPath, List<AnimationClip> destination)
        {
            string normalizedFolderPath = NormalizePath(folderPath).TrimEnd('/');
            string[] guids = AssetDatabase.FindAssets(
                "t:AnimationClip", new[] { normalizedFolderPath });
            var assetPaths = new List<string>();
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string assetPath = NormalizePath(
                    AssetDatabase.GUIDToAssetPath(guids[guidIndex]));
                if (!_includeSubfolders)
                {
                    string directory = NormalizePath(Path.GetDirectoryName(assetPath));
                    if (!string.Equals(
                            directory, normalizedFolderPath, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                assetPaths.Add(assetPath);
            }

            assetPaths.Sort(StringComparer.Ordinal);
            int addedCount = 0;
            for (int pathIndex = 0; pathIndex < assetPaths.Count; pathIndex++)
            {
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    assetPaths[pathIndex]);
                if (clip == null || destination.Contains(clip))
                {
                    continue;
                }

                destination.Add(clip);
                addedCount++;
            }

            if (addedCount > 0)
            {
                InvalidatePlan();
                _avatarRootDirty = true;
                Debug.Log(
                    "Candy Box: " + addedCount + " 件のアニメーションを追加しました。");
            }
            else
            {
                Debug.LogWarning(
                    "Candy Box: 追加できるアニメーションが見つかりませんでした。");
            }
        }

        private void OpenAnimationFolder()
        {
            string selectedPath = EditorUtility.OpenFolderPanel(
                "アニメーションのフォルダを選択", "Assets", string.Empty);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                if (TryConvertToAssetPath(selectedPath, out string assetPath))
                {
                    AddClipsFromFolder(assetPath, _clips);
                }
                else
                {
                    Debug.LogWarning(OutsideProjectWarning);
                }
            }

            GUIUtility.ExitGUI();
        }

        private void DrawOutput()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(OutputHeaderContent, EditorStyles.boldLabel);
            BlendshapeKeeperOutputMode nextMode = (BlendshapeKeeperOutputMode)
                EditorGUILayout.Popup(OutputModeContent, (int)_outputMode, OutputModeContents);
            if (nextMode != _outputMode)
            {
                _outputMode = nextMode;
                InvalidatePlan();
            }

            if (_outputMode != BlendshapeKeeperOutputMode.SaveAsCopy)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DefaultAsset nextOutputFolderAsset = EditorGUILayout.ObjectField(
                OutputFolderContent,
                _outputFolderAsset,
                typeof(DefaultAsset),
                false) as DefaultAsset;
            Rect outputFolderFieldRect = GUILayoutUtility.GetLastRect();
            bool selectFolderPressed = GUILayout.Button(SelectFolderContent, GUILayout.Width(52f));
            EditorGUILayout.EndHorizontal();
            if (nextOutputFolderAsset != _outputFolderAsset)
            {
                SetOutputFolderAsset(nextOutputFolderAsset);
            }

            HandleOutputFolderDrop(outputFolderFieldRect);

            if (selectFolderPressed)
            {
                OpenOutputFolder();
            }

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(
                OutputFolderPathContent,
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

            _copyWithoutChanges = EditorGUILayout.ToggleLeft(
                CopyWithoutChangesContent, _copyWithoutChanges);
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
                    Debug.LogWarning(
                        "Candy Box: 出力フォルダに指定できないパスです: " + selectedPath);
                    Repaint();
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
                    Repaint();
                }
            }

            currentEvent.Use();
        }

        private bool TryConvertToAssetPath(string absolutePath, out string assetPath)
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
                    normalizedSelection, normalizedAssets, StringComparison.OrdinalIgnoreCase))
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

        private void SetOutputFolderAsset(DefaultAsset folderAsset)
        {
            if (folderAsset == null)
            {
                _outputFolderAsset = null;
                _outputFolderPath = string.Empty;
                _outputFolderError = string.Empty;
                Repaint();
                return;
            }

            string assetPath = NormalizePath(AssetDatabase.GetAssetPath(folderAsset));
            if (!AssetDatabase.IsValidFolder(assetPath))
            {
                _outputFolderAsset = null;
                _outputFolderPath = string.Empty;
                _outputFolderError = NotAFolderMessage;
                Repaint();
                return;
            }

            _outputFolderAsset = folderAsset;
            _outputFolderPath = assetPath;
            Repaint();
            _outputFolderError = string.Empty;
        }

        private string GetBlockedReason()
        {
            if (EditorApplication.isPlaying)
            {
                return PlayingWarning;
            }

            if (AnimationMode.InAnimationMode())
            {
                return AnimationPreviewWarning;
            }

            bool hasTargetMesh = false;
            for (int meshIndex = 0; meshIndex < _targetMeshes.Count; meshIndex++)
            {
                if (_targetMeshes[meshIndex] != null)
                {
                    hasTargetMesh = true;
                    break;
                }
            }

            if (!hasTargetMesh)
            {
                return TargetMeshWarning;
            }

            if (_hasRootMismatch)
            {
                return RootMismatchWarning;
            }

            if (_resolvedAvatarRoot == null)
            {
                return AvatarRootWarning;
            }

            bool hasClip = false;
            for (int clipIndex = 0; clipIndex < _clips.Count; clipIndex++)
            {
                if (_clips[clipIndex] != null)
                {
                    hasClip = true;
                    break;
                }
            }

            if (!hasClip)
            {
                return ClipWarning;
            }

            if (_outputMode == BlendshapeKeeperOutputMode.SaveAsCopy &&
                string.IsNullOrEmpty(_outputFolderPath))
            {
                return OutputFolderWarning;
            }

            if (_outputMode == BlendshapeKeeperOutputMode.SaveAsCopy &&
                !AssetDatabase.IsValidFolder(_outputFolderPath))
            {
                return OutputFolderMissingWarning;
            }

            return null;
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(PreviewContent, EditorStyles.boldLabel);

            if (_plan.Clips.Count == 0 && _plan.Skips.Count == 0)
            {
                EditorGUILayout.HelpBox(NoChangesMessage, MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(
                _scroll, GUILayout.MinHeight(120f));
            for (int clipIndex = 0; clipIndex < _plan.Clips.Count; clipIndex++)
            {
                DrawClipPlan(_plan.Clips[clipIndex]);
            }

            if (_plan.Skips.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(SkipContent, EditorStyles.boldLabel);
                for (int skipIndex = 0; skipIndex < _plan.Skips.Count; skipIndex++)
                {
                    BlendshapeKeeperSkip skip = _plan.Skips[skipIndex];
                    EditorGUILayout.LabelField(
                        skip.Label, EditorStyles.wordWrappedLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawClipPlan(BlendshapeKeeperClipPlan clipPlan)
        {
            bool allEnabled = true;
            for (int changeIndex = 0; changeIndex < clipPlan.Changes.Count; changeIndex++)
            {
                if (!clipPlan.Changes[changeIndex].Enabled)
                {
                    allEnabled = false;
                    break;
                }
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            clipPlan.Foldout = EditorGUILayout.Foldout(
                clipPlan.Foldout, clipPlan.ClipLabel, true);
            GUIContent selectionContent = allEnabled ? ClearAllContent : SelectAllContent;
            if (GUILayout.Button(selectionContent, GUILayout.Width(72f)))
            {
                bool next = !allEnabled;
                for (int changeIndex = 0; changeIndex < clipPlan.Changes.Count; changeIndex++)
                {
                    clipPlan.Changes[changeIndex].Enabled = next;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!clipPlan.Foldout)
            {
                return;
            }

            EditorGUI.indentLevel++;
            for (int changeIndex = 0; changeIndex < clipPlan.Changes.Count; changeIndex++)
            {
                BlendshapeKeeperChange change = clipPlan.Changes[changeIndex];
                change.Enabled = EditorGUILayout.ToggleLeft(change.Label, change.Enabled);
            }
            EditorGUI.indentLevel--;
        }

        private void ApplyPlan()
        {
            int enabledCount = _plan.EnabledChangeCount;
            string confirmation = string.Format(
                _outputMode == BlendshapeKeeperOutputMode.SaveAsCopy
                    ? CopyConfirmFormat
                    : ConfirmFormat,
                enabledCount);
            bool confirmed = EditorUtility.DisplayDialog(
                "Candy Box", confirmation, "適用", "キャンセル");
            if (!confirmed)
            {
                return;
            }

            BlendshapeKeeperPreviewWindow.CloseIfOpen();
            BlendshapeKeeperApplyResult result = BlendshapeKeeperApplier.Apply(
                _plan,
                _outputMode,
                _outputFolderPath,
                _suffix,
                _copyWithoutChanges);
            if (_outputMode == BlendshapeKeeperOutputMode.SaveAsCopy)
            {
                _resultMessage = string.Format(
                    CopyResultFormat,
                    result.ChangedKeys,
                    result.CreatedClips,
                    _outputFolderPath);
                if (result.RenamedCount > 0)
                {
                    _resultMessage += string.Format(RenamedFormat, result.RenamedCount);
                }
            }
            else
            {
                _resultMessage = string.Format(
                    ResultFormat, result.ChangedKeys, result.ChangedClips);
            }

            _plan = null;
            _avatarRootDirty = true;
            GUIUtility.ExitGUI();
        }

        private bool ContainsMeshExcept(
            SkinnedMeshRenderer renderer, int excludedIndex)
        {
            for (int meshIndex = 0; meshIndex < _targetMeshes.Count; meshIndex++)
            {
                if (meshIndex != excludedIndex && _targetMeshes[meshIndex] == renderer)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ContainsClipExcept(AnimationClip clip, int excludedIndex)
        {
            for (int clipIndex = 0; clipIndex < _clips.Count; clipIndex++)
            {
                if (clipIndex != excludedIndex && _clips[clipIndex] == clip)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        private void InvalidatePlan()
        {
            _plan = null;
            BlendshapeKeeperPreviewWindow.CloseIfOpen();
        }
    }
}
