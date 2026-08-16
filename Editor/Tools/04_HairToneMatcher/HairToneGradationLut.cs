using UnityEngine;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal static class HairToneGradationLut
    {
        private const int Size = 256;

        internal static Texture2D Build(float[] srcR, float[] srcG, float[] srcB,
            float[] dstR, float[] dstG, float[] dstB)
        {
            var texture = new Texture2D(Size, 1, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[Size];
            for (int i = 0; i < Size; i++)
            {
                pixels[i] = new Color(
                    FindOutput(srcR, ValueAt(dstR, i)),
                    FindOutput(srcG, ValueAt(dstG, i)),
                    FindOutput(srcB, ValueAt(dstB, i)),
                    1f);
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        internal static Color ApplyToPixel(Color color, Texture2D lut)
        {
            if (lut == null)
            {
                return color;
            }

            Color result = new Color(
                lut.GetPixelBilinear(Mathf.Clamp01(color.r), 0.5f).r,
                lut.GetPixelBilinear(Mathf.Clamp01(color.g), 0.5f).g,
                lut.GetPixelBilinear(Mathf.Clamp01(color.b), 0.5f).b,
                color.a);
            return result;
        }

        private static float FindOutput(float[] sourceCdf, float target)
        {
            if (sourceCdf == null || sourceCdf.Length == 0)
            {
                return 0f;
            }

            int limit = Mathf.Min(Size, sourceCdf.Length);
            for (int i = 0; i < limit; i++)
            {
                if (sourceCdf[i] >= target)
                {
                    return i / 255f;
                }
            }

            return 1f;
        }

        private static float ValueAt(float[] values, int index)
        {
            return values != null && index < values.Length ? values[index] : 0f;
        }
    }
}
