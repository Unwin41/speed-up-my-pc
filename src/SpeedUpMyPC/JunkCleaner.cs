using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;

namespace SpeedUpMyPC
{
    // Deletes only files that Windows regenerates on its own.
    // Never follows junctions/symlinks, never throws on locked files.
    internal static class JunkCleaner
    {
        private static readonly TimeSpan RecentFileAge = TimeSpan.FromHours(24);

        private sealed class Target
        {
            public string Path;
            public bool SkipRecentFiles;   // temp folders: an installer may be using fresh files
            public string[] Services;      // services that lock the folder; stopped and restarted around cleaning
            public string FilePattern;     // null = everything
        }

        public static long Run(Action<string> progress)
        {
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var targets = new List<Target>
            {
                new Target { Path = System.IO.Path.GetTempPath(), SkipRecentFiles = true },
                new Target { Path = System.IO.Path.Combine(windir, "Temp"), SkipRecentFiles = true },
                new Target
                {
                    Path = System.IO.Path.Combine(windir, @"SoftwareDistribution\Download"),
                    Services = new[] { "wuauserv", "bits" }
                },
                new Target
                {
                    Path = System.IO.Path.Combine(windir, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
                    Services = new[] { "dosvc" }
                },
                new Target
                {
                    Path = System.IO.Path.Combine(localAppData, @"Microsoft\Windows\Explorer"),
                    FilePattern = "thumbcache_*.db"
                },
            };

            long freed = 0;
            foreach (Target t in targets)
            {
                if (!Directory.Exists(t.Path)) continue;
                progress("Cleaning " + t.Path);

                List<ServiceController> stopped = StopServices(t.Services);
                try
                {
                    if (t.FilePattern != null)
                        freed += DeleteFiles(t.Path, t.FilePattern, t.SkipRecentFiles);
                    else
                        freed += CleanFolderContents(t.Path, t.SkipRecentFiles);
                }
                finally
                {
                    StartServices(stopped);
                }
            }
            return freed;
        }

        private static long DeleteFiles(string dir, string pattern, bool skipRecent)
        {
            long freed = 0;
            string[] files;
            try { files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
            catch { return 0; }

            foreach (string f in files)
                freed += TryDeleteFile(f, skipRecent);
            return freed;
        }

        // Deletes everything *inside* root (never root itself).
        private static long CleanFolderContents(string root, bool skipRecent)
        {
            long freed = DeleteFiles(root, "*", skipRecent);

            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { return freed; }

            foreach (string d in dirs)
            {
                if (IsReparsePoint(d)) continue; // never walk into junctions or symlinks
                freed += CleanFolderContents(d, skipRecent);
                try { Directory.Delete(d, false); } catch { } // only succeeds if now empty
            }
            return freed;
        }

        private static long TryDeleteFile(string path, bool skipRecent)
        {
            try
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return 0;
                if (skipRecent && DateTime.UtcNow - info.LastWriteTimeUtc < RecentFileAge) return 0;

                long size = info.Length;
                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                    info.Attributes &= ~FileAttributes.ReadOnly;
                info.Delete();
                return size;
            }
            catch
            {
                return 0; // in use or no access: leave it alone
            }
        }

        private static bool IsReparsePoint(string dir)
        {
            try { return (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0; }
            catch { return true; }
        }

        private static List<ServiceController> StopServices(string[] names)
        {
            var stopped = new List<ServiceController>();
            if (names == null) return stopped;
            foreach (string name in names)
            {
                try
                {
                    var sc = new ServiceController(name);
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                        stopped.Add(sc);
                    }
                }
                catch { } // can't stop it: files stay locked and are simply skipped
            }
            return stopped;
        }

        private static void StartServices(List<ServiceController> services)
        {
            foreach (ServiceController sc in services)
            {
                try { sc.Start(); } catch { }
            }
        }
    }
}
