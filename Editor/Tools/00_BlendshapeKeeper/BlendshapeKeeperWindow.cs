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

        private const string PlayingWarning =
            "再生中は実行できません。再生を停止してください。";
        private const string AnimationPreviewWarning =
            "Animation ウィンドウのプレビュー中は実行できません。プレビューを終了してください。";
        private const string TargetMeshWarning = "対象メッシュを 1 つ以上指定してください。";
        private const string AvatarRootWarning = "アバタールートを指定してください。";
        private const string RootMismatchWarning =
            "指定したメッシュのアバタールートが一致しません。別々に実行してください。";
        private const string ClipWarning = "対象アニメーションを 1 つ以上指定してください。";
        private const string NoChangesMessage = "引き上げるキーはありませんでした。";
        private const string DuplicateMeshWarning =
            "Candy Box: 同じ対象メッシュが既に指定されています。";
        private const string DuplicateClipWarning =
            "Candy Box: 同じアニメーションが既に指定されています。";
        private const string OutsideProjectWarning =
            "Candy Box: プロジェクト内のフォルダを選択してください。";
        private const string ConfirmFormat = "{0} 件のキーを書き換えます。よろしいですか？";
        private const string ResultFormat =
            "{0} 件のキーを {1} 個のアニメーションに反映しました。";

        [SerializeField] private List<SkinnedMeshRenderer> _targetMeshes =
            new List<SkinnedMeshRenderer>();
        [SerializeField] private GameObject _manualAvatarRoot;
        [SerializeField] private List<AnimationClip> _clips = new List<AnimationClip>();
        [SerializeField] private bool _includeSubfolders = true;
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private string _resultMessage = string.Empty;

        private BlendshapeKeeperPlan _plan;
        private GameObject _resolvedAvatarRoot;
        private bool _hasMeshWithoutAnimator;
        private bool _hasRootMismatch;
        private bool _avatarRootDirty = true;

        internal static void Open()
        {
            var window = GetWindow<BlendshapeKeeperWindow>(
                false, "00_Blendshape Keeper", true);
            window.minSize = new Vector2(560f, 400f);
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
                _plan = BlendshapeKeeperScanner.Scan(
                    _resolvedAvatarRoot, _targetMeshes, _clips);
                _resultMessage = string.Empty;
            }

            if (_plan != null)
            {
                DrawPreview();

                EditorGUI.BeginDisabledGroup(
                    isBlocked || _plan.EnabledChangeCount == 0);
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
                        _plan = null;
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
                _plan = null;
                _avatarRootDirty = true;
            }
            EditorGUILayout.EndVertical();

            if (removeIndex >= 0)
            {
                _targetMeshes.RemoveAt(removeIndex);
                _plan = null;
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
                    _plan = null;
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
            _includeSubfolders = EditorGUILayout.ToggleLeft(
                IncludeSubfoldersContent, _includeSubfolders);

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
                        _plan = null;
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
                _plan = null;
                _avatarRootDirty = true;
            }
            EditorGUILayout.EndVertical();
            Rect dropArea = GUILayoutUtility.GetLastRect();

            if (removeIndex >= 0)
            {
                _clips.RemoveAt(removeIndex);
                _plan = null;
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
                    _plan = null;
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
                _plan = null;
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
                string normalizedSelection = NormalizePath(selectedPath).TrimEnd('/');
                string normalizedAssets = NormalizePath(Application.dataPath).TrimEnd('/');
                if (string.Equals(
                        normalizedSelection, normalizedAssets, StringComparison.OrdinalIgnoreCase))
                {
                    AddClipsFromFolder("Assets", _clips);
                }
                else if (normalizedSelection.StartsWith(
                             normalizedAssets + "/", StringComparison.OrdinalIgnoreCase))
                {
                    string assetPath = "Assets" +
                        normalizedSelection.Substring(normalizedAssets.Length);
                    AddClipsFromFolder(assetPath, _clips);
                }
                else
                {
                    Debug.LogWarning(OutsideProjectWarning);
                }
            }

            GUIUtility.ExitGUI();
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

            for (int clipIndex = 0; clipIndex < _clips.Count; clipIndex++)
            {
                if (_clips[clipIndex] != null)
                {
                    return null;
                }
            }

            return ClipWarning;
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
            string confirmation = string.Format(ConfirmFormat, enabledCount);
            bool confirmed = EditorUtility.DisplayDialog(
                "Candy Box", confirmation, "適用", "キャンセル");
            if (!confirmed)
            {
                return;
            }

            BlendshapeKeeperApplier.Apply(
                _plan, out int changedKeys, out int changedClips);
            _resultMessage = string.Format(ResultFormat, changedKeys, changedClips);
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
    }
}
