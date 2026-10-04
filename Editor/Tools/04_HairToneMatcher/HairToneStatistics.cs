using System;
using UnityEngine;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal struct HairToneStats
    {
        internal float Hue;
        internal float Saturation;
        internal float Value;
        internal Color Representative;
        internal int SampleCount;
    }

    internal struct HairToneValueProfile
    {
        internal float P10;
        internal float P25;
        internal float P50;
        internal float P75;
        internal float P90;
        internal float ClipFraction;
        internal bool IsValid;
    }

    internal static class HairToneStatistics
    {
        private const float HueSaturationThreshold = 0.05f;
        private const float DivisionThreshold = 0.001f;
        private const int MinimumSamples = 100;
        private const int IterationCount = 3;
        private const int HistogramSize = 256;
        private const int MaxSolveSamples = 16384;
        private const float GammaMin = 0.4f;
        private const float GammaMax = 2.5f;
        private const float ClipThreshold = 0.995f;
        private const float ClipAllowance = 0.01f;
        private const float LogFloor = 1f / 255f;
        private static readonly float[] GammaQuantiles = { 0.10f, 0.25f, 0.75f, 0.90f };
        private static readonly float[] GammaBlendSteps = { 1f, 0.75f, 0.5f, 0.25f, 0f };

        internal static HairToneValueProfile ComputeValueProfile(
            Color[] pixels, bool[] mask, float alphaThreshold)
        {
            if (pixels == null) return default;
            int count = CountSelected(pixels, mask, alphaThreshold);
            if (count < MinimumSamples) return default;
            var values = new float[count];
            int output = 0;
            int clipped = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (!IsSelected(pixels, mask, i, alphaThreshold)) continue;
                Color.RGBToHSV(pixels[i], out _, out _, out float value);
                values[output++] = value;
                if (value >= ClipThreshold) clipped++;
            }
            Array.Sort(values);
            return new HairToneValueProfile
            {
                P10 = Quantile(values, 0.10f), P25 = Quantile(values, 0.25f),
                P50 = Quantile(values, 0.50f), P75 = Quantile(values, 0.75f),
                P90 = Quantile(values, 0.90f), ClipFraction = (float)clipped / count,
                IsValid = true,
            };
        }

        private static float Quantile(float[] values, float q)
        {
            return values[Mathf.FloorToInt(q * (values.Length - 1))];
        }

        private static float ProfileQuantile(HairToneValueProfile profile, float q)
        {
            if (q == 0.10f) return profile.P10;
            if (q == 0.25f) return profile.P25;
            return q == 0.75f ? profile.P75 : profile.P90;
        }

        internal static HairToneAdjustment SolveStatistics(HairToneStats source,
            HairToneValueProfile sourceValues, Color[] destinationPixels,
            bool[] destinationMask, float alphaThreshold, Color destinationMainColor,
            HairToneShaderProfile destinationProfile, out bool gammaLimited)
        {
            gammaLimited = false;
            if (destinationPixels == null) return HairToneAdjustment.Neutral;
            int count = CountSelected(destinationPixels, destinationMask, alphaThreshold);
            if (count == 0) return HairToneAdjustment.Neutral;
            // NOTE: 対象画素の固定間隔で間引き、同じ入力には必ず同じ補正値を返す。
            int stride = Mathf.Max(1, Mathf.CeilToInt((float)count / MaxSolveSamples));
            var samples = new Color[(count + stride - 1) / stride];
            int selected = 0;
            int output = 0;
            for (int i = 0; i < destinationPixels.Length; i++)
            {
                if (!IsSelected(destinationPixels, destinationMask, i, alphaThreshold)) continue;
                if (selected++ % stride == 0) samples[output++] = destinationPixels[i];
            }
            HairToneValueProfile destination = ComputeValueProfile(samples, null, alphaThreshold);
            float gamma = 1f;
            if (sourceValues.IsValid && destination.IsValid)
            {
                // NOTE: 明度は m × V^γ の形で効くため、対数での中央値まわりの広がりの比を求める。
                float sumXY = 0f;
                float sumXX = 0f;
                foreach (float q in GammaQuantiles)
                {
                    float x = Mathf.Log(Mathf.Max(ProfileQuantile(destination, q), LogFloor)) -
                        Mathf.Log(Mathf.Max(destination.P50, LogFloor));
                    float y = Mathf.Log(Mathf.Max(ProfileQuantile(sourceValues, q), LogFloor)) -
                        Mathf.Log(Mathf.Max(sourceValues.P50, LogFloor));
                    sumXY += x * y;
                    sumXX += x * x;
                }
                gamma = Mathf.Clamp(sumXX < 1e-6f ? 1f : sumXY / sumXX, GammaMin, GammaMax);
            }

            var adjustedPixels = new Color[samples.Length];
            HairToneAdjustment best = HairToneAdjustment.Neutral;
            float bestClip = float.PositiveInfinity;
            float bestT = 1f;
            foreach (float t in GammaBlendSteps)
            {
                HairToneAdjustment candidate = SolveWithGamma(source, samples, adjustedPixels,
                    alphaThreshold, destinationMainColor, destinationProfile, 1f + t * (gamma - 1f));
                int clipped = 0;
                foreach (Color raw in samples)
                {
                    // NOTE: 頭打ちはメインカラーを乗じる前に起きるため、暗い色で白飛びを隠さない。
                    Color.RGBToHSV(HairToneShaderProfile.ApplyToPixel(raw, candidate, destinationProfile),
                        out _, out _, out float value);
                    if (value >= ClipThreshold) clipped++;
                }
                float clipFraction = (float)clipped / samples.Length;
                if (clipFraction < bestClip)
                {
                    best = candidate;
                    bestClip = clipFraction;
                    bestT = t;
                }
                if (clipFraction <= sourceValues.ClipFraction + ClipAllowance)
                {
                    best = candidate;
                    bestT = t;
                    break;
                }
                if (gamma == 1f) break;
            }
            gammaLimited = gamma != 1f && bestT < 1f;
            return best;
        }

        private static HairToneAdjustment SolveWithGamma(HairToneStats source,
            Color[] samples, Color[] adjustedPixels, float alphaThreshold,
            Color mainColor, HairToneShaderProfile profile, float gamma)
        {
            HairToneAdjustment result = HairToneAdjustment.Neutral;
            result.Gamma = gamma;
            // NOTE: 画素ごとのガンマと頭打ちを反映した分布で反復し、作業配列は候補間も使い回す。
            for (int iteration = 0; iteration < IterationCount; iteration++)
            {
                for (int i = 0; i < samples.Length; i++)
                    adjustedPixels[i] = HairToneShaderProfile.MultiplyMainColor(
                        HairToneShaderProfile.ApplyToPixel(samples[i], result, profile), mainColor);
                if (!TryCompute(adjustedPixels, null, alphaThreshold, out HairToneStats stats)) return result;
                // NOTE: 中央値を合わせる反復ではガンマを動かさず、推定した広がりを維持する。
                result = Compose(result, SolveOnce(source, stats));
            }
            return result;
        }

        internal static bool TryCompute(Color[] pixels, bool[] mask,
            float alphaThreshold, out HairToneStats stats)
        {
            stats = default;
            if (pixels == null)
            {
                return false;
            }

            int selected = CountSelected(pixels, mask, alphaThreshold);
            if (selected < MinimumSamples)
            {
                return false;
            }

            var saturations = new float[selected];
            var values = new float[selected];
            double sumSin = 0.0;
            double sumCos = 0.0;
            int hueCount = 0;
            int outputIndex = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (!IsSelected(pixels, mask, i, alphaThreshold))
                {
                    continue;
                }

                Color.RGBToHSV(pixels[i], out float hue, out float saturation, out float value);
                saturations[outputIndex] = saturation;
                values[outputIndex] = value;
                outputIndex++;
                if (saturation >= HueSaturationThreshold)
                {
                    double radians = hue * 2.0 * Math.PI;
                    sumSin += Math.Sin(radians);
                    sumCos += Math.Cos(radians);
                    hueCount++;
                }
            }

            Array.Sort(saturations);
            Array.Sort(values);
            float meanHue = 0f;
            if (hueCount > 0)
            {
                double mean = Math.Atan2(sumSin, sumCos) / (2.0 * Math.PI);
                if (mean < 0.0)
                {
                    mean += 1.0;
                }

                meanHue = (float)mean;
            }

            stats.Hue = meanHue;
            stats.Saturation = Median(saturations);
            stats.Value = Median(values);
            stats.Representative = Color.HSVToRGB(
                stats.Hue, stats.Saturation, stats.Value, true);
            stats.SampleCount = selected;
            return true;
        }

        internal static void ComputeCdf(Color[] pixels, bool[] mask,
            float alphaThreshold, out float[] r, out float[] g, out float[] b)
        {
            r = new float[HistogramSize];
            g = new float[HistogramSize];
            b = new float[HistogramSize];
            if (pixels == null)
            {
                return;
            }

            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (!IsSelected(pixels, mask, i, alphaThreshold))
                {
                    continue;
                }

                r[Mathf.Clamp(Mathf.RoundToInt(pixels[i].r * 255f), 0, 255)]++;
                g[Mathf.Clamp(Mathf.RoundToInt(pixels[i].g * 255f), 0, 255)]++;
                b[Mathf.Clamp(Mathf.RoundToInt(pixels[i].b * 255f), 0, 255)]++;
                count++;
            }

            if (count == 0)
            {
                return;
            }

            for (int i = 1; i < HistogramSize; i++)
            {
                r[i] += r[i - 1];
                g[i] += g[i - 1];
                b[i] += b[i - 1];
            }

            float inverse = 1f / count;
            for (int i = 0; i < HistogramSize; i++)
            {
                r[i] *= inverse;
                g[i] *= inverse;
                b[i] *= inverse;
            }
        }

        internal static HairToneAdjustment Solve(HairToneStats source,
            HairToneStats destinationRaw, Color destinationMainColor,
            HairToneShaderProfile destinationProfile)
        {
            HairToneAdjustment result = HairToneAdjustment.Neutral;
            HairToneStats adjusted = StatsAfter(destinationRaw, result,
                destinationMainColor, destinationProfile);
            for (int i = 0; i < IterationCount; i++)
            {
                HairToneAdjustment step = SolveOnce(source, adjusted);
                result = Compose(result, step);
                adjusted = StatsAfter(destinationRaw, result,
                    destinationMainColor, destinationProfile);
            }

            return result;
        }

        internal static Color PreviewColor(HairToneStats destinationRaw,
            HairToneAdjustment adjustment, Color destinationMainColor,
            HairToneShaderProfile profile)
        {
            return StatsAfter(destinationRaw, adjustment,
                destinationMainColor, profile).Representative;
        }

        private static HairToneAdjustment SolveOnce(HairToneStats source,
            HairToneStats destination)
        {
            float hue = source.Hue - destination.Hue;
            if (hue > 0.5f)
            {
                hue -= 1f;
            }
            else if (hue < -0.5f)
            {
                hue += 1f;
            }

            return new HairToneAdjustment
            {
                Hue = hue,
                Saturation = Mathf.Abs(destination.Saturation) < DivisionThreshold
                    ? 1f : source.Saturation / destination.Saturation,
                Value = Mathf.Abs(destination.Value) < DivisionThreshold
                    ? 1f : source.Value / destination.Value,
                Gamma = 1f,
            };
        }

        private static HairToneAdjustment Compose(HairToneAdjustment current,
            HairToneAdjustment step)
        {
            float hue = current.Hue + step.Hue;
            if (hue > 0.5f)
            {
                hue -= 1f;
            }
            else if (hue < -0.5f)
            {
                hue += 1f;
            }

            return new HairToneAdjustment
            {
                Hue = hue,
                Saturation = current.Saturation * step.Saturation,
                Value = current.Value * step.Value,
                Gamma = current.Gamma * step.Gamma,
            };
        }

        internal static HairToneStats StatsAfter(HairToneStats input,
            HairToneAdjustment adjustment, Color mainColor,
            HairToneShaderProfile profile)
        {
            Color color = HairToneShaderProfile.ApplyToPixel(
                input.Representative, adjustment, profile);
            color = HairToneShaderProfile.MultiplyMainColor(color, mainColor);
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            return new HairToneStats
            {
                Hue = hue,
                Saturation = saturation,
                Value = value,
                Representative = color,
                SampleCount = input.SampleCount,
            };
        }

        private static int CountSelected(Color[] pixels, bool[] mask, float alphaThreshold)
        {
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (IsSelected(pixels, mask, i, alphaThreshold))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsSelected(Color[] pixels, bool[] mask, int index,
            float alphaThreshold)
        {
            return pixels[index].a >= alphaThreshold &&
                (mask == null || (index < mask.Length && mask[index]));
        }

        private static float Median(float[] values)
        {
            int middle = values.Length / 2;
            return values.Length % 2 == 0
                ? (values[middle - 1] + values[middle]) * 0.5f
                : values[middle];
        }
    }
}
