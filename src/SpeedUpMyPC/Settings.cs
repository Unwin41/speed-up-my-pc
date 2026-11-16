using System;
using System.IO;

namespace SpeedUpMyPC
{
    // The user's choices, saved next to the startup undo file. A missing or broken
    // file just means "follow Windows" for both.
    internal static class Settings
    {
        public const string Auto = "auto";
        public const string Light = "light";
        public const string Dark = "dark";

        public static string Language = Auto;   // Auto or a language code such as "tr"
        public static string ThemeMode = Auto;  // Auto, Light or Dark

        private static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"SpeedUpMyPC\settings.txt");
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    if (key == "language" && value.Length > 0) Language = value;
                    if (key == "theme" && (value == Auto || value == Light || value == Dark)) ThemeMode = value;
                }
            }
            catch { } // unreadable: keep the defaults
        }

        public static bool Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[] { "language=" + Language, "theme=" + ThemeMode });
                return true;
            }
            catch { return false; } // the choice still applies until the app closes
        }
    }
}
