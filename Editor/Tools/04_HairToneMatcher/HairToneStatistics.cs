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

    internal static class HairToneStatistics
    {
        private const float HueSaturationThreshold = 0.05f;
        private const float DivisionThreshold = 0.001f;
        private const int MinimumSamples = 100;
        private const int IterationCount = 3;
        private const int HistogramSize = 256;

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
