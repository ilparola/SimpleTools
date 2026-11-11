using UnityEngine;

namespace SimpleTools.Media
{
    /// <summary>Small image helpers for player pictures (avatars, thumbnails).</summary>
    public static class ImageTools
    {
        /// <summary>
        /// Loads a PNG or JPEG from bytes and returns the centred square crop scaled to size × size, or null if the
        /// bytes are not an image Unity can read (HEIC, WebP...). Main thread only (uses the GPU to scale).
        /// </summary>
        public static Texture2D SquareThumbnail(byte[] bytes, int size)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (bytes == null || !source.LoadImage(bytes))
            {
                Object.Destroy(source);
                return null;
            }

            Texture2D result = SquareThumbnail(source, size);
            Object.Destroy(source);
            return result;
        }

        public static Texture2D SquareThumbnail(Texture2D source, int size)
        {
            int side = Mathf.Min(source.width, source.height);
            var scale = new Vector2((float)side / source.width, (float)side / source.height);
            var offset = new Vector2((1f - scale.x) * 0.5f, (1f - scale.y) * 0.5f);
            RenderTexture target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, target, scale, offset);
            RenderTexture.active = target;
            var result = new Texture2D(size, size, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return result;
        }
    }
}
