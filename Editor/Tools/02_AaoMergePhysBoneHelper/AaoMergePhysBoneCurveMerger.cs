using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.AaoMergePhysBoneHelper.Editor
{
    internal static class AaoMergePhysBoneCurveMerger
    {
        private const int SampleCount = 11;
        private const string SignChangeReason =
            "実効値の符号が途中で変わるため、値とカーブの組では表現できません。";

        internal static bool TryMerge(
            float[] values,
            AnimationCurve[] curves,
            AaoMergePhysBoneMetric metric,
            AaoMergePhysBoneProperty property,
            out float mergedValue,
            out AnimationCurve mergedCurve,
            out string blockedReason)
        {
            mergedValue = 0f;
            mergedCurve = null;
            blockedReason = null;
            if (values == null || values.Length == 0)
            {
                mergedCurve = new AnimationCurve();
                return true;
            }

            var profile = new float[SampleCount];
            var samples = new float[values.Length];
            bool hasPositive = false;
            bool hasNegative = false;
            for (int sampleIndex = 0; sampleIndex < SampleCount; sampleIndex++)
            {
                float time = sampleIndex / (float)(SampleCount - 1);
                for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                {
                    samples[valueIndex] = Evaluate(values[valueIndex], curves[valueIndex], time);
                }

                float value = AaoMergePhysBoneStatistics.Compute(metric, samples);
                profile[sampleIndex] = value;
                hasPositive |= value > 0f;
                hasNegative |= value < 0f;
            }

            if (hasPositive && hasNegative)
            {
                blockedReason = SignChangeReason;
                return false;
            }

            int greatestIndex = 0;
            for (int sampleIndex = 1; sampleIndex < profile.Length; sampleIndex++)
            {
                if (Mathf.Abs(profile[sampleIndex]) > Mathf.Abs(profile[greatestIndex]))
                {
                    greatestIndex = sampleIndex;
                }
            }

            mergedValue = profile[greatestIndex];
            if (property.HasRange)
            {
                mergedValue = Mathf.Clamp(mergedValue, property.RangeMin, property.RangeMax);
            }

            if (mergedValue == 0f)
            {
                mergedCurve = new AnimationCurve();
                return true;
            }

            var ratios = new float[SampleCount];
            bool isConstant = true;
            for (int sampleIndex = 0; sampleIndex < SampleCount; sampleIndex++)
            {
                ratios[sampleIndex] = profile[sampleIndex] / mergedValue;
                if (Mathf.Abs(ratios[sampleIndex] - 1f) > 0.0001f)
                {
                    isConstant = false;
                }
            }

            mergedCurve = isConstant ? new AnimationCurve() : CreateLinearCurve(ratios);
            return true;
        }

        private static float Evaluate(float value, AnimationCurve curve, float time)
        {
            return curve == null || curve.length == 0
                ? value
                : value * curve.Evaluate(time);
        }

        private static AnimationCurve CreateLinearCurve(float[] ratios)
        {
            var curve = new AnimationCurve();
            for (int sampleIndex = 0; sampleIndex < SampleCount; sampleIndex++)
            {
                float time = sampleIndex / (float)(SampleCount - 1);
                curve.AddKey(new Keyframe(time, ratios[sampleIndex]));
            }

            for (int keyIndex = 0; keyIndex < curve.length; keyIndex++)
            {
                AnimationUtility.SetKeyLeftTangentMode(
                    curve, keyIndex, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(
                    curve, keyIndex, AnimationUtility.TangentMode.Linear);
            }

            UpdateTangentsFromMode(curve);

            return curve;
        }

        private static void UpdateTangentsFromMode(AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                Keyframe key = keys[keyIndex];
                key.inTangent = keyIndex > 0
                    ? CalculateSlope(keys[keyIndex - 1], key)
                    : keys.Length > 1 ? CalculateSlope(key, keys[1]) : 0f;
                key.outTangent = keyIndex + 1 < keys.Length
                    ? CalculateSlope(key, keys[keyIndex + 1])
                    : keyIndex > 0 ? CalculateSlope(keys[keyIndex - 1], key) : 0f;
                curve.MoveKey(keyIndex, key);
            }
        }

        private static float CalculateSlope(Keyframe left, Keyframe right)
        {
            float duration = right.time - left.time;
            return duration != 0f ? (right.value - left.value) / duration : 0f;
        }
    }
}
