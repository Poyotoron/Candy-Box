using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.FaceEmoSplitter.Editor
{
    internal sealed class FaceEmoSplitterLabels
    {
        internal const int Inherit = -2;
        private readonly FaceEmoSplitterTree _tree;
        private readonly List<FaceEmoSplitterLabel> _labels = new List<FaceEmoSplitterLabel>();
        internal IReadOnlyList<FaceEmoSplitterLabel> Labels => _labels;
        internal GUIContent[] AssignOptions { get; private set; }

        internal FaceEmoSplitterLabels(FaceEmoSplitterTree tree)
        {
            _tree = tree;
            for (int i = 0; i < tree.LabelSources.Count; i++)
            {
                _labels.Add(new FaceEmoSplitterLabel
                {
                    Index = i,
                    Name = FaceEmoSplitterNames.LabelName(i),
                    Source = tree.LabelSources[i]
                });
            }

            foreach (FaceEmoSplitterNode node in tree.Nodes)
            {
                int parent = node.Parent == null ? -1 : node.Parent.CurrentLabel;
                node.Assigned = node.CurrentLabel == parent ? Inherit : node.CurrentLabel;
                node.InitialAssigned = node.Assigned;
            }

            Recompute();
        }

        internal void SetAssigned(FaceEmoSplitterNode node, int popupIndex)
        {
            if (popupIndex == 0)
            {
                node.Assigned = Inherit;
            }
            else if (popupIndex == _labels.Count + 1)
            {
                int index = 0;
                foreach (FaceEmoSplitterLabel label in _labels)
                {
                    if (label.Index != index)
                    {
                        break;
                    }

                    index++;
                }

                _labels.Add(new FaceEmoSplitterLabel
                {
                    Index = index,
                    Name = FaceEmoSplitterNames.LabelName(index)
                });
                _labels.Sort((left, right) => left.Index.CompareTo(right.Index));
                node.Assigned = index;
            }
            else
            {
                node.Assigned = _labels[popupIndex - 1].Index;
            }

            Recompute();
        }

        internal void ApplySaved(FaceEmoSplitterSettingsData data, out int missing)
        {
            missing = 0;
            var sourceIndices = new Dictionary<string, int>();
            var reserved = new HashSet<int>();
            foreach (FaceEmoSplitterSavedLabel saved in data.labels)
            {
                reserved.Add(saved.index);
                if (!string.IsNullOrEmpty(saved.sourceGuid))
                {
                    sourceIndices[saved.sourceGuid] = saved.index;
                }
            }

            var remap = new Dictionary<int, int>();
            foreach (FaceEmoSplitterLabel label in _labels)
            {
                string guid = AssetDatabase.AssetPathToGUID(label.Source);
                if (!sourceIndices.TryGetValue(guid, out int index))
                {
                    index = 0;
                    while (reserved.Contains(index))
                    {
                        index++;
                    }
                }

                reserved.Add(index);
                remap[label.Index] = index;
                label.Index = index;
                label.Name = FaceEmoSplitterNames.LabelName(index);
            }

            foreach (FaceEmoSplitterNode node in _tree.Nodes)
            {
                if (node.CurrentLabel >= 0)
                {
                    node.CurrentLabel = remap[node.CurrentLabel];
                }

                if (node.Assigned >= 0)
                {
                    node.Assigned = remap[node.Assigned];
                }

                if (node.InitialAssigned >= 0)
                {
                    node.InitialAssigned = remap[node.InitialAssigned];
                }
            }

            foreach (FaceEmoSplitterSavedLabel saved in data.labels)
            {
                if (string.IsNullOrEmpty(saved.sourceGuid) && Find(saved.index) == null)
                {
                    _labels.Add(new FaceEmoSplitterLabel
                    {
                        Index = saved.index,
                        Name = FaceEmoSplitterNames.LabelName(saved.index)
                    });
                }
            }

            _labels.Sort((left, right) => left.Index.CompareTo(right.Index));
            var nodesByGuid = new Dictionary<string, FaceEmoSplitterNode>();
            foreach (FaceEmoSplitterNode node in _tree.Nodes)
            {
                node.Assigned = Inherit;
                nodesByGuid[AssetDatabase.AssetPathToGUID(node.Path)] = node;
            }

            foreach (FaceEmoSplitterSavedNode saved in data.nodes)
            {
                if (!string.IsNullOrEmpty(saved.guid) &&
                    nodesByGuid.TryGetValue(saved.guid, out FaceEmoSplitterNode node) && Find(saved.assigned) != null)
                {
                    node.Assigned = saved.assigned;
                }
                else
                {
                    missing++;
                }
            }

            foreach (FaceEmoSplitterSavedLabel saved in data.labels)
            {
                FaceEmoSplitterLabel label = Find(saved.index);
                if (label == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(saved.starGuid))
                {
                    if (nodesByGuid.TryGetValue(saved.starGuid, out FaceEmoSplitterNode star) && star.Launchers.Count > 0)
                    {
                        label.Star = star;
                    }
                    else
                    {
                        missing++;
                    }
                }

                label.Apply = saved.apply;
            }

            Recompute(true);
        }

        internal void SetStar(FaceEmoSplitterNode node)
        {
            if (node.Launchers.Count == 0 || node.Effective < 0)
            {
                return;
            }

            FaceEmoSplitterLabel label = Find(node.Effective);
            label.Star = node;
            label.Apply = true;
            Recompute();
        }

        internal void SetApply(FaceEmoSplitterLabel label, bool value)
        {
            label.Apply = value && label.Star != null && label.OriginCount > 0;
        }

        internal FaceEmoSplitterLabel Find(int index)
        {
            foreach (FaceEmoSplitterLabel label in _labels)
            {
                if (label.Index == index)
                {
                    return label;
                }
            }

            return null;
        }

        internal string Validate()
        {
            bool any = false;
            foreach (FaceEmoSplitterNode node in _tree.Nodes)
            {
                any |= node.Effective >= 0;
            }

            if (!any)
            {
                return "ラベルを付けてください。";
            }

            foreach (FaceEmoSplitterLabel label in _labels)
            {
                if (label.Apply && (label.Star == null || label.Star.Launchers.Count == 0 ||
                    label.Star.Launchers[0] == null || label.Star.Launchers[0].AV3Setting == null ||
                    label.Star.Launchers[0].AV3Setting.TargetAvatar == null ||
                    label.Star.Instances[0] == null))
                {
                    return "ラベル " + label.Name + " の担当（★）を選んでください。";
                }
            }

            foreach (FaceEmoSplitterLabel label in _labels)
            {
                if (label.Apply && label.Star.Launchers.Count >= 2)
                {
                    return label.Star.Name + " を対象にするランチャーが複数あります。1 つにしてください。";
                }
            }

            foreach (FaceEmoSplitterLabel label in _labels)
            {
                if (!label.Apply)
                {
                    continue;
                }

                foreach (MonoBehaviour avatar in label.Star.Launchers[0].AV3Setting.SubTargetAvatars)
                {
                    if (avatar != null)
                    {
                        return "ラベル " + label.Name + " のランチャーにサブアバターが設定されています。外してから実行してください。";
                    }
                }
            }

            foreach (FaceEmoSplitterLabel label in _labels)
            {
                if (!label.Apply)
                {
                    continue;
                }

                bool added = false;
                foreach (GameObject instance in label.Star.Instances)
                {
                    added |= instance != null && FaceEmoSplitterScanner.HasAddedExpression(instance);
                }

                label.Star.HasSceneAddedExpression = added;
                if (added)
                {
                    return label.Star.Name + " のシーンに追加された FaceEmoPrefab があります。削除してから実行してください。";
                }
            }

            foreach (FaceEmoSplitterLabel label in _labels)
            {
                if (label.OriginCount > 0 && !label.Apply &&
                    (string.IsNullOrEmpty(label.Source) || AssetDatabase.LoadAssetAtPath<GameObject>(label.Source) == null))
                {
                    return "ラベル " + label.Name + " はまだ表情が作られていません。「適用する」を付けてください。";
                }
            }

            return null;
        }

        private void Recompute(bool preserveApply = false)
        {
            var used = new HashSet<int>();
            foreach (FaceEmoSplitterNode node in _tree.Nodes)
            {
                int parent = node.Parent == null ? -1 : node.Parent.Effective;
                node.Effective = node.Assigned == Inherit ? parent : node.Assigned;
                node.IsOrigin = node.Effective >= 0 && node.Effective != parent;
                used.Add(node.Effective);
            }

            // NOTE: 元の表情を再び選べるよう、出どころのあるラベルは使われなくても残す。
            _labels.RemoveAll(label => label.Source == null && !used.Contains(label.Index));
            foreach (FaceEmoSplitterLabel label in _labels)
            {
                label.OriginCount = 0;
                foreach (FaceEmoSplitterNode node in _tree.Nodes)
                {
                    if (node.IsOrigin && node.Effective == label.Index)
                    {
                        label.OriginCount++;
                    }
                }

                if (label.Star == null || label.Star.Effective != label.Index)
                {
                    FaceEmoSplitterNode candidate = null;
                    int count = 0;
                    foreach (FaceEmoSplitterNode node in _tree.Nodes)
                    {
                        if (node.Effective == label.Index && node.Launchers.Count > 0)
                        {
                            candidate = node;
                            count++;
                        }
                    }

                    label.Star = count == 1 ? candidate : null;
                    // NOTE: 配布元など担当のないラベルまで ON にすると、担当未指定で毎回止まってしまう。
                    // NOTE: 保存値の読み戻しでは、担当を自動選択しても保存した OFF を保つ。
                    if (!preserveApply)
                    {
                        label.Apply = label.Star != null;
                    }
                }

                if (label.Star == null || label.OriginCount == 0)
                {
                    label.Apply = false;
                }

                label.StarLabel = label.Star == null ? "担当がありません" :
                    label.Star.Name + " / " + label.Star.Launchers[0].gameObject.name;
                label.SourceLabel = string.IsNullOrEmpty(label.Source) ? "まだ作られていません" : Path.GetFileName(label.Source);
                label.OriginLabel = "起点 " + label.OriginCount;
                label.ChipContent = new GUIContent(label.Name);
            }

            AssignOptions = new GUIContent[_labels.Count + 2];
            AssignOptions[0] = new GUIContent("継承");
            for (int i = 0; i < _labels.Count; i++)
            {
                AssignOptions[i + 1] = new GUIContent(_labels[i].Name);
            }

            AssignOptions[AssignOptions.Length - 1] = new GUIContent("新しいラベル");
            foreach (FaceEmoSplitterNode node in _tree.Nodes)
            {
                int index = _labels.FindIndex(label => label.Index == node.Assigned);
                node.AssignIndex = node.Assigned == Inherit || index < 0 ? 0 : index + 1;
                FaceEmoSplitterLabel effective = Find(node.Effective);
                node.ChipContent = effective == null ? null : effective.ChipContent;
                node.IsStar = effective != null && effective.Star == node;
                node.RowStatus = node.IsOrigin
                    ? "起点" + (string.IsNullOrEmpty(node.StatusLabel) ? "" : "、" + node.StatusLabel)
                    : node.StatusLabel;
                node.StatusContent = new GUIContent(node.RowStatus);
            }
        }
    }
}
