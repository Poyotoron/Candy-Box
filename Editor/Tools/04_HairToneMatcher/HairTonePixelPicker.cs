using UnityEngine;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal struct HairTonePickedPoint
    {
        internal bool HasValue;
        internal Vector2 Uv;
        internal Color Color;
        internal bool InsideMask;
    }

    internal static class HairTonePixelPicker
    {
        private const int PickRadius = 2;

        internal static bool TryPick(Color[] pixels, bool[] mask, int size,
            Vector2 uv, float alphaThreshold, out HairTonePickedPoint point)
        {
            point = default;
            if (pixels == null || pixels.Length < size * size)
            {
                return false;
            }

            int centerX = Mathf.Clamp(Mathf.FloorToInt(uv.x * size), 0, size - 1);
            int centerY = Mathf.Clamp(Mathf.FloorToInt(uv.y * size), 0, size - 1);
            Color sum = Color.clear;
            int count = 0;
            for (int y = Mathf.Max(0, centerY - PickRadius);
                 y <= Mathf.Min(size - 1, centerY + PickRadius); y++)
            {
                for (int x = Mathf.Max(0, centerX - PickRadius);
                     x <= Mathf.Min(size - 1, centerX + PickRadius); x++)
                {
                    Color color = pixels[y * size + x];
                    if (color.a < alphaThreshold)
                    {
                        continue;
                    }

                    sum += color;
                    count++;
                }
            }

            if (count == 0)
            {
                return false;
            }

            int centerIndex = centerY * size + centerX;
            point.HasValue = true;
            point.Uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            point.Color = sum / count;
            point.InsideMask = mask == null ||
                (centerIndex < mask.Length && mask[centerIndex]);
            return true;
        }

        internal static bool TryGetUv(Rect drawRect, Vector2 mousePosition,
            out Vector2 uv)
        {
            uv = default;
            if (drawRect.width <= 0f || drawRect.height <= 0f ||
                !drawRect.Contains(mousePosition))
            {
                return false;
            }

            uv.x = (mousePosition.x - drawRect.x) / drawRect.width;
            uv.y = 1f - (mousePosition.y - drawRect.y) / drawRect.height;
            return true;
        }
    }
}
