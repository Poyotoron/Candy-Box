using System;
using System.Text;
using UnityEngine;

namespace Poyo.CandyBox.AaoMergePhysBoneHelper.Editor
{
    internal enum AaoMergePhysBoneMetric
    {
        Min,
        Max,
        Mean,
        Median,
        Mode,
    }

    internal static class AaoMergePhysBoneStatistics
    {
        private const string CurveSuffix = "（カーブあり）";
        private const string ChainLengthWarning =
            "統合対象でチェーンの長さが異なります。カーブの提案は目安です。";

        internal static readonly string[] MetricDisplayNames =
        {
            "最小", "最大", "平均", "中央", "最頻",
        };

        private static readonly AaoMergePhysBoneMetric[] NumericMetrics =
        {
            AaoMergePhysBoneMetric.Min,
            AaoMergePhysBoneMetric.Max,
            AaoMergePhysBoneMetric.Mean,
            AaoMergePhysBoneMetric.Median,
            AaoMergePhysBoneMetric.Mode,
        };

        private static readonly AaoMergePhysBoneMetric[] ModeMetric =
        {
            AaoMergePhysBoneMetric.Mode,
        };

        private static readonly AaoMergePhysBoneMetric[] NoMetrics =
            Array.Empty<AaoMergePhysBoneMetric>();

        private static readonly string[] NumericMetricNames =
        {
            "最小", "最大", "平均", "中央", "最頻",
        };

        private static readonly string[] ModeMetricName = { "最頻" };
        private static readonly string[] NoMetricNames = Array.Empty<string>();

        internal static AaoMergePhysBoneMetric[] GetAvailableMetrics(
            AaoMergePhysBoneValueKind kind)
        {
            switch (kind)
            {
                case AaoMergePhysBoneValueKind.Float:
                case AaoMergePhysBoneValueKind.Vector3:
                    return NumericMetrics;
                case AaoMergePhysBoneValueKind.Bool:
                case AaoMergePhysBoneValueKind.Enum:
                case AaoMergePhysBoneValueKind.Permission:
                    return ModeMetric;
                default:
                    return NoMetrics;
            }
        }

        internal static string[] GetAvailableMetricNames(AaoMergePhysBoneValueKind kind)
        {
            switch (kind)
            {
                case AaoMergePhysBoneValueKind.Float:
                case AaoMergePhysBoneValueKind.Vector3:
                    return NumericMetricNames;
                case AaoMergePhysBoneValueKind.Bool:
                case AaoMergePhysBoneValueKind.Enum:
                case AaoMergePhysBoneValueKind.Permission:
                    return ModeMetricName;
                default:
                    return NoMetricNames;
            }
        }

        internal static float Compute(AaoMergePhysBoneMetric metric, float[] values)
        {
            if (values == null || values.Length == 0)
            {
                return 0f;
            }

            switch (metric)
            {
                case AaoMergePhysBoneMetric.Min:
                    return ComputeMinimum(values);
                case AaoMergePhysBoneMetric.Max:
                    return ComputeMaximum(values);
                case AaoMergePhysBoneMetric.Mean:
                    float sum = 0f;
                    for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                    {
                        sum += values[valueIndex];
                    }

                    return sum / values.Length;
                case AaoMergePhysBoneMetric.Median:
                    var sorted = (float[])values.Clone();
                    Array.Sort(sorted);
                    int middle = sorted.Length / 2;
                    return sorted.Length % 2 == 0
                        ? (sorted[middle - 1] + sorted[middle]) / 2f
                        : sorted[middle];
                case AaoMergePhysBoneMetric.Mode:
                    return ComputeMode(values);
                default:
                    return 0f;
            }
        }

        internal static float ComputeMode(float[] values)
        {
            if (values == null || values.Length == 0)
            {
                return 0f;
            }

            float bestValue = values[0];
            int bestCount = 0;
            for (int candidateIndex = 0; candidateIndex < values.Length; candidateIndex++)
            {
                float candidate = values[candidateIndex];
                int count = 0;
                for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                {
                    if (values[valueIndex] == candidate)
                    {
                        count++;
                    }
                }

                if (count > bestCount || count == bestCount && candidate < bestValue)
                {
                    bestValue = candidate;
                    bestCount = count;
                }
            }

            return bestValue;
        }

        internal static int ComputeMode(int[] values)
        {
            if (values == null || values.Length == 0)
            {
                return 0;
            }

            int bestValue = values[0];
            int bestCount = 0;
            for (int candidateIndex = 0; candidateIndex < values.Length; candidateIndex++)
            {
                int candidate = values[candidateIndex];
                int count = 0;
                for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                {
                    if (values[valueIndex] == candidate)
                    {
                        count++;
                    }
                }

                if (count > bestCount || count == bestCount && candidate < bestValue)
                {
                    bestValue = candidate;
                    bestCount = count;
                }
            }

            return bestValue;
        }

        internal static void Recompute(
            AaoMergePhysBonePropertyPlan plan, AaoMergePhysBoneMetric metric)
        {
            plan.Blocked = false;
            plan.BlockedReason = null;
            AaoMergePhysBoneSuggestion suggestion = CreateSuggestion(plan, metric);
            if (suggestion == null)
            {
                plan.Selected = false;
                plan.Suggestion = null;
                plan.BlockedDisplayText =
                    plan.Property.DisplayName + ": " + plan.BlockedReason;
                RefreshHeader(plan);
                return;
            }

            suggestion.NormalizePending = true;
            plan.Suggestion = suggestion;
            plan.BlockedDisplayText = null;
            RefreshDisplayText(plan);
        }

        internal static void BuildStatisticsText(AaoMergePhysBonePropertyPlan plan)
        {
            AaoMergePhysBoneMetric[] metrics = GetAvailableMetrics(plan.Property.Kind);
            var builder = new StringBuilder();
            for (int metricIndex = 0; metricIndex < metrics.Length; metricIndex++)
            {
                AaoMergePhysBoneSuggestion suggestion = CreateSuggestion(plan, metrics[metricIndex]);
                if (suggestion == null)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(" / ");
                }

                builder.Append(MetricDisplayNames[(int)metrics[metricIndex]]);
                builder.Append(' ');
                builder.Append(FormatSuggestion(plan, suggestion));
            }

            plan.StatisticsText = builder.ToString();
        }

        internal static void RefreshDisplayText(AaoMergePhysBonePropertyPlan plan)
        {
            if (plan.Suggestion != null)
            {
                plan.Suggestion.DisplayText = FormatSuggestion(plan, plan.Suggestion);
            }

            RefreshHeader(plan);
        }

        private static AaoMergePhysBoneSuggestion CreateSuggestion(
            AaoMergePhysBonePropertyPlan plan, AaoMergePhysBoneMetric metric)
        {
            var suggestion = new AaoMergePhysBoneSuggestion
            {
                Metric = metric,
                NormalizePending = true,
            };
            int valueCount = plan.Values.Count;
            switch (plan.Property.Kind)
            {
                case AaoMergePhysBoneValueKind.Float:
                    var floats = new float[valueCount];
                    for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
                    {
                        floats[valueIndex] = plan.Values[valueIndex].Float;
                    }

                    if (plan.Property.CurveFieldName != null)
                    {
                        var curves = new AnimationCurve[valueCount];
                        for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
                        {
                            curves[valueIndex] = plan.Values[valueIndex].Curve;
                        }

                        if (!AaoMergePhysBoneCurveMerger.TryMerge(
                                floats, curves, metric, plan.Property,
                                out suggestion.Float, out suggestion.Curve,
                                out string blockedReason))
                        {
                            plan.Blocked = true;
                            plan.BlockedReason = blockedReason;
                            return null;
                        }

                        if (plan.ChainLengthDiffers)
                        {
                            suggestion.Warning = ChainLengthWarning;
                        }
                    }
                    else
                    {
                        suggestion.Float = Clamp(plan.Property, Compute(metric, floats));
                    }

                    break;
                case AaoMergePhysBoneValueKind.Vector3:
                    if (!TryCreateVectorSuggestion(plan, metric, suggestion))
                    {
                        return null;
                    }

                    break;
                case AaoMergePhysBoneValueKind.Bool:
                case AaoMergePhysBoneValueKind.Enum:
                    var integers = new int[valueCount];
                    for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
                    {
                        integers[valueIndex] = plan.Values[valueIndex].Int;
                    }

                    suggestion.Int = ComputeMode(integers);
                    break;
                case AaoMergePhysBoneValueKind.Permission:
                    var permissions = new int[valueCount];
                    for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
                    {
                        AaoMergePhysBoneValue value = plan.Values[valueIndex];
                        permissions[valueIndex] = value.Int * 4 + value.FilterInt;
                    }

                    int permission = ComputeMode(permissions);
                    suggestion.Int = permission / 4;
                    suggestion.FilterInt = permission % 4;
                    break;
                default:
                    return null;
            }

            return suggestion;
        }

        private static bool TryCreateVectorSuggestion(
            AaoMergePhysBonePropertyPlan plan,
            AaoMergePhysBoneMetric metric,
            AaoMergePhysBoneSuggestion suggestion)
        {
            int count = plan.Values.Count;
            var xValues = new float[count];
            var yValues = new float[count];
            var zValues = new float[count];
            var xCurves = new AnimationCurve[count];
            var yCurves = new AnimationCurve[count];
            var zCurves = new AnimationCurve[count];
            for (int valueIndex = 0; valueIndex < count; valueIndex++)
            {
                AaoMergePhysBoneValue value = plan.Values[valueIndex];
                xValues[valueIndex] = value.Vector.x;
                yValues[valueIndex] = value.Vector.y;
                zValues[valueIndex] = value.Vector.z;
                xCurves[valueIndex] = value.Curve;
                yCurves[valueIndex] = value.CurveY;
                zCurves[valueIndex] = value.CurveZ;
            }

            if (!AaoMergePhysBoneCurveMerger.TryMerge(
                    xValues, xCurves, metric, plan.Property,
                    out float x, out suggestion.Curve, out string reason) ||
                !AaoMergePhysBoneCurveMerger.TryMerge(
                    yValues, yCurves, metric, plan.Property,
                    out float y, out suggestion.CurveY, out reason) ||
                !AaoMergePhysBoneCurveMerger.TryMerge(
                    zValues, zCurves, metric, plan.Property,
                    out float z, out suggestion.CurveZ, out reason))
            {
                plan.Blocked = true;
                plan.BlockedReason = reason;
                return false;
            }

            suggestion.Vector = new Vector3(x, y, z);
            if (plan.ChainLengthDiffers)
            {
                suggestion.Warning = ChainLengthWarning;
            }

            return true;
        }

        private static float ComputeMinimum(float[] values)
        {
            float result = values[0];
            for (int valueIndex = 1; valueIndex < values.Length; valueIndex++)
            {
                result = Mathf.Min(result, values[valueIndex]);
            }

            return result;
        }

        private static float ComputeMaximum(float[] values)
        {
            float result = values[0];
            for (int valueIndex = 1; valueIndex < values.Length; valueIndex++)
            {
                result = Mathf.Max(result, values[valueIndex]);
            }

            return result;
        }

        private static float Clamp(AaoMergePhysBoneProperty property, float value)
        {
            return property.HasRange
                ? Mathf.Clamp(value, property.RangeMin, property.RangeMax)
                : value;
        }

        private static string FormatSuggestion(
            AaoMergePhysBonePropertyPlan plan, AaoMergePhysBoneSuggestion suggestion)
        {
            switch (plan.Property.Kind)
            {
                case AaoMergePhysBoneValueKind.Float:
                    string floatText = suggestion.Float.ToString("0.###");
                    return suggestion.Curve != null && suggestion.Curve.length > 0
                        ? floatText + CurveSuffix
                        : floatText;
                case AaoMergePhysBoneValueKind.Vector3:
                    string vectorText = string.Format(
                        "({0}, {1}, {2})",
                        suggestion.Vector.x.ToString("0.###"),
                        suggestion.Vector.y.ToString("0.###"),
                        suggestion.Vector.z.ToString("0.###"));
                    return HasAnyCurve(suggestion) ? vectorText + CurveSuffix : vectorText;
                case AaoMergePhysBoneValueKind.Bool:
                    return suggestion.Int != 0 ? "有効" : "無効";
                case AaoMergePhysBoneValueKind.Enum:
                    return GetEnumDisplayName(plan, suggestion.Int);
                case AaoMergePhysBoneValueKind.Permission:
                    return GetEnumDisplayName(plan, suggestion.Int) + " / " +
                        FormatFilter(suggestion.FilterInt);
                default:
                    return string.Empty;
            }
        }

        private static bool HasAnyCurve(AaoMergePhysBoneSuggestion suggestion)
        {
            return suggestion.Curve != null && suggestion.Curve.length > 0 ||
                suggestion.CurveY != null && suggestion.CurveY.length > 0 ||
                suggestion.CurveZ != null && suggestion.CurveZ.length > 0;
        }

        private static string GetEnumDisplayName(AaoMergePhysBonePropertyPlan plan, int index)
        {
            return plan.EnumDisplayNames != null && index >= 0 && index < plan.EnumDisplayNames.Length
                ? plan.EnumDisplayNames[index]
                : index.ToString();
        }

        private static string FormatFilter(int filter)
        {
            switch (filter)
            {
                case 1:
                    return "Self";
                case 2:
                    return "Others";
                case 3:
                    return "Self, Others";
                default:
                    return "なし";
            }
        }

        private static void RefreshHeader(AaoMergePhysBonePropertyPlan plan)
        {
            string suggestionText = plan.Suggestion != null
                ? plan.Suggestion.DisplayText
                : plan.BlockedReason;
            plan.HeaderText = string.Format(
                "{0}    {1}    {2}",
                plan.Property.DisplayName,
                plan.CurrentOverrideText,
                suggestionText);
        }
    }
}
