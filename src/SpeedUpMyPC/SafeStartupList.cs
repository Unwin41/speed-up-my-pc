using System;

namespace SpeedUpMyPC
{
    // Only apps on this list are ever suggested. Anything unknown (drivers,
    // antivirus, cloud sync, company software) is never shown or touched.
    internal sealed class KnownApp
    {
        public string Match { get; private set; }        // matched against entry name and command, lowercase
        public string FriendlyName { get; private set; }
        public bool CheckedByDefault { get; private set; }

        public KnownApp(string match, string friendlyName, bool checkedByDefault)
        {
            Match = match;
            FriendlyName = friendlyName;
            CheckedByDefault = checkedByDefault;
        }
    }

    internal static class SafeStartupList
    {
        // Pre-checked: game launchers, media and helpers nobody needs at boot.
        // Unchecked: chat apps, which some people want running for notifications.
        public static readonly KnownApp[] Apps = new[]
        {
            new KnownApp("spotify", "Spotify", true),
            new KnownApp("steam", "Steam", true),
            new KnownApp("epicgameslauncher", "Epic Games Launcher", true),
            new KnownApp("eadesktop", "EA app", true),
            new KnownApp("origin.exe", "Origin", true),
            new KnownApp("battle.net", "Battle.net", true),
            new KnownApp("upc.exe", "Ubisoft Connect", true),
            new KnownApp("galaxyclient", "GOG Galaxy", true),
            new KnownApp("utorrent", "uTorrent", true),
            new KnownApp("bittorrent", "BitTorrent", true),
            new KnownApp("ituneshelper", "iTunes Helper", true),
            new KnownApp("jusched", "Java Update Scheduler", true),
            new KnownApp("adobearm", "Adobe Updater", true),
            new KnownApp("microsoftedgeautolaunch", "Microsoft Edge (pre-launch)", true),
            new KnownApp("opera browser assistant", "Opera Browser Assistant", true),
            new KnownApp("ccleaner", "CCleaner", true),
            new KnownApp("discord", "Discord", false),
            new KnownApp("skype", "Skype", false),
            new KnownApp("zoom", "Zoom", false),
            new KnownApp("whatsapp", "WhatsApp", false),
            new KnownApp("telegram", "Telegram", false),
            new KnownApp("teams", "Microsoft Teams", false),
        };

        public static KnownApp Find(string name, string command)
        {
            string haystack = ((name ?? "") + " " + (command ?? "")).ToLowerInvariant();
            foreach (KnownApp app in Apps)
            {
                if (haystack.Contains(app.Match)) return app;
            }
            return null;
        }
    }
}
