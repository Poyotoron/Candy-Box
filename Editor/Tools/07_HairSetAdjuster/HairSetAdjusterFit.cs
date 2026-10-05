using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.HairSetAdjuster.Editor
{
    internal static class HairSetAdjusterFit
    {
        private const float TargetGap = 0.00342f;
        private const float TargetGapUp = 0.00679f;
        private const float TargetGapForward = -0.00896f;
        private const float SampleSpacing = 0.005f;
        private const int SampleLimit = 6000;
        private const int MinimumRows = 16;
        private const float MeasureMin = -0.03f;
        private const float MeasureMax = 0.04f;
        private const float CoverReach = 0.10f;
        private const float OutwardThreshold = 0.6f;
        private const float FaceNormalThreshold = 0.8f;
        private const float ScalpElevation = 0.1f;
        private const float BackElevation = -0.4f;
        private const float BackNormalThreshold = -0.2f;
        private const float ShapeNormalThreshold = 0.5f;
        private const float PenetrationTolerance = 0.005f;
        private const float FloatTolerance = 0.012f;
        private const float DepthLimit = 20f;
        private const float PenetrationWeight = 30f;
        private const float PenetrationDepthWeight = 0.05f;
        private const float UncoveredWeight = 30f;
        private const float LostWeight = 60f;
        private const float FloatWeight = 0.3f;
        private const float ShapeWeight = 1f;
        private const float ShapeLimit = 15f;
        private const float TranslationPrior = 0.15f;
        private const float ScalePrior = 30f;
        private const float CoarseStep = 0.01f;
        private const float CandidateDistance = 0.015f;
        private const float CandidateScaleDistance = 0.04f;
        private const float PositionStep = 0.008f;
        private const float ScaleStep = 0.04f;
        private const float ImprovementTolerance = 1e-5f;
        private const float MinimumPositionStep = 0.0005f;
        private const float MetersToCentimeters = 100f;
        private const float MetersToMillimeters = 1000f;
        private const int MaximumIterations = 300;
        private const string ProgressMessage = "髪を頭に合わせています…";
        private static readonly float[] StartScales = { 0.85f, 0.90f, 0.95f, 1f, 1.05f, 1.10f, 1.15f };

        private struct Sample
        {
            internal Vector3 Position;
            internal Vector3 Normal;
            internal float TypicalGap;
            internal bool Face;
            internal bool Upper;
            internal bool Scalp;
            internal bool Low;
            internal bool Shape;
        }

        private struct Candidate
        {
            internal Vector3 Delta;
            internal float Scale;
            internal float Cost;
            internal int Order;
        }

        internal static HairSetFitResult Fit(HairSetTarget target, Quaternion rootRotation, bool fitScale)
        {
            var result = new HairSetFitResult();
            try
            {
                EditorUtility.DisplayProgressBar("Candy Box", ProgressMessage, 0f);
                Vector3 up = rootRotation * Vector3.up;
                Vector3 forward = rootRotation * Vector3.forward;
                Vector3 right = rootRotation * Vector3.right;
                Vector3 bias = up * TargetGapUp + forward * TargetGapForward;
                List<Sample> samples = CollectSamples(target, up, forward, bias);
                if (samples.Count < MinimumRows)
                {
                    result.FailureReason = "頭の表面が見つからないため、自動で合わせられません。顔メッシュを確認してください。";
                    return result;
                }

                var points = new List<Vector3>();
                var triangles = new List<int>();
                HairSetAdjusterPenetration.CollectHairMesh(target.Hair, points, triangles);
                if (triangles.Count == 0)
                {
                    result.FailureReason = "髪にメッシュが見つからないため、自動で合わせられません。";
                    return result;
                }

                var context = new FitContext
                {
                    Grid = new HairSetTriangleGrid(points.ToArray(), triangles.ToArray()),
                    Samples = samples,
                    Center = target.HeadCenter,
                    Up = up,
                    Forward = forward,
                    Right = right,
                    Start = new Candidate { Scale = 1f }
                };
                result.FromHeadBone = target.HairHeadBone != null && target.HairHeadBone.IsChildOf(target.AdjustTarget);
                if (result.FromHeadBone)
                {
                    context.Start.Delta = target.HeadBone.position - target.HairHeadBone.position;
                }
                else
                {
                    // NOTE: 頭ボーンが使えない場合は、広い範囲で出発点を求めてから、その付近で整える。
                    context.MarkStart();
                    context.Start = context.Search(fitScale, 40);
                }

                context.PriorTranslation = TranslationPrior;
                context.MarkStart();
                Candidate best = context.Search(fitScale, 8);
                // NOTE: 計算だけでは Transform に書き込まず、適用と Undo は呼び出し側へ任せる。
                result.Succeeded = true;
                result.WorldDelta = best.Delta;
                result.Scale = best.Scale;
                return result;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static List<Sample> CollectSamples(HairSetTarget target, Vector3 up, Vector3 forward, Vector3 bias)
        {
            var samples = new List<Sample>();
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                target.Face.BakeMesh(mesh, true);
                // NOTE: BakeMesh は拡大を焼き込むため、ワールド化では位置と回転だけを使う。
                Matrix4x4 matrix = Matrix4x4.TRS(
                    target.Face.transform.position, target.Face.transform.rotation, Vector3.one);
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                var isHead = new bool[vertices.Length];
                foreach (int index in target.HeadVertexIndices)
                {
                    isHead[index] = true;
                }

                if (normals.Length != vertices.Length)
                {
                    return samples;
                }

                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                    normals[i] = matrix.MultiplyVector(normals[i]).normalized;
                }

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    int[] triangles = mesh.GetTriangles(s);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int a = triangles[i];
                        int b = triangles[i + 1];
                        int c = triangles[i + 2];
                        if (!isHead[a] || !isHead[b] || !isHead[c])
                        {
                            continue;
                        }

                        Vector3 m = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                        Vector3 nn = (normals[a] + normals[b] + normals[c]).normalized;
                        Vector3 d = m - target.HeadCenter;
                        // NOTE: 耳や内側の面は除くが、うなじの被覆も調べるため頭の下半分と顔を残す。
                        if (Vector3.Dot(nn, d.normalized) < OutwardThreshold)
                        {
                            continue;
                        }

                        // NOTE: 頭皮の頂点は疎らなので、面から一定間隔で標本を取る。
                        int k = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(
                            Vector3.Distance(vertices[a], vertices[b]),
                            Mathf.Max(Vector3.Distance(vertices[b], vertices[c]),
                                Vector3.Distance(vertices[c], vertices[a]))) / SampleSpacing));
                        if (k == 1)
                        {
                            AddSample(samples, m, nn, target.HeadCenter, up, forward, bias);
                        }
                        else
                        {
                            for (int x = 1; x < k; x++)
                            {
                                for (int y = 1; y < k - x; y++)
                                {
                                    float u = (float)x / k;
                                    float v = (float)y / k;
                                    AddSample(samples,
                                        vertices[a] + (vertices[b] - vertices[a]) * u + (vertices[c] - vertices[a]) * v,
                                        (normals[a] * (1f - u - v) + normals[b] * u + normals[c] * v).normalized,
                                        target.HeadCenter, up, forward, bias);
                                }
                            }
                        }
                    }
                }

                if (samples.Count > SampleLimit)
                {
                    int stride = Mathf.CeilToInt((float)samples.Count / SampleLimit);
                    var reduced = new List<Sample>();
                    for (int i = 0; i < samples.Count; i += stride)
                    {
                        reduced.Add(samples[i]);
                    }

                    return reduced;
                }

                return samples;
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        private static void AddSample(List<Sample> samples, Vector3 point, Vector3 normal,
            Vector3 center, Vector3 up, Vector3 forward, Vector3 bias)
        {
            // NOTE: 頭頂と生え際では隙間が違うため、目標値を法線の向きで変える。
            Vector3 d = point - center;
            float elevation = Vector3.Dot(d.normalized, up);
            float normalForward = Vector3.Dot(normal, forward);
            bool inFront = Vector3.Dot(d, forward) > 0f;
            bool face = normalForward > FaceNormalThreshold && inFront;
            samples.Add(new Sample
            {
                Position = point,
                Normal = normal,
                TypicalGap = TargetGap + Vector3.Dot(normal, bias),
                Face = face,
                Upper = elevation >= 0f,
                Scalp = !face && (elevation >= ScalpElevation ||
                    (elevation >= BackElevation && normalForward < BackNormalThreshold)),
                Shape = elevation >= 0f && !(normalForward > ShapeNormalThreshold && inFront)
            });
        }

        private sealed class FitContext
        {
            internal HairSetTriangleGrid Grid;
            internal List<Sample> Samples;
            internal Vector3 Center;
            internal Vector3 Right;
            internal Vector3 Up;
            internal Vector3 Forward;
            internal Candidate Start;
            internal float PriorTranslation;
            private bool _refining;
            private int _evaluationCount;

            private void Measure(Sample sample, Candidate pose, out float distance, out bool hidden)
            {
                // NOTE: 髪の格子は一度だけ作り、標本側を逆写像して候補を測る。
                Vector3 local = (sample.Position - Center) / pose.Scale + Center - pose.Delta;
                float t = Grid.RayMin(local, sample.Normal, MeasureMin / pose.Scale, MeasureMax / pose.Scale);
                distance = float.IsNaN(t) ? float.NaN : t * pose.Scale;
                hidden = !float.IsNaN(distance);
                if (!hidden && (sample.Scalp || sample.Low))
                {
                    // NOTE: 頭から浮いた髪でも肌を隠すことがあるため、外側の遠い面も調べる。
                    hidden = !float.IsNaN(Grid.RayMin(
                        local, sample.Normal, MeasureMax / pose.Scale, CoverReach / pose.Scale));
                }
            }

            internal void MarkStart()
            {
                for (int i = 0; i < Samples.Count; i++)
                {
                    Sample sample = Samples[i];
                    // NOTE: 被覆を確定する前は下側の候補全体を 10cm まで調べ、遠い髪に隠れた肌も守る。
                    // NOTE: 出発点が変わったら、以前の被覆集合を引き継がずに測り直す。
                    bool lowCandidate = !sample.Face && !sample.Scalp;
                    sample.Low = lowCandidate;
                    Measure(sample, Start, out _, out bool hidden);
                    sample.Low = lowCandidate && hidden;
                    Samples[i] = sample;
                }
            }

            private float Evaluate(Candidate pose, int stride)
            {
                if (_refining)
                {
                    EditorUtility.DisplayProgressBar("Candy Box", ProgressMessage,
                        0.3f + 0.7f * Mathf.Min(1f, _evaluationCount++ / 7500f));
                }

                int nUpper = 0;
                int nScalp = 0;
                int nLow = 0;
                int nShape = 0;
                int penCount = 0;
                float penDepth = 0f;
                int uncovered = 0;
                int lost = 0;
                float floatSum = 0f;
                float shapeSum = 0f;
                for (int i = 0; i < Samples.Count; i += stride)
                {
                    Sample sample = Samples[i];
                    if (!sample.Upper && !sample.Scalp && !sample.Low && !sample.Shape)
                    {
                        continue;
                    }

                    Measure(sample, pose, out float t, out bool hidden);
                    // NOTE: 下端をうなじや耳の後ろへ沈める髪を動かさないよう、めり込みは頭の上半分だけ数える。
                    if (sample.Upper)
                    {
                        nUpper++;
                        if (t < -PenetrationTolerance)
                        {
                            penCount++;
                            float depth = Mathf.Min((-t - PenetrationTolerance) * MetersToMillimeters, DepthLimit);
                            penDepth += depth * depth;
                        }
                    }

                    if (sample.Scalp)
                    {
                        nScalp++;
                        if (!hidden)
                        {
                            uncovered++;
                        }

                        if (!float.IsNaN(t) && t > FloatTolerance)
                        {
                            float depth = Mathf.Min((t - FloatTolerance) * MetersToMillimeters, DepthLimit);
                            floatSum += depth * depth;
                        }
                    }

                    if (sample.Low)
                    {
                        nLow++;
                        if (!hidden)
                        {
                            lost++;
                        }
                    }

                    if (sample.Shape)
                    {
                        nShape++;
                        // NOTE: 典型の隙間は補助に留め、作者が広く作った髪を無理に縮めない。
                        shapeSum += float.IsNaN(t)
                            ? ShapeLimit
                            : Mathf.Min(Mathf.Abs(t - sample.TypicalGap) * MetersToMillimeters, ShapeLimit);
                    }
                }

                float logScale = Mathf.Log(pose.Scale / Start.Scale);
                return PenetrationWeight * Ratio(penCount, nUpper) +
                    PenetrationDepthWeight * Ratio(penDepth, nUpper) +
                    UncoveredWeight * Ratio(uncovered, nScalp) +
                    LostWeight * Ratio(lost, nLow) +
                    FloatWeight * Ratio(floatSum, nScalp) +
                    ShapeWeight * Ratio(shapeSum, nShape) +
                    PriorTranslation * ((pose.Delta - Start.Delta) * MetersToCentimeters).sqrMagnitude +
                    ScalePrior * logScale * logScale;
            }

            private static float Ratio(float sum, int count)
            {
                return count == 0 ? 0f : sum / count;
            }

            internal Candidate Search(bool fitScale, int range)
            {
                _refining = false;
                int stride = Mathf.Max(1, Samples.Count / 400);
                var candidates = new List<Candidate>();
                int order = 0;
                int total = (fitScale ? StartScales.Length : 1) * (2 * range + 1) * 3;
                foreach (float s in StartScales)
                {
                    // NOTE: 位置だけを合わせる場合は、粗い走査でも倍率を変えない。
                    if (!fitScale && s != 1f)
                    {
                        continue;
                    }

                    for (int iy = -range; iy <= range; iy++)
                    {
                        for (int iz = -1; iz <= 1; iz++)
                        {
                            EditorUtility.DisplayProgressBar("Candy Box", ProgressMessage, 0.3f * order / total);
                            var candidate = new Candidate
                            {
                                Delta = Start.Delta + Up * (CoarseStep * iy) + Forward * (CoarseStep * iz),
                                Scale = Start.Scale * s,
                                Order = order++
                            };
                            candidate.Cost = Evaluate(candidate, stride);
                            candidates.Add(candidate);
                        }
                    }
                }

                // NOTE: 同点は走査順で決め、安定でない並べ替えによって結果が変わるのを防ぐ。
                candidates.Sort(CompareCandidates);
                var starts = new List<Candidate>();
                foreach (Candidate candidate in candidates)
                {
                    bool separated = true;
                    foreach (Candidate chosen in starts)
                    {
                        if (Vector3.Distance(candidate.Delta, chosen.Delta) <= CandidateDistance &&
                            Mathf.Abs(Mathf.Log(candidate.Scale / chosen.Scale)) <= CandidateScaleDistance)
                        {
                            separated = false;
                            break;
                        }
                    }

                    if (separated)
                    {
                        starts.Add(candidate);
                        if (starts.Count == 2)
                        {
                            break;
                        }
                    }
                }

                bool containsStart = false;
                foreach (Candidate candidate in starts)
                {
                    if ((candidate.Delta - Start.Delta).sqrMagnitude == 0f && candidate.Scale == Start.Scale)
                    {
                        containsStart = true;
                        break;
                    }
                }

                // NOTE: 合っている髪を無理に動かさないよう、出発点も改善候補に含める。
                if (!containsStart)
                {
                    starts.Insert(0, Start);
                }

                _refining = true;
                _evaluationCount = 0;
                Candidate best = default;
                float bestCost = float.PositiveInfinity;
                foreach (Candidate candidate in starts)
                {
                    Candidate refined = CoordinateSearch(candidate, fitScale, Mathf.Max(1, Samples.Count / 1000));
                    float cost = Evaluate(refined, 1);
                    if (cost < bestCost)
                    {
                        best = refined;
                        bestCost = cost;
                    }
                }

                best = CoordinateSearch(best, fitScale, Mathf.Max(1, Samples.Count / 4000));
                EditorUtility.DisplayProgressBar("Candy Box", ProgressMessage, 1f);
                return best;
            }

            private static int CompareCandidates(Candidate a, Candidate b)
            {
                int comparison = a.Cost.CompareTo(b.Cost);
                return comparison != 0 ? comparison : a.Order.CompareTo(b.Order);
            }

            private Candidate CoordinateSearch(Candidate start, bool fitScale, int stride)
            {
                float factor = 1f;
                Candidate current = start;
                float currentCost = Evaluate(current, stride);
                for (int iteration = 0; iteration < MaximumIterations; iteration++)
                {
                    Candidate best = current;
                    float bestCost = float.PositiveInfinity;
                    int count = fitScale ? 8 : 6;
                    for (int i = 0; i < count; i++)
                    {
                        Candidate candidate = current;
                        if (i < 6)
                        {
                            Vector3 axis = i < 2 ? Right : i < 4 ? Up : Forward;
                            candidate.Delta += axis * (PositionStep * factor * (i % 2 == 0 ? 1f : -1f));
                        }
                        else
                        {
                            candidate.Scale *= Mathf.Exp(ScaleStep * factor * (i == 6 ? 1f : -1f));
                        }

                        float cost = Evaluate(candidate, stride);
                        if (cost < bestCost)
                        {
                            best = candidate;
                            bestCost = cost;
                        }
                    }

                    if (bestCost < currentCost - ImprovementTolerance)
                    {
                        current = best;
                        currentCost = bestCost;
                    }
                    else
                    {
                        factor *= 0.5f;
                        if (PositionStep * factor < MinimumPositionStep)
                        {
                            break;
                        }
                    }
                }

                return current;
            }
        }
    }
}
