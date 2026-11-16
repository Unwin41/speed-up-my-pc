using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace SpeedUpMyPC
{
    // All text the user sees comes from the files in lang/ (built into the exe as "lang.<code>.txt").
    // A new language is just a new file there: build.ps1 picks up every file in the folder.
    // Keys missing from a language fall back to English, then to the key itself.
    internal static class Lang
    {
        public const string Default = "en";
        private const string Prefix = "lang.";
        private const string Suffix = ".txt";

        private static Dictionary<string, string> english = new Dictionary<string, string>();
        private static Dictionary<string, string> current = english;

        public static string Code { get; private set; }

        // setting: Settings.Auto (follow Windows) or a language code such as "tr".
        public static void Init(string setting)
        {
            english = Load(Default) ?? new Dictionary<string, string>();
            string code = setting != Settings.Auto && Exists(setting) ? setting : FromWindows();
            Code = code;
            current = code == Default ? english : (Load(code) ?? english);
        }

        // Windows' display language if there's a file for it (pt-BR first, then pt), else English.
        public static string FromWindows()
        {
            CultureInfo ui = CultureInfo.CurrentUICulture;
            if (Exists(ui.Name)) return ui.Name;
            if (Exists(ui.TwoLetterISOLanguageName)) return ui.TwoLetterISOLanguageName;
            return Default;
        }

        public static string T(string key)
        {
            string text;
            if (current.TryGetValue(key, out text) || english.TryGetValue(key, out text)) return text;
            return key;
        }

        // T() with {0}, {1}... filled in. A translation with broken {placeholders} falls back to English
        // instead of crashing the app.
        public static string F(string key, params object[] args)
        {
            try { return string.Format(CultureInfo.CurrentCulture, T(key), args); }
            catch (FormatException) { }
            string text;
            if (english.TryGetValue(key, out text))
            {
                try { return string.Format(CultureInfo.CurrentCulture, text, args); }
                catch (FormatException) { }
            }
            return key;
        }

        // Every built-in language: code -> its own name ("@name" in the file), sorted by name.
        public static List<KeyValuePair<string, string>> Available()
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (string resource in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                if (!resource.StartsWith(Prefix, StringComparison.Ordinal) || !resource.EndsWith(Suffix, StringComparison.Ordinal)) continue;
                string code = resource.Substring(Prefix.Length, resource.Length - Prefix.Length - Suffix.Length);
                Dictionary<string, string> file = Load(code);
                string name;
                if (file == null || !file.TryGetValue("@name", out name)) name = code;
                result.Add(new KeyValuePair<string, string>(code, name));
            }
            result.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.CurrentCultureIgnoreCase));
            return result;
        }

        private static bool Exists(string code)
        {
            return !string.IsNullOrEmpty(code)
                && Assembly.GetExecutingAssembly().GetManifestResourceInfo(Prefix + code + Suffix) != null;
        }

        internal static Dictionary<string, string> Load(string code)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + code + Suffix))
                {
                    if (stream == null) return null;
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                        return Parse(reader);
                }
            }
            catch { return null; }
        }

        // "key = value" lines. Lines starting with # are comments. \n in a value is a line break.
        internal static Dictionary<string, string> Parse(TextReader reader)
        {
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                strings[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Replace(@"\n", "\n");
            }
            return strings;
        }
    }
}
