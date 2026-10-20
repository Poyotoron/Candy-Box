using System;
using System.Collections.Generic;
using System.Threading;
using Suzuryg.FaceEmo.AppMain;
using Suzuryg.FaceEmo.UseCase;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.FaceEmoSplitter.Editor
{
    internal static class FaceEmoSplitterGenerator
    {
        internal static Dictionary<int, string> PlannedPaths(FaceEmoSplitterLabels labels)
        {
            var paths = new Dictionary<int, string>();
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (FaceEmoSplitterLabel label in labels.Labels)
            {
                string path = label.Apply && label.Star != null
                    ? AssetDatabase.GetAssetPath(label.Star.Launchers[0].AV3Setting.MARootObjectPrefab)
                    : label.Source;
                if (string.IsNullOrEmpty(path))
                {
                    path = null;
                }

                if (label.Apply && path != null)
                {
                    bool shared = claimed.Contains(path);
                    foreach (FaceEmoSplitterLabel other in labels.Labels)
                    {
                        shared |= other != label && other.Source == path;
                    }

                    // NOTE: 同じ表情 Prefab の上書きが別ラベルへ及ばないよう、元のラベルに所有を残す。
                    if (shared)
                    {
                        path = null;
                    }
                }

                paths.Add(label.Index, path);
                if (path != null)
                {
                    claimed.Add(path);
                }
            }

            return paths;
        }

        internal static void ResolvePrefabs(FaceEmoSplitterLabels labels)
        {
            Dictionary<int, string> paths = PlannedPaths(labels);
            foreach (FaceEmoSplitterLabel label in labels.Labels)
            {
                if (label.Apply && paths[label.Index] == null)
                {
                    var settings = label.Star.Launchers[0].AV3Setting;
                    if (settings.MARootObjectPrefab != null)
                    {
                        Undo.RecordObject(settings, "FaceEmo Splitter");
                        settings.MARootObjectPrefab = null;
                        EditorUtility.SetDirty(settings);
                    }
                }
            }
        }

        internal static bool Generate(FaceEmoSplitterLabel label, out string error)
        {
            error = null;
            GameObject instance = label.Star.Instances[0];
            var launcher = label.Star.Launchers[0];
            var renamed = new List<GameObject>();
            IBackupper backupper = null;
            var observer = new ResultObserver();
            try
            {
                // NOTE: 受け継いだ表情を直接書き換えず、一時インスタンスだけを生成先にする。
                for (int i = 0; i < instance.transform.childCount; i++)
                {
                    GameObject child = instance.transform.GetChild(i).gameObject;
                    if (child.name == FaceEmoSplitterNames.Expression)
                    {
                        renamed.Add(child);
                        child.name = FaceEmoSplitterNames.Hidden;
                    }
                }

                if (launcher.AV3Setting.MARootObjectPrefab != null)
                {
                    var temporary = (GameObject)PrefabUtility.InstantiatePrefab(
                        launcher.AV3Setting.MARootObjectPrefab, instance.transform);
                    temporary.name = FaceEmoSplitterNames.Expression;
                }

                var installer = new FaceEmoInstaller(launcher.gameObject);
                backupper = installer.Container.Resolve<IBackupper>();
                // NOTE: 自動バックアップにはランチャー名の設定が必要。
                backupper.SetName(launcher.gameObject.name);
                using (installer.Container.Resolve<IGenerateFxPresenter>().Observable.Subscribe(observer))
                {
                    // NOTE: 生成先のフォルダ名は秒単位なので、直前の生成と同じ秒なら待つ。
                    for (int i = 0; i < 20; i++)
                    {
                        string path = "Assets/Suzuryg/FaceEmo/Generated/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        if (!AssetDatabase.IsValidFolder(path))
                        {
                            break;
                        }

                        Thread.Sleep(100);
                    }

                    // NOTE: Prefab への配置は系統の起点ごとに行うため、FaceEmo に書き換え先を渡さない。
                    installer.Container.Resolve<IGenerateFxUseCase>().Handle("", Array.Empty<string>());
                }

                if (!observer.Received)
                {
                    error = "FaceEmo から結果が返りませんでした。";
                    return false;
                }

                if (observer.Result != GenerateFxResult.Succeeded)
                {
                    error = "ラベル " + label.Name + " の表情を作れませんでした。\n" +
                        (string.IsNullOrEmpty(observer.Message) ? observer.Result.ToString() : observer.Message);
                    return false;
                }

                return true;
            }
            finally
            {
                try
                {
                    for (int i = instance.transform.childCount - 1; i >= 0; i--)
                    {
                        GameObject child = instance.transform.GetChild(i).gameObject;
                        if (child.name == FaceEmoSplitterNames.Expression)
                        {
                            UnityEngine.Object.DestroyImmediate(child);
                        }
                    }
                }
                finally
                {
                    try
                    {
                        foreach (GameObject child in renamed)
                        {
                            if (child == null)
                            {
                                continue;
                            }

                            child.name = FaceEmoSplitterNames.Expression;
                            var serialized = new SerializedObject(child);
                            SerializedProperty name = serialized.FindProperty("m_Name");
                            if (name.prefabOverride)
                            {
                                PrefabUtility.RevertPropertyOverride(name, InteractionMode.AutomatedAction);
                            }
                        }
                    }
                    finally
                    {
                        backupper?.Dispose();
                    }
                }
            }
        }

        private sealed class ResultObserver : IObserver<(GenerateFxResult, string)>
        {
            internal bool Received;
            internal GenerateFxResult Result;
            internal string Message;

            public void OnNext((GenerateFxResult, string) value)
            {
                Received = true;
                Result = value.Item1;
                Message = value.Item2;
            }

            public void OnError(Exception error)
            {
                Received = true;
                Result = GenerateFxResult.Error;
                Message = error.Message;
                Debug.LogException(error);
            }

            public void OnCompleted()
            {
            }
        }
    }
}
