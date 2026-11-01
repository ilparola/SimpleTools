using System;
using Steamworks;
using UnityEngine;

namespace SimpleTools.Steam
{
    // Starts the Steam API (Steamworks.NET) and keeps it running for the whole game. When Steam is not
    // running, or steam_appid.txt is missing outside the Steam client, the game goes on without Steam.
    // Call Initialize once before the first scene (RuntimeInitializeLoadType.BeforeSceneLoad).
    public static class SteamService
    {
        public static bool IsActive { get; private set; }

        // Raised right before SteamAPI.Shutdown, to release Steam Input and other interfaces
        public static event Action ShuttingDown;

        public static bool Initialize()
        {
            if (IsActive)
            {
                return true;
            }

            if (!Packsize.Test() || !DllCheck.Test())
            {
                Debug.LogWarning("SteamService: incompatible Steam library, running without Steam.");
                return false;
            }

            try
            {
                IsActive = SteamAPI.Init();
            }
            catch (DllNotFoundException exception)
            {
                Debug.LogWarning("SteamService: Steam library missing, running without Steam. " + exception.Message);
            }

            if (!IsActive)
            {
                Debug.Log("SteamService: Steam not available, running without Steam.");
                return false;
            }

            GameObject host = new GameObject("SimpleTools.Steam");
            host.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<SteamRunner>();
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            IsActive = false;
            ShuttingDown = null;
        }

        internal static void Shutdown()
        {
            if (!IsActive)
            {
                return;
            }

            ShuttingDown?.Invoke();
            SteamAPI.Shutdown();
            IsActive = false;
        }
    }

    // Runs before Controllers, so Steam Input data is fresh when the controller is read
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-1100)]
    internal sealed class SteamRunner : MonoBehaviour
    {
        private void Update()
        {
            if (SteamService.IsActive)
            {
                // Also runs the Steam Input frame
                SteamAPI.RunCallbacks();
            }
        }

        private void OnDestroy()
        {
            SteamService.Shutdown();
        }
    }
}
