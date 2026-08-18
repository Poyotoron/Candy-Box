using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.BoneWeightCollapser.Editor
{
    internal static class BoneWeightCollapserApplier
    {
        private sealed class SharedBake
        {
            internal Transform[] BoneOrder;
            internal Mesh BakedMesh;
            internal string OutputPath;
        }

        private const string ApplyUndoName = "Bone Weight Collapser";
        private const string RevertUndoName = "Bone Weight Collapser Revert";

        internal static BoneWeightCollapseResult Apply(
            BoneWeightCollapserPlan plan, string outputFolderPath, string suffix)
        {
            var result = new BoneWeightCollapseResult();
            if (plan == null ||
                string.IsNullOrEmpty(outputFolderPath) ||
                !AssetDatabase.IsValidFolder(outputFolderPath))
            {
                return result;
            }

            var sharedBakes = new Dictionary<Mesh, List<SharedBake>>();
            // NOTE: ここで新しいグループを始めないと直前のユーザー操作と一緒になり、
            // 1 回の Undo で無関係な操作まで戻ってしまう。
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(ApplyUndoName);
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                for (int targetIndex = 0; targetIndex < plan.Targets.Count; targetIndex++)
                {
                    BoneWeightCollapseTarget target = plan.Targets[targetIndex];
                    if (!target.IsSelected)
                    {
                        continue;
                    }

                    if (target.BlockReason != BoneWeightBlockReason.None)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    try
                    {
                        ApplyTarget(
                            target,
                            plan.BlendRatio,
                            plan.Normalize,
                            outputFolderPath,
                            suffix,
                            sharedBakes,
                            result);
                    }
                    catch (Exception exception)
                    {
                        result.SkippedCount++;
                        string error = string.Format(
                            "{0}: {1}", target.PathLabel, exception.Message);
                        target.ResultLabel = error;
                        result.Errors.Add(error);
                    }
                }

                if (result.CreatedAssetCount > 0)
                {
                    AssetDatabase.SaveAssets();
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            return result;
        }

        internal static int Revert(BoneWeightCollapserPlan plan)
        {
            if (plan == null)
            {
                return 0;
            }

            int revertedCount = 0;
            // NOTE: 適用時と同様に、直前の操作を巻き込まない Undo グループにする。
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(RevertUndoName);
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                for (int targetIndex = 0; targetIndex < plan.Targets.Count; targetIndex++)
                {
                    BoneWeightCollapseTarget target = plan.Targets[targetIndex];
                    if (target.PreviousMesh == null || target.Renderer == null)
                    {
                        continue;
                    }

                    SkinnedMeshRenderer renderer = target.Renderer;
                    Undo.RecordObject(renderer, RevertUndoName);
                    renderer.sharedMesh = target.PreviousMesh;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    EditorUtility.SetDirty(renderer);
                    target.PreviousMesh = null;
                    target.OutputPath = null;
                    target.ResultLabel = null;
                    revertedCount++;
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            return revertedCount;
        }

        private static void ApplyTarget(
            BoneWeightCollapseTarget target,
            float blendRatio,
            bool normalize,
            string outputFolderPath,
            string suffix,
            Dictionary<Mesh, List<SharedBake>> sharedBakes,
            BoneWeightCollapseResult result)
        {
            SkinnedMeshRenderer renderer = target.Renderer;
            Mesh source = target.SourceMesh;
            if (renderer == null || source == null || renderer.sharedMesh != source)
            {
                throw new InvalidOperationException(
                    "走査後にメッシュが変更されました。もう一度影響を確認してください。");
            }

            Transform[] boneOrder = renderer.bones;
            SharedBake shared = FindSharedBake(sharedBakes, source, boneOrder);
            bool reused = shared != null;
            if (!reused)
            {
                Mesh baked = BoneWeightCollapserBaker.Bake(
                    source,
                    target.SourceBoneIndices,
                    target.DestinationBoneIndex,
                    blendRatio,
                    normalize,
                    out int ignoredAffectedVertexCount,
                    out float ignoredMovedWeight);
                if (baked == null)
                {
                    throw new InvalidOperationException(
                        "新しいメッシュを作成できませんでした。");
                }

                string desiredPath = outputFolderPath.TrimEnd('/') + "/" +
                    source.name + suffix + ".asset";
                string path = AssetDatabase.GenerateUniqueAssetPath(desiredPath);
                baked.name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    AssetDatabase.CreateAsset(baked, path);
                }
                catch
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                    throw;
                }

                shared = new SharedBake
                {
                    BoneOrder = boneOrder,
                    BakedMesh = baked,
                    OutputPath = path,
                };
                if (!sharedBakes.TryGetValue(source, out List<SharedBake> sourceBakes))
                {
                    sourceBakes = new List<SharedBake>();
                    sharedBakes.Add(source, sourceBakes);
                }

                sourceBakes.Add(shared);
                result.CreatedAssetCount++;
            }

            // NOTE: 続けて適用しても最初のメッシュまで戻せるよう、
            // 差し替え前の参照は最初の 1 回だけ記録する。
            if (target.PreviousMesh == null)
            {
                target.PreviousMesh = renderer.sharedMesh;
            }

            target.OutputPath = shared.OutputPath;
            Undo.RecordObject(renderer, ApplyUndoName);
            renderer.sharedMesh = shared.BakedMesh;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);

            target.ResultLabel = string.Format(
                reused
                    ? "{0}  影響 {1} 頂点  →  {2}（共有）"
                    : "{0}  影響 {1} 頂点  →  {2}",
                target.PathLabel,
                target.AffectedVertexCount,
                shared.OutputPath);
            result.Lines.Add(target.ResultLabel);
            result.AppliedCount++;
        }

        private static SharedBake FindSharedBake(
            Dictionary<Mesh, List<SharedBake>> sharedBakes,
            Mesh source,
            Transform[] boneOrder)
        {
            if (!sharedBakes.TryGetValue(source, out List<SharedBake> candidates))
            {
                return null;
            }

            for (int candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                SharedBake candidate = candidates[candidateIndex];
                // NOTE: 同じ Mesh でも bones の並びが違うとインデックスの意味が変わり、
                // 別のボーンへウェイトが乗るため、並びまで一致する場合だけ共有する。
                if (HasSameBoneOrder(candidate.BoneOrder, boneOrder))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool HasSameBoneOrder(Transform[] first, Transform[] second)
        {
            if (first == null || second == null || first.Length != second.Length)
            {
                return false;
            }

            for (int boneIndex = 0; boneIndex < first.Length; boneIndex++)
            {
                if (first[boneIndex] != second[boneIndex])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
