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
        private static readonly GUIContent NoneContent = new GUIContent("なし");
        private static readonly GUIContent[] ColumnContents =
        {
            GUIContent.none, new GUIContent("名前"), new GUIContent("指定"),
            new GUIContent("ラベル"), StarContent, new GUIContent("状態")
        };
        private static readonly Color[] LabelColors =
        {
            new Color(0.90f, 0.33f, 0.30f), new Color(0.25f, 0.52f, 0.96f),
            new Color(0.20f, 0.66f, 0.42f), new Color(0.90f, 0.65f, 0.20f),
            new Color(0.62f, 0.42f, 0.88f), new Color(0.17f, 0.72f, 0.77f),
            new Color(0.89f, 0.44f, 0.67f), new Color(0.55f, 0.48f, 0.36f)
        };
        private static readonly bool[] BlackChipText = { false, false, false, true, false, true, false, false };
        private static readonly Color LightLineColor = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color DarkLineColor = new Color(1f, 1f, 1f, 0.25f);
        private static readonly Color LightStripeColor = new Color(0f, 0f, 0f, 0.04f);
        private static readonly Color DarkStripeColor = new Color(1f, 1f, 1f, 0.025f);
        private const float IndentWidth = 16f;
        private const float ChangedWidth = 12f;
        private const float ChipColumnWidth = 36f;
        private const float StarWidth = 24f;
        private const float ColumnGap = 4f;
        private GUIStyle _effectiveStyle;
        private GUIStyle _chipWhite;
        private GUIStyle _chipBlack;
        private GUIStyle[] _chipOutline;
        private GUIStyle _boldName;
        private float _assignWidth;
        private float _nameWidth = 180f;
        private float _statusWidth = 120f;
        private float _treeWidth;
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
            EnsureStyles();
            _tree = FaceEmoSplitterScanner.Scan(_input, out _error);
            _labels = _tree == null ? null : new FaceEmoSplitterLabels(_tree);
            MeasureColumns();
            _collapsed.Clear();
            _result = null;
            UpdateValidation();
            Repaint();
        }

        private void OnGUI()
        {
            if (_effectiveStyle == null || _chipOutline == null || _boldName == null ||
                _chipWhite == null || _chipBlack == null)
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

            DrawTree();
            if (_labels != null)
            {
                EditorGUILayout.LabelField("ラベル", EditorStyles.boldLabel);
                foreach (FaceEmoSplitterLabel label in _labels.Labels)
                {
                    EditorGUILayout.BeginHorizontal();
                    Rect chipRect = GUILayoutUtility.GetRect(
                        ChipColumnWidth, EditorGUIUtility.singleLineHeight + 4f, GUILayout.Width(ChipColumnWidth));
                    DrawChip(chipRect, label.Index, label.ChipContent, true);
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

        private void MeasureColumns()
        {
            _nameWidth = 180f;
            _statusWidth = 120f;
            if (_tree != null)
            {
                foreach (FaceEmoSplitterNode node in _tree.Nodes)
                {
                    // NOTE: 指定を変えると起点になって太字になるため、全ノードを太字の幅で測る。
                    _nameWidth = Mathf.Max(_nameWidth,
                        node.Depth * IndentWidth + IndentWidth + _boldName.CalcSize(node.NameContent).x + 8f);
                    var status = new GUIContent("起点、" + node.StatusLabel);
                    _statusWidth = Mathf.Max(_statusWidth, EditorStyles.label.CalcSize(status).x + 8f);
                }
            }

            _treeWidth = ChangedWidth + _nameWidth + _assignWidth + ChipColumnWidth +
                StarWidth + _statusWidth + ColumnGap * 5f;
        }

        private float ColumnWidth(int column)
        {
            switch (column)
            {
                case 0:
                    return ChangedWidth;
                case 1:
                    return _nameWidth;
                case 2:
                    return _assignWidth;
                case 3:
                    return ChipColumnWidth;
                case 4:
                    return StarWidth;
                default:
                    return _statusWidth;
            }
        }

        private Rect ColumnRect(Rect row, int column)
        {
            float x = row.x;
            for (int i = 0; i < column; i++)
            {
                x += ColumnWidth(i) + ColumnGap;
            }

            return new Rect(x, row.y + 2f, ColumnWidth(column), EditorGUIUtility.singleLineHeight);
        }

        private void DrawTree()
        {
            float height = EditorGUIUtility.singleLineHeight + 4f;
            Rect header = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
            GUI.BeginClip(header, new Vector2(-_scroll.x, 0f), Vector2.zero, false);
            try
            {
                Rect row = new Rect(0f, 0f, _treeWidth, height);
                for (int i = 1; i < ColumnContents.Length; i++)
                {
                    GUI.Label(ColumnRect(row, i), ColumnContents[i], EditorStyles.miniLabel);
                }
            }
            finally
            {
                GUI.EndClip();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            if (_tree != null)
            {
                int visible = 0;
                foreach (FaceEmoSplitterNode node in _tree.Nodes)
                {
                    if (IsHidden(node))
                    {
                        continue;
                    }

                    Rect row = GUILayoutUtility.GetRect(
                        _treeWidth, height, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(false));
                    DrawTreeRow(row, node, visible++);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTreeRow(Rect row, FaceEmoSplitterNode node, int visibleIndex)
        {
            if (visibleIndex % 2 != 0)
            {
                EditorGUI.DrawRect(row, EditorGUIUtility.isProSkin ? DarkStripeColor : LightStripeColor);
            }

            if (node.Effective >= 0)
            {
                Color tint = LabelColors[node.Effective % LabelColors.Length];
                tint.a = node.IsOrigin ? 0.18f : 0.08f;
                EditorGUI.DrawRect(row, tint);
            }

            if (node.Assigned != node.InitialAssigned)
            {
                GUI.Label(ColumnRect(row, 0), ChangedContent);
            }

            Rect nameColumn = ColumnRect(row, 1);
            DrawBranches(row, nameColumn.x, node);
            Rect foldout = new Rect(
                nameColumn.x + node.Depth * IndentWidth, nameColumn.y, IndentWidth, nameColumn.height);
            if (node.Children.Count > 0)
            {
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

            Rect name = new Rect(foldout.xMax, nameColumn.y, nameColumn.xMax - foldout.xMax, nameColumn.height);
            GUI.Label(name, node.NameContent, node.IsOrigin ? _boldName : EditorStyles.label);
            if (Event.current.type == EventType.MouseDown && name.Contains(Event.current.mousePosition))
            {
                EditorGUIUtility.PingObject(node.Asset);
                Event.current.Use();
            }

            int assigned = EditorGUI.Popup(ColumnRect(row, 2), node.AssignIndex, _labels.AssignOptions);
            if (assigned != node.AssignIndex)
            {
                EditorApplication.delayCall += () => ChangeAssigned(node, assigned);
            }

            DrawChip(ColumnRect(row, 3), node.Effective, node.ChipContent,
                node.Assigned != FaceEmoSplitterLabels.Inherit);
            if (node.Launchers.Count > 0 &&
                GUI.Button(ColumnRect(row, 4), node.IsStar ? StarContent : EmptyStarContent, EditorStyles.miniButton))
            {
                EditorApplication.delayCall += () => ChangeStar(node);
            }

            GUI.Label(ColumnRect(row, 5), node.StatusContent);
        }

        private static void DrawBranches(Rect row, float x0, FaceEmoSplitterNode node)
        {
            Color color = EditorGUIUtility.isProSkin ? DarkLineColor : LightLineColor;
            for (int d = 0; d < node.Depth - 1; d++)
            {
                if (node.Continues[d])
                {
                    EditorGUI.DrawRect(new Rect(x0 + d * IndentWidth + 8f, row.y, 1f, row.height), color);
                }
            }

            if (node.Depth > 0)
            {
                float x = x0 + (node.Depth - 1) * IndentWidth + 8f;
                EditorGUI.DrawRect(new Rect(x, row.y, 1f, node.IsLastChild ? row.height * 0.5f : row.height), color);
                EditorGUI.DrawRect(new Rect(x, row.center.y, IndentWidth * 0.5f, 1f), color);
            }
        }

        private void DrawChip(Rect column, int index, GUIContent content, bool filled)
        {
            if (index < 0)
            {
                GUI.Label(column, NoneContent, _effectiveStyle);
                return;
            }

            int colorIndex = index % LabelColors.Length;
            Color color = LabelColors[colorIndex];
            Rect chip = new Rect(column.center.x - 14f, column.center.y - 8f, 28f, 16f);
            if (filled)
            {
                EditorGUI.DrawRect(chip, color);
                GUI.Label(chip, content, BlackChipText[colorIndex] ? _chipBlack : _chipWhite);
            }
            else
            {
                EditorGUI.DrawRect(new Rect(chip.x, chip.y, chip.width, 1f), color);
                EditorGUI.DrawRect(new Rect(chip.x, chip.yMax - 1f, chip.width, 1f), color);
                EditorGUI.DrawRect(new Rect(chip.x, chip.y, 1f, chip.height), color);
                EditorGUI.DrawRect(new Rect(chip.xMax - 1f, chip.y, 1f, chip.height), color);
                GUI.Label(chip, content, _chipOutline[colorIndex]);
            }
        }

        private void EnsureStyles()
        {
            if (_effectiveStyle == null || _chipOutline == null || _boldName == null ||
                _chipWhite == null || _chipBlack == null)
            {
                _effectiveStyle = new GUIStyle(EditorStyles.label);
                _effectiveStyle.normal.textColor = Color.gray;
                _chipWhite = CreateChipStyle(Color.white);
                _chipBlack = CreateChipStyle(Color.black);
                _chipOutline = new GUIStyle[LabelColors.Length];
                for (int i = 0; i < _chipOutline.Length; i++)
                {
                    _chipOutline[i] = CreateChipStyle(LabelColors[i]);
                }

                _boldName = new GUIStyle(EditorStyles.boldLabel);
                _assignWidth = EditorStyles.popup.CalcSize(NewLabelContent).x + 8f;
                MeasureColumns();
                Repaint();
            }
        }

        private static GUIStyle CreateChipStyle(Color color)
        {
            var style = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = color;
            return style;
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
