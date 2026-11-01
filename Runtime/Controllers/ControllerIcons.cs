using System.Collections.Generic;
using UnityEngine;

namespace SimpleTools.Controllers
{
    // Button prompt icons loaded from Resources, one folder per controller family with the same file names
    // in each (for example Resources/ControllerIcons/playstation/south.png). Get picks the folder of the
    // controller in use. Free icon sets with this layout: Kenney Input Prompts (CC0).
    public static class ControllerIcons
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        // Resources path containing the family folders
        public static string Root { get; set; } = "ControllerIcons";

        public static Dictionary<ControllerFamily, string> FamilyFolders { get; } = new Dictionary<ControllerFamily, string>
        {
            { ControllerFamily.Xbox, "xbox" },
            { ControllerFamily.PlayStation, "playstation" },
            { ControllerFamily.SteamDeck, "steamdeck" },
        };

        // Icon for the controller in use
        public static Texture2D Get(string name)
        {
            return Load(FamilyFolders[ActiveDevice.Family] + "/" + name);
        }

        // Any icon under Root, for example "keyboard/space"
        public static Texture2D Load(string path)
        {
            string fullPath = Root + "/" + path;
            if (!Cache.TryGetValue(fullPath, out Texture2D icon))
            {
                icon = Resources.Load<Texture2D>(fullPath);
                if (icon == null)
                {
                    Debug.LogWarning("ControllerIcons: missing icon " + fullPath);
                }

                Cache[fullPath] = icon;
            }

            return icon;
        }
    }
}
