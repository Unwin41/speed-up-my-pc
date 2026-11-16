using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace SpeedUpMyPC
{
    internal sealed class StartupEntry
    {
        public KnownApp App;
        public RegistryHive Hive;
        public string ApprovedKey;   // StartupApproved subkey that Task Manager uses
        public string ValueName;
        public string DisplayName { get { return App.FriendlyName; } }
        public override string ToString() { return DisplayName; }
    }

    // Turns startup apps off exactly the way Task Manager does (StartupApproved keys).
    // Nothing is deleted; every change is recorded so it can be undone.
    internal static class StartupManager
    {
        private const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        private static string UndoFile
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"SpeedUpMyPC\undo.txt");
            }
        }

        public static List<StartupEntry> FindSuggestions()
        {
            var result = new List<StartupEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddRunKey(result, seen, RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run");
            AddRunKey(result, seen, RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run");
            AddRunKey(result, seen, RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32");
            AddStartupFolder(result, seen, RegistryHive.CurrentUser, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            AddStartupFolder(result, seen, RegistryHive.LocalMachine, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
            return result;
        }

        private static RegistryKey OpenBase(RegistryHive hive)
        {
            return RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        }

        private static void AddRunKey(List<StartupEntry> result, HashSet<string> seen, RegistryHive hive, string runKey, string approvedName)
        {
            try
            {
                using (RegistryKey root = OpenBase(hive))
                using (RegistryKey key = root.OpenSubKey(runKey))
                {
                    if (key == null) return;
                    foreach (string name in key.GetValueNames())
                    {
                        string command = Convert.ToString(key.GetValue(name));
                        Consider(result, seen, hive, ApprovedBase + approvedName, name, command);
                    }
                }
            }
            catch { }
        }

        private static void AddStartupFolder(List<StartupEntry> result, HashSet<string> seen, RegistryHive hive, string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (string file in Directory.GetFiles(folder))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    Consider(result, seen, hive, ApprovedBase + "StartupFolder", fileName, fileName);
                }
            }
            catch { }
        }

        private static void Consider(List<StartupEntry> result, HashSet<string> seen, RegistryHive hive, string approvedKey, string valueName, string command)
        {
            KnownApp app = SafeStartupList.Find(valueName, command);
            if (app == null) return;                       // unknown: never touch
            if (IsDisabled(hive, approvedKey, valueName)) return;
            if (!seen.Add(app.FriendlyName)) return;       // one row per app
            result.Add(new StartupEntry { App = app, Hive = hive, ApprovedKey = approvedKey, ValueName = valueName });
        }

        private static bool IsDisabled(RegistryHive hive, string approvedKey, string valueName)
        {
            byte[] data = ReadApproved(hive, approvedKey, valueName);
            return data != null && data.Length > 0 && (data[0] & 1) == 1; // 0x03 = disabled, 0x02/0x06 = enabled
        }

        private static byte[] ReadApproved(RegistryHive hive, string approvedKey, string valueName)
        {
            try
            {
                using (RegistryKey root = OpenBase(hive))
                using (RegistryKey key = root.OpenSubKey(approvedKey))
                {
                    return key == null ? null : key.GetValue(valueName) as byte[];
                }
            }
            catch { return null; }
        }

        public static int Disable(IEnumerable<StartupEntry> entries)
        {
            int count = 0;
            foreach (StartupEntry e in entries)
            {
                try
                {
                    byte[] previous = ReadApproved(e.Hive, e.ApprovedKey, e.ValueName);
                    RecordUndo(e, previous); // record before changing, so undo is always possible

                    byte[] data = new byte[12];
                    data[0] = 0x03;
                    BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);

                    using (RegistryKey root = OpenBase(e.Hive))
                    using (RegistryKey key = root.CreateSubKey(e.ApprovedKey))
                    {
                        key.SetValue(e.ValueName, data, RegistryValueKind.Binary);
                    }
                    count++;
                }
                catch { }
            }
            return count;
        }

        public static bool HasUndo()
        {
            return File.Exists(UndoFile);
        }

        // Restores every recorded change, newest first, so the oldest saved state wins.
        public static int UndoAll()
        {
            if (!File.Exists(UndoFile)) return 0;
            string[] lines = File.ReadAllLines(UndoFile);
            int restored = 0;
            bool allOk = true;

            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string[] parts = lines[i].Split('\t');
                if (parts.Length != 4) continue;
                try
                {
                    var hive = (RegistryHive)Enum.Parse(typeof(RegistryHive), parts[0]);
                    using (RegistryKey root = OpenBase(hive))
                    using (RegistryKey key = root.CreateSubKey(parts[1]))
                    {
                        if (parts[3] == "-")
                            key.DeleteValue(parts[2], false);
                        else
                            key.SetValue(parts[2], FromHex(parts[3]), RegistryValueKind.Binary);
                    }
                    restored++;
                }
                catch { allOk = false; }
            }

            if (allOk) File.Delete(UndoFile);
            return restored;
        }

        private static void RecordUndo(StartupEntry e, byte[] previous)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UndoFile));
            string line = e.Hive + "\t" + e.ApprovedKey + "\t" + e.ValueName + "\t" + (previous == null ? "-" : ToHex(previous));
            File.AppendAllText(UndoFile, line + Environment.NewLine);
        }

        private static string ToHex(byte[] data)
        {
            return BitConverter.ToString(data).Replace("-", "");
        }

        private static byte[] FromHex(string hex)
        {
            var data = new byte[hex.Length / 2];
            for (int i = 0; i < data.Length; i++)
                data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return data;
        }
    }
}
