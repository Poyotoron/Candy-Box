using UnityEngine;

namespace Poyo.CandyBox.HairToneMatcher.Editor
{
    internal static class HairTonePixelSampler
    {
        internal static Color[] Read(Texture texture, int size)
        {
            return Read(texture, size, size);
        }

        internal static Color[] Read(Texture texture, int width, int height)
        {
            if (texture == null)
            {
                return null;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture renderTexture = null;
            Texture2D readable = null;
            try
            {
                renderTexture = RenderTexture.GetTemporary(width, height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(texture, renderTexture);
                RenderTexture.active = renderTexture;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                readable.Apply(false, false);
                return readable.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }

                if (readable != null)
                {
                    Object.DestroyImmediate(readable);
                }
            }
        }
    }
}
