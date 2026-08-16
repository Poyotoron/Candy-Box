using UnityEngine;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal struct HairToneMaskCounts
    {
        internal int Total;
        internal int DroppedByAlpha;
        internal int DroppedByUv;
        internal int DroppedByExistingMask;
        internal int DroppedByUserMask;
        internal int Selected;
    }

    internal static class HairToneRegionMask
    {
        internal static bool[] Build(Renderer renderer, int materialSlot,
            Color[] mainTexPixels, Color[] existingMaskPixels, Color[] userMaskPixels,
            float alphaThreshold, bool useSubmeshUv, int size,
            out HairToneMaskCounts counts)
        {
            return Build(renderer, materialSlot, mainTexPixels, existingMaskPixels,
                userMaskPixels, alphaThreshold, useSubmeshUv, size, size, out counts);
        }

        internal static bool[] Build(Renderer renderer, int materialSlot,
            Color[] mainTexPixels, Color[] existingMaskPixels, Color[] userMaskPixels,
            float alphaThreshold, bool useSubmeshUv, int width, int height,
            out HairToneMaskCounts counts)
        {
            int length = width * height;
            var result = new bool[length];
            bool[] uvCoverage = useSubmeshUv
                ? BuildUvCoverage(renderer, materialSlot, width, height)
                : null;
            counts = new HairToneMaskCounts { Total = length };
            for (int i = 0; i < length; i++)
            {
                if (mainTexPixels == null || i >= mainTexPixels.Length ||
                    mainTexPixels[i].a < alphaThreshold)
                {
                    counts.DroppedByAlpha++;
                }
                else if (uvCoverage != null && !uvCoverage[i])
                {
                    counts.DroppedByUv++;
                }
                else if (existingMaskPixels != null &&
                    (i >= existingMaskPixels.Length || existingMaskPixels[i].r < 0.5f))
                {
                    counts.DroppedByExistingMask++;
                }
                else if (userMaskPixels != null &&
                    (i >= userMaskPixels.Length || userMaskPixels[i].r < 0.5f))
                {
                    counts.DroppedByUserMask++;
                }
                else
                {
                    result[i] = true;
                    counts.Selected++;
                }
            }

            return result;
        }

        internal static bool[] BuildSource(Renderer sourceRenderer, int sourceSlot,
            Color[] sourcePixels, float alphaThreshold, bool useSubmeshUv, int size,
            out HairToneMaskCounts counts)
        {
            int length = size * size;
            var result = new bool[length];
            bool[] uvCoverage = sourceRenderer != null && useSubmeshUv
                ? BuildUvCoverage(sourceRenderer, sourceSlot, size)
                : null;
            counts = new HairToneMaskCounts { Total = length };
            for (int i = 0; i < length; i++)
            {
                if (sourcePixels == null || i >= sourcePixels.Length ||
                    sourcePixels[i].a < alphaThreshold)
                {
                    counts.DroppedByAlpha++;
                }
                else if (uvCoverage != null && !uvCoverage[i])
                {
                    counts.DroppedByUv++;
                }
                else
                {
                    result[i] = true;
                    counts.Selected++;
                }
            }

            return result;
        }

        internal static Texture2D CreateMaskTexture(bool[] mask, int size)
        {
            return CreateMaskTexture(mask, size, size);
        }

        internal static Texture2D CreateMaskTexture(bool[] mask, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            Color32 white = new Color32(255, 255, 255, 255);
            Color32 black = new Color32(0, 0, 0, 0);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = mask != null && i < mask.Length && mask[i] ? white : black;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static bool[] BuildUvCoverage(Renderer renderer, int materialSlot, int size)
        {
            return BuildUvCoverage(renderer, materialSlot, size, size);
        }

        private static bool[] BuildUvCoverage(Renderer renderer, int materialSlot,
            int width, int height)
        {
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                mesh = skinned.sharedMesh;
            }
            else if (renderer is MeshRenderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
            }

            if (mesh == null || materialSlot < 0 || materialSlot >= mesh.subMeshCount)
            {
                return null;
            }

            Vector2[] uv = mesh.uv;
            if (uv == null || uv.Length == 0)
            {
                return null;
            }

            int[] triangles = mesh.GetTriangles(materialSlot);
            var coverage = new bool[width * height];
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int ia = triangles[i];
                int ib = triangles[i + 1];
                int ic = triangles[i + 2];
                if (ia < 0 || ib < 0 || ic < 0 ||
                    ia >= uv.Length || ib >= uv.Length || ic >= uv.Length)
                {
                    continue;
                }

                Vector2 a = WrapUv(uv[ia], width, height);
                Vector2 b = WrapUv(uv[ib], width, height);
                Vector2 c = WrapUv(uv[ic], width, height);
                RasterizeTriangle(coverage, width, height, a, b, c);
            }

            int dilationRadius = Mathf.Max(2, Mathf.Max(width, height) / 256 * 2);
            Dilate(coverage, width, height, dilationRadius);
            return coverage;
        }

        private static Vector2 WrapUv(Vector2 uv, int size)
        {
            return WrapUv(uv, size, size);
        }

        private static Vector2 WrapUv(Vector2 uv, int width, int height)
        {
            return new Vector2(Mathf.Repeat(uv.x, 1f) * width,
                Mathf.Repeat(uv.y, 1f) * height);
        }

        private static void RasterizeTriangle(bool[] mask, int size,
            Vector2 a, Vector2 b, Vector2 c)
        {
            RasterizeTriangle(mask, size, size, a, b, c);
        }

        private static void RasterizeTriangle(bool[] mask, int width, int height,
            Vector2 a, Vector2 b, Vector2 c)
        {
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, width - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, width - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, height - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, height - 1);
            float denominator = (b.y - c.y) * (a.x - c.x) +
                (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(denominator) < Mathf.Epsilon)
            {
                return;
            }

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float w1 = ((b.y - c.y) * (px - c.x) +
                        (c.x - b.x) * (py - c.y)) / denominator;
                    float w2 = ((c.y - a.y) * (px - c.x) +
                        (a.x - c.x) * (py - c.y)) / denominator;
                    float w3 = 1f - w1 - w2;
                    if (w1 >= 0f && w2 >= 0f && w3 >= 0f)
                    {
                        mask[y * width + x] = true;
                    }
                }
            }
        }

        private static void Dilate(bool[] mask, int size, int radius)
        {
            Dilate(mask, size, size, radius);
        }

        private static void Dilate(bool[] mask, int width, int height, int radius)
        {
            bool[] source = (bool[])mask.Clone();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!source[y * width + x])
                    {
                        continue;
                    }

                    int minY = Mathf.Max(0, y - radius);
                    int maxY = Mathf.Min(height - 1, y + radius);
                    int minX = Mathf.Max(0, x - radius);
                    int maxX = Mathf.Min(width - 1, x + radius);
                    for (int dy = minY; dy <= maxY; dy++)
                    {
                        for (int dx = minX; dx <= maxX; dx++)
                        {
                            mask[dy * width + dx] = true;
                        }
                    }
                }
            }
        }
    }
}
