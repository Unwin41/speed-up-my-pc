using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace SpeedUpMyPC
{
    internal sealed class InstalledApp
    {
        public string Name;
        public string Publisher;
        public string Version;
        public string InstallLocation;
        public long? Size;          // bytes; null = not known
        public bool SizeMeasured;   // true = measured from the install folder, false = the app's own estimate
        public DateTime? InstallDate;
    }

    // Reads the registry entries that Windows' own "Installed apps" list is built from.
    // Read-only: it never runs an uninstaller and never changes anything.
    //
    // There is deliberately no "last used" date. Windows doesn't record one for desktop apps.
    // The traces that exist (Prefetch, UserAssist) are per .exe, not per app, are pruned over
    // time, and are kept fresh by background updaters, so they would make guesses look like facts.
    internal static class InstalledApps
    {
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<InstalledApp> Read()
        {
            var apps = new List<InstalledApp>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReadKey(RegistryHive.LocalMachine, RegistryView.Registry64, apps, seen);
            ReadKey(RegistryHive.LocalMachine, RegistryView.Registry32, apps, seen);
            ReadKey(RegistryHive.CurrentUser, RegistryView.Registry64, apps, seen);
            return apps;
        }

        private static void ReadKey(RegistryHive hive, RegistryView view, List<InstalledApp> apps, HashSet<string> seen)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey list = root.OpenSubKey(UninstallKey))
                {
                    if (list == null) return;
                    foreach (string name in list.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey key = list.OpenSubKey(name))
                            {
                                InstalledApp app = key == null ? null : FromKey(key);
                                if (app == null) continue;
                                if (!seen.Add(app.Name + "|" + app.Version + "|" + app.Publisher)) continue; // same app in both views
                                apps.Add(app);
                            }
                        }
                        catch { } // one unreadable entry doesn't hide the others
                    }
                }
            }
            catch { }
        }

        // Same filter as Windows' list: hidden components, updates and entries
        // that can't be uninstalled are left out.
        private static InstalledApp FromKey(RegistryKey key)
        {
            string name = Text(key, "DisplayName");
            if (name.Length == 0) return null;
            if (Number(key, "SystemComponent") == 1) return null;
            if (Text(key, "ParentKeyName").Length > 0) return null;
            string release = Text(key, "ReleaseType");
            if (release == "Update" || release == "Hotfix" || release == "Security Update") return null;
            if (Text(key, "UninstallString").Length == 0) return null;

            var app = new InstalledApp
            {
                Name = name,
                Publisher = Text(key, "Publisher"),
                Version = Text(key, "DisplayVersion"),
                InstallLocation = CleanPath(Text(key, "InstallLocation")),
            };
            long kb = Number(key, "EstimatedSize");
            if (kb > 0) app.Size = kb * 1024;

            DateTime date;
            if (DateTime.TryParseExact(Text(key, "InstallDate"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                app.InstallDate = date;
            return app;
        }

        // For apps that don't report a size, adds up the files in their install folder.
        // progress(app just measured or null, done, total). Throws OperationCanceledException on cancel.
        public static void MeasureMissingSizes(List<InstalledApp> apps, Action<InstalledApp, int, int> progress, CancellationToken cancel)
        {
            var todo = new List<InstalledApp>();
            foreach (InstalledApp app in apps)
                if (app.Size == null && CanMeasure(app.InstallLocation)) todo.Add(app);

            for (int i = 0; i < todo.Count; i++)
            {
                progress(null, i, todo.Count);
                long total = 0;
                FolderWalker.Walk(todo[i].InstallLocation, (path, size, lastWrite) => total += size, (path, reason) => { }, null, cancel);
                todo[i].Size = total;
                todo[i].SizeMeasured = true;
                progress(todo[i], i + 1, todo.Count);
            }
        }

        // Some installers list a folder that holds much more than the app (a whole drive,
        // Program Files, Windows). Measuring that would blame one app for everything in it,
        // so those stay "not known".
        public static bool CanMeasure(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            string full;
            try
            {
                full = Path.GetFullPath(folder).TrimEnd('\\');
                if (!Directory.Exists(full)) return false;
                if (string.Equals(Path.GetPathRoot(full).TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch { return false; }

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (full.StartsWith(windows + @"\", StringComparison.OrdinalIgnoreCase)) return false;

            foreach (string shared in SharedFolders())
            {
                if (string.IsNullOrEmpty(shared)) continue;
                string s = shared.TrimEnd('\\');
                if (string.Equals(full, s, StringComparison.OrdinalIgnoreCase)) return false;              // e.g. Program Files itself
                if (s.StartsWith(full + @"\", StringComparison.OrdinalIgnoreCase)) return false;          // e.g. C:\Users
            }
            return true;
        }

        private static IEnumerable<string> SharedFolders()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                local,
                string.IsNullOrEmpty(local) ? null : Path.Combine(local, "Programs"),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
        }

        private static string CleanPath(string path)
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            try
            {
                if (path.Length > 0 && File.Exists(path)) path = Path.GetDirectoryName(path); // some list the .exe
            }
            catch { }
            return path;
        }

        private static string Text(RegistryKey key, string name)
        {
            object value = key.GetValue(name);
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        private static long Number(RegistryKey key, string name)
        {
            object value = key.GetValue(name);
            if (value is int) return (uint)(int)value; // DWORDs come back signed
            if (value is long) return (long)value;
            long parsed;
            return value is string && long.TryParse((string)value, out parsed) ? parsed : -1;
        }
    }
}
