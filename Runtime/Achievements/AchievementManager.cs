using System;
using System.Collections.Generic;
using SimpleTools.Settings;
using UnityEngine;

namespace SimpleTools.Achievements
{
    // A platform that also records achievements: Steam (SteamAchievements in SimpleTools.Steam), and in the same way
    // Google Play Games, Game Center, PlayStation trophies, Xbox. Unlock gets the platform's own id (see
    // AchievementManager.AddBackend) and may be called again for an achievement the platform already has: it must not fail.
    public interface IAchievementBackend
    {
        void Unlock(string platformId);
    }

    // How valuable an achievement is. PlayStation trophies use it directly (bronze, silver, gold, and one platinum for
    // getting all the others); Xbox turns it into Gamerscore (AchievementInfo.DefaultGamerscore); Steam, Google Play
    // and Game Center have no tiers (Steam shows rarity instead), so there it only matters to the game's own screens.
    public enum AchievementTier
    {
        Bronze,
        Silver,
        Gold,
        Platinum
    }

    // What the platforms need to know about an achievement besides its id: the tier and whether it stays secret until
    // unlocked (Steam "Hidden", PlayStation hidden trophy, Xbox secret achievement). The game declares them once with
    // AchievementManager.Define; unlocking works the same without.
    public sealed class AchievementInfo
    {
        public string Id { get; }
        public AchievementTier Tier { get; }
        public bool Hidden { get; }

        public AchievementInfo(string id, AchievementTier tier = AchievementTier.Bronze, bool hidden = false)
        {
            Id = id;
            Tier = tier;
            Hidden = hidden;
        }

        // A common Xbox split (the total must be 1000 for the base game): adjust per game if the sum does not fit
        public static int DefaultGamerscore(AchievementTier tier)
        {
            switch (tier)
            {
                case AchievementTier.Silver: return 30;
                case AchievementTier.Gold: return 90;
                case AchievementTier.Platinum: return 180;
                default: return 15;
            }
        }
    }

    [Serializable]
    public sealed class AchievementState
    {
        [SerializeField] private List<string> _unlocked = new List<string>();

        public List<string> Unlocked => _unlocked ??= new List<string>();
    }

    // Achievements unlocked by the player, for every platform the game runs on. The game uses one id per achievement
    // (its own); they are always saved locally (achievements.json in the persistent data folder), so they survive
    // without a platform and can be shown in game. Every registered backend gets each unlock, translated to the
    // platform's id (Google Play uses generated ids, Game Center reverse-DNS ids, PlayStation trophy numbers), and
    // Sync sends it the ones it may have missed (unlocked offline, or before the backend was registered).
    public static class AchievementManager
    {
        public const string FileName = "achievements.json";

        private static readonly List<(IAchievementBackend backend, Func<string, string> toPlatformId)> _backends =
            new List<(IAchievementBackend, Func<string, string>)>();
        private static readonly Dictionary<string, AchievementInfo> _infos = new Dictionary<string, AchievementInfo>();
        private static readonly List<AchievementInfo> _defined = new List<AchievementInfo>();
        private static SettingsManager<AchievementState> _store;

        // Raised once per achievement, the first time it is unlocked
        public static event Action<string> Unlocked;

        public static IReadOnlyList<string> All => Store.Unlocked;

        public static bool IsUnlocked(string id) => Store.Unlocked.Contains(id);

        // The achievements declared by the game, in its order
        public static IReadOnlyList<AchievementInfo> Defined => _defined;

        // Declares tier and visibility of the game's achievements (again for the same id replaces it)
        public static void Define(IEnumerable<AchievementInfo> achievements)
        {
            if (achievements == null)
            {
                return;
            }

            foreach (AchievementInfo info in achievements)
            {
                if (info == null || string.IsNullOrEmpty(info.Id))
                {
                    continue;
                }

                if (_infos.TryGetValue(info.Id, out AchievementInfo old))
                {
                    _defined[_defined.IndexOf(old)] = info;
                }
                else
                {
                    _defined.Add(info);
                }

                _infos[info.Id] = info;
            }
        }

        // Null for an id the game did not declare
        public static AchievementInfo Info(string id) =>
            id != null && _infos.TryGetValue(id, out AchievementInfo info) ? info : null;

        // Hidden and not yet unlocked: show it as a secret
        public static bool IsSecret(string id) => Info(id)?.Hidden == true && !IsUnlocked(id);

        // toPlatformId: the platform's id for one of the game's ids; null when they are the same. Returning null or
        // an empty string skips an achievement the platform does not have.
        public static void AddBackend(IAchievementBackend backend, Func<string, string> toPlatformId = null)
        {
            if (backend == null || _backends.Exists(entry => entry.backend == backend))
            {
                return;
            }

            _backends.Add((backend, toPlatformId));
            Sync(backend, toPlatformId);
        }

        // True the first time: saved locally and sent to every backend
        public static bool Unlock(string id)
        {
            if (string.IsNullOrEmpty(id) || IsUnlocked(id))
            {
                return false;
            }

            Store.Unlocked.Add(id);
            _store.Save();
            foreach ((IAchievementBackend backend, Func<string, string> toPlatformId) in _backends)
            {
                SafeUnlock(backend, toPlatformId, id);
            }

            Unlocked?.Invoke(id);
            return true;
        }

        // Sends the locally unlocked achievements to every backend
        public static void Sync()
        {
            foreach ((IAchievementBackend backend, Func<string, string> toPlatformId) in _backends)
            {
                Sync(backend, toPlatformId);
            }
        }

        // A fresh state, for tests and debug menus (backends keep what they have)
        public static void ResetLocal()
        {
            Store.Unlocked.Clear();
            _store.Save();
        }

        private static AchievementState Store
        {
            get
            {
                if (_store == null)
                {
                    _store = new SettingsManager<AchievementState>(FileName);
                    // An unreadable file starts over: the platforms keep their own copy and Sync is idempotent
                    if (_store.LoadOrCreate() == null)
                    {
                        _store.Reset();
                    }
                }

                return _store.CurrentSettings;
            }
        }

        private static void Sync(IAchievementBackend backend, Func<string, string> toPlatformId)
        {
            foreach (string id in Store.Unlocked)
            {
                SafeUnlock(backend, toPlatformId, id);
            }
        }

        private static void SafeUnlock(IAchievementBackend backend, Func<string, string> toPlatformId, string id)
        {
            try
            {
                string platformId = toPlatformId != null ? toPlatformId(id) : id;
                if (!string.IsNullOrEmpty(platformId))
                {
                    backend.Unlock(platformId);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Achievements: {backend.GetType().Name} could not unlock {id} ({exception.Message}).");
            }
        }
    }
}
