using System;
using System.IO;
using UnityEngine;

namespace SimpleTools.Media
{
    /// <summary>
    /// Copies an exported file (image, GIF or video) into the device gallery so the player finds it with the other
    /// photos and videos and can share it from there. Android 10+ (API 29) uses MediaStore with a relative path, which
    /// needs no storage permission; elsewhere it does nothing and returns false, so the caller keeps its own file.
    /// </summary>
    public static class MediaGallery
    {
        public static bool IsSupported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    return version.GetStatic<int>("SDK_INT") >= 29;
                }
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Copies <paramref name="path"/> into Pictures/<paramref name="album"/> (images and GIFs) or
        /// Movies/<paramref name="album"/> (videos). Returns true when the gallery has the file.
        /// </summary>
        public static bool Save(string path, string mimeType, string album)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsSupported || !File.Exists(path))
            {
                return false;
            }

            bool video = mimeType.StartsWith("video/", StringComparison.Ordinal);
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var values = new AndroidJavaObject("android.content.ContentValues"))
                using (var media = new AndroidJavaClass(video ? "android.provider.MediaStore$Video$Media" : "android.provider.MediaStore$Images$Media"))
                using (AndroidJavaObject collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
                {
                    values.Call("put", "_display_name", Path.GetFileName(path));
                    values.Call("put", "mime_type", mimeType);
                    values.Call("put", "relative_path", (video ? "Movies/" : "Pictures/") + album);
                    values.Call("put", "is_pending", Integer(1));
                    using (AndroidJavaObject uri = resolver.Call<AndroidJavaObject>("insert", collection, values))
                    {
                        if (uri == null)
                        {
                            return false;
                        }

                        using (AndroidJavaObject stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                        using (FileStream source = File.OpenRead(path))
                        {
                            // In blocks: a whole video would be a huge Java array
                            var buffer = new byte[1 << 20];
                            int read;
                            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                stream.Call("write", ToJava(buffer, read), 0, read);
                            }

                            stream.Call("close");
                        }

                        values.Call("clear");
                        values.Call("put", "is_pending", Integer(0));
                        resolver.Call<int>("update", uri, values, null, null);
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"MediaGallery: could not save {path} to the gallery ({exception.Message}).");
                return false;
            }
#else
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject Integer(int value) => new AndroidJavaObject("java.lang.Integer", value);

        // byte[] goes to Java as byte[] (signed): only the part actually read
        private static sbyte[] ToJava(byte[] buffer, int count)
        {
            var copy = new sbyte[count];
            Buffer.BlockCopy(buffer, 0, copy, 0, count);
            return copy;
        }
#endif
    }
}
