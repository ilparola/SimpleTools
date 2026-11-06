using System;
using Steamworks;
using UnityEngine;

namespace SimpleTools.Steam
{
    // The local Steam user: persona name and large avatar. Without Steam both are null. The avatar may not be
    // downloaded yet the first time it is asked for: AvatarChanged is raised when it arrives.
    public static class SteamProfile
    {
        private static Texture2D _avatar;
        private static Callback<AvatarImageLoaded_t> _avatarLoaded;

        public static event Action AvatarChanged;

        public static string PersonaName => SteamService.IsActive ? SteamFriends.GetPersonaName() : null;

        public static Texture2D Avatar
        {
            get
            {
                if (_avatar == null && SteamService.IsActive)
                {
                    Load();
                }

                return _avatar;
            }
        }

        private static void Load()
        {
            if (_avatarLoaded == null)
            {
                _avatarLoaded = Callback<AvatarImageLoaded_t>.Create(OnAvatarLoaded);
                SteamService.ShuttingDown += Release;
            }

            int image = SteamFriends.GetLargeFriendAvatar(SteamUser.GetSteamID());
            // -1: still downloading, the callback arrives later. 0: no avatar
            if (image > 0)
            {
                _avatar = ToTexture(image);
            }
        }

        private static void OnAvatarLoaded(AvatarImageLoaded_t data)
        {
            if (data.m_steamID != SteamUser.GetSteamID() || data.m_iImage <= 0)
            {
                return;
            }

            _avatar = ToTexture(data.m_iImage);
            AvatarChanged?.Invoke();
        }

        private static Texture2D ToTexture(int image)
        {
            if (!SteamUtils.GetImageSize(image, out uint width, out uint height) || width == 0 || height == 0)
            {
                return null;
            }

            var pixels = new byte[width * height * 4];
            if (!SteamUtils.GetImageRGBA(image, pixels, pixels.Length))
            {
                return null;
            }

            // Steam rows go top to bottom, Unity textures bottom to top
            var flipped = new byte[pixels.Length];
            int row = (int)width * 4;
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(pixels, y * row, flipped, ((int)height - 1 - y) * row, row);
            }

            var texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false) { name = "SteamAvatar" };
            texture.LoadRawTextureData(flipped);
            texture.Apply();
            return texture;
        }

        private static void Release()
        {
            _avatarLoaded?.Dispose();
            _avatarLoaded = null;
            SteamService.ShuttingDown -= Release;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _avatar = null;
            _avatarLoaded = null;
            AvatarChanged = null;
        }
    }
}
