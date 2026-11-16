using SimpleTools.Achievements;
using Steamworks;
using UnityEngine;

namespace SimpleTools.Steam
{
    // Steam backend for SimpleTools.Achievements: SetAchievement and StoreStats. The ids must exist in the app's
    // Steamworks settings (Stats & Achievements), otherwise Steam ignores them. Without Steam it does nothing.
    public sealed class SteamAchievements : IAchievementBackend
    {
        public void Unlock(string id)
        {
            if (!SteamService.IsActive)
            {
                return;
            }

            if (SteamUserStats.GetAchievement(id, out bool achieved) && achieved)
            {
                return;
            }

            if (!SteamUserStats.SetAchievement(id))
            {
                Debug.LogWarning($"SteamAchievements: {id} is not an achievement of this app.");
                return;
            }

            SteamUserStats.StoreStats();
        }
    }
}
