using System.Collections.Generic;
using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.FaceEmoSplitter.Editor
{
    internal sealed class FaceEmoSplitterWindow : EditorWindow
    {
        private static readonly GUIContent InputContent = new GUIContent("アバターの Prefab");
        private static readonly GUIContent LoadContent = new GUIContent("読み込む");
        private static readonly GUIContent ReloadContent = new GUIContent("Prefab を変えたら、読み込み直してください。");
        private static readonly GUIContent NewLabelContent = new GUIContent("新しいラベル");
        private static readonly GUIContent StarContent = new GUIContent("★");
        private static readonly GUIContent EmptyStarContent = new GUIContent("☆");
        private static readonly GUIContent ChangedContent = new GUIContent("*");
        private static readonly GUIContent ApplyLabelContent = new GUIContent("適用する");
        private static readonly GUIContent ApplyContent = new GUIContent("アバターに適用");
        private GUIStyle _effectiveStyle;
        private GUILayoutOption[] _assignWidth;
        [SerializeField] private GameObject _input;
        private FaceEmoSplitterTree _tree;
        private FaceEmoSplitterLabels _labels;
        private string _error;
        private string _validation;
        private string _result;
        private bool _applying;
        private Vector2 _scroll;
        private readonly HashSet<FaceEmoSplitterNode> _collapsed = new HashSet<FaceEmoSplitterNode>();

        internal static void Open()
        {
            var window = GetWindow<FaceEmoSplitterWindow>(false, "08_FaceEmo Splitter", true);
            window.minSize = new Vector2(640f, 640f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.delayCall += EnsureStyles;
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -= EnsureStyles;
            EditorApplication.delayCall -= Load;
            EditorApplication.delayCall -= Apply;
            EditorApplication.delayCall -= UpdateValidation;
        }

        private void OnFocus()
        {
            UpdateValidation();
        }

        private void Load()
        {
            _tree = FaceEmoSplitterScanner.Scan(_input, out _error);
            _labels = _tree == null ? null : new FaceEmoSplitterLabels(_tree);
            _collapsed.Clear();
            _result = null;
            UpdateValidation();
            Repaint();
        }

        private void OnGUI()
        {
            if (_effectiveStyle == null)
            {
                return;
            }

            _input = (GameObject)EditorGUILayout.ObjectField(InputContent, _input, typeof(GameObject), true);
            EditorGUI.BeginDisabledGroup(EditorApplication.isPlaying);
            if (GUILayout.Button(LoadContent))
            {
                EditorApplication.delayCall += Load;
            }

            EditorGUILayout.LabelField(ReloadContent, EditorStyles.miniLabel);
            if (_error != null)
            {
                EditorGUILayout.HelpBox(_error, MessageType.Warning);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            if (_tree != null)
            {
                foreach (FaceEmoSplitterNode node in _tree.Nodes)
                {
                    if (IsHidden(node))
                    {
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(node.Depth * 16f);
                    if (node.Assigned != node.InitialAssigned)
                    {
                        GUILayout.Label(ChangedContent, GUILayout.ExpandWidth(false));
                    }

                    if (node.Children.Count > 0)
                    {
                        Rect foldout = GUILayoutUtility.GetRect(16f, EditorGUIUtility.singleLineHeight, GUILayout.Width(16f));
                        bool expanded = EditorGUI.Foldout(foldout, !_collapsed.Contains(node), GUIContent.none);
                        if (expanded)
                        {
                            _collapsed.Remove(node);
                        }
                        else
                        {
                            _collapsed.Add(node);
                        }
                    }

                    GUILayout.Label(node.Name, GUILayout.ExpandWidth(false));
                    if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                    {
                        EditorGUIUtility.PingObject(node.Asset);
                        Event.current.Use();
                    }

                    int assigned = EditorGUILayout.Popup(node.AssignIndex, _labels.AssignOptions, _assignWidth);
                    if (assigned != node.AssignIndex)
                    {
                        EditorApplication.delayCall += () => ChangeAssigned(node, assigned);
                    }

                    if (node.EffectiveLabel != null)
                    {
                        GUILayout.Label(node.EffectiveLabel, _effectiveStyle, GUILayout.ExpandWidth(false));
                    }

                    if (node.Launchers.Count > 0)
                    {
                        FaceEmoSplitterLabel label = _labels.Find(node.Effective);
                        if (GUILayout.Button(label != null && label.Star == node ? StarContent : EmptyStarContent,
                            EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        {
                            EditorApplication.delayCall += () => ChangeStar(node);
                        }
                    }

                    GUILayout.Label(node.RowStatus);
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();
            if (_labels != null)
            {
                EditorGUILayout.LabelField("ラベル", EditorStyles.boldLabel);
                foreach (FaceEmoSplitterLabel label in _labels.Labels)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(label.Name, GUILayout.ExpandWidth(false));
                    GUILayout.Label(label.StarLabel);
                    GUILayout.Label(label.SourceLabel);
                    GUILayout.Label(label.OriginLabel, GUILayout.ExpandWidth(false));
                    EditorGUI.BeginDisabledGroup(label.Star == null || label.OriginCount == 0);
                    bool apply = EditorGUILayout.ToggleLeft(ApplyLabelContent, label.Apply, GUILayout.ExpandWidth(false));
                    if (apply != label.Apply)
                    {
                        _labels.SetApply(label, apply);
                        EditorApplication.delayCall += UpdateValidation;
                    }

                    EditorGUI.EndDisabledGroup();
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUI.BeginDisabledGroup(_tree == null || EditorApplication.isCompiling || _applying);
            if (GUILayout.Button(ApplyContent))
            {
                EditorApplication.delayCall += Apply;
            }

            EditorGUI.EndDisabledGroup();
            if (_validation != null)
            {
                EditorGUILayout.HelpBox(_validation, MessageType.Warning);
            }

            if (_result != null)
            {
                EditorGUILayout.HelpBox(_result, MessageType.Info);
            }

            EditorGUI.EndDisabledGroup();
            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("再生中は使えません。再生を停止してください。", MessageType.Warning);
            }
        }

        private bool IsHidden(FaceEmoSplitterNode node)
        {
            for (FaceEmoSplitterNode parent = node.Parent; parent != null; parent = parent.Parent)
            {
                if (_collapsed.Contains(parent))
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureStyles()
        {
            if (_effectiveStyle == null)
            {
                _effectiveStyle = new GUIStyle(EditorStyles.label);
                _effectiveStyle.normal.textColor = Color.gray;
                _assignWidth = new[] { GUILayout.Width(EditorStyles.popup.CalcSize(NewLabelContent).x + 8f) };
                Repaint();
            }
        }

        private void ChangeAssigned(FaceEmoSplitterNode node, int index)
        {
            if (this == null || _tree == null || !_tree.Nodes.Contains(node))
            {
                return;
            }

            _labels.SetAssigned(node, index);
            UpdateValidation();
            Repaint();
        }

        private void ChangeStar(FaceEmoSplitterNode node)
        {
            if (this == null || _tree == null || !_tree.Nodes.Contains(node))
            {
                return;
            }

            _labels.SetStar(node);
            UpdateValidation();
            Repaint();
        }

        private void UpdateValidation()
        {
            _validation = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                ? "再生中・コンパイル中は使えません。"
                : _labels == null ? "「読み込む」を押してください。" : _labels.Validate();
            Repaint();
        }

        private void Apply()
        {
            UpdateValidation();
            if (_validation != null)
            {
                EditorUtility.DisplayDialog("Candy Box", _validation, "OK");
                return;
            }

            List<FaceEmoSplitterNode> plan = FaceEmoSplitterPlacer.Plan(_tree, _labels);
            var descriptions = new List<string>();
            foreach (FaceEmoSplitterLabel label in _labels.Labels)
            {
                if (label.Apply)
                {
                    descriptions.Add(label.Name + "（担当: " + label.Star.Name + "）");
                }
            }

            var message = new StringBuilder("次の Prefab を書き換えます。Undo では戻せません。\n\n表情を作り直すラベル: ");
            message.Append(descriptions.Count == 0 ? "なし" : string.Join("、", descriptions));
            message.Append("\n書き換える Prefab: ").Append(plan.Count).Append(" 件");
            for (int i = 0; i < Math.Min(10, plan.Count); i++)
            {
                message.Append('\n').Append(plan[i].Path);
            }

            if (plan.Count > 10)
            {
                message.Append("\nほか ").Append(plan.Count - 10).Append(" 件");
            }

            if (!EditorUtility.DisplayDialog("Candy Box", message.ToString(), "適用", "キャンセル"))
            {
                return;
            }

            _applying = true;
            int saved = 0;
            var generated = new List<string>();
            try
            {
                FaceEmoSplitterGenerator.ResolvePrefabs(_labels);
                foreach (FaceEmoSplitterLabel label in _labels.Labels)
                {
                    if (!label.Apply)
                    {
                        continue;
                    }

                    if (!FaceEmoSplitterGenerator.Generate(label, out string error))
                    {
                        EditorUtility.DisplayDialog("Candy Box", error, "OK");
                        return;
                    }

                    generated.Add(label.Name);
                }

                saved = FaceEmoSplitterPlacer.Place(_tree, _labels, out string placementError);
                if (placementError != null)
                {
                    EditorUtility.DisplayDialog("Candy Box", placementError, "OK");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Candy Box", "表情を適用できませんでした。\n" + exception.Message, "OK");
            }
            finally
            {
                try
                {
                    AssetDatabase.SaveAssets();
                    Load();
                    _result = "表情を作り直したラベル: " +
                        (generated.Count == 0 ? "なし" : string.Join(", ", generated)) +
                        " / 書き換えた Prefab: " + saved + " 件";
                }
                finally
                {
                    _applying = false;
                    Repaint();
                }
            }
        }
    }
}
