using System;
using System.IO;
using UnityEngine;

namespace SimpleTools.Media
{
    // Still pictures from a camera: renders it into a temporary texture at any size (screen space UI is left out),
    // and saves PNG files with a timestamped name in the user's pictures folder.
    public static class PhotoCapture
    {
        // Renders the camera at the given size. The caller owns the returned texture and destroys it.
        public static Texture2D Render(Camera camera, int width, int height)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                return image;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        // Renders and saves in one go; returns the full path of the file
        public static string Save(Camera camera, int width, int height, string folder, string prefix)
        {
            Texture2D image = Render(camera, width, height);
            try
            {
                return SavePng(image, folder, prefix);
            }
            finally
            {
                UnityEngine.Object.Destroy(image);
            }
        }

        public static string SavePng(Texture2D image, string folder, string prefix)
        {
            Directory.CreateDirectory(folder);
            string name = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}";
            string path = Path.Combine(folder, name + ".png");
            // Two pictures in the same second get a number
            for (int copy = 2; File.Exists(path); copy++)
            {
                path = Path.Combine(folder, $"{name}_{copy}.png");
            }

            File.WriteAllBytes(path, image.EncodeToPNG());
            return path;
        }

        // Pictures/<subfolder> on every platform; the application data folder if that cannot be written
        public static string PicturesFolder(string subfolder)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(pictures))
            {
                pictures = Path.Combine(home, "Pictures");
            }

            string folder = Path.Combine(pictures, subfolder);
            try
            {
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"PhotoCapture: {folder} is not writable ({exception.Message}), using the application data folder.");
                folder = Path.Combine(Application.persistentDataPath, "Pictures");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }
    }
}
