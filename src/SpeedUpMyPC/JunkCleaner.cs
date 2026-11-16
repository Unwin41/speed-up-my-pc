using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;

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
            public Func<bool> SkipIf;      // checked right before cleaning; true = leave the folder alone
        }

        // True after Run() if the Windows Update cache was left alone because an update was in progress.
        public static bool SkippedWindowsUpdate { get; private set; }

        public static long Run(Action<string> progress)
        {
            SkippedWindowsUpdate = false;
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var targets = new List<Target>
            {
                new Target { Path = System.IO.Path.GetTempPath(), SkipRecentFiles = true },
                new Target { Path = System.IO.Path.Combine(windir, "Temp"), SkipRecentFiles = true },
                new Target
                {
                    Path = System.IO.Path.Combine(windir, @"SoftwareDistribution\Download"),
                    Services = new[] { "wuauserv", "bits" },
                    SkipIf = () => SkippedWindowsUpdate = WindowsUpdateInProgress(System.IO.Path.Combine(windir, @"SoftwareDistribution\Download"))
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
                if (t.SkipIf != null && t.SkipIf()) continue;
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

        // An update is downloading, installing, or waiting for a restart. Emptying its
        // download folder now could break it, so the folder is left for next time.
        private static bool WindowsUpdateInProgress(string downloadDir)
        {
            if (KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")) return true;
            if (KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")) return true;
            if (IsServiceRunning("TrustedInstaller")) return true; // Windows Modules Installer: installing updates right now
            return HasRecentFiles(downloadDir);                     // something was downloaded in the last 24 hours
        }

        private static bool KeyExists(string path)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = root.OpenSubKey(path))
                    return key != null;
            }
            catch { return true; } // can't tell: assume an update is pending
        }

        private static bool IsServiceRunning(string name)
        {
            try
            {
                using (var sc = new ServiceController(name))
                    return sc.Status != ServiceControllerStatus.Stopped;
            }
            catch { return false; } // service doesn't exist
        }

        private static bool HasRecentFiles(string dir)
        {
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) < RecentFileAge) return true;
                }
                return false;
            }
            catch { return true; } // can't tell: assume an update is in progress
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
