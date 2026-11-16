using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SpeedUpMyPC
{
    internal sealed class FoundFile
    {
        public string Path;
        public long Size;
        public DateTime LastWriteUtc;

        public string Name { get { return Path.Substring(Path.LastIndexOf('\\') + 1); } }
        public string Folder { get { return Path.Substring(0, Math.Max(0, Path.LastIndexOf('\\'))); } }
    }

    internal sealed class DuplicateGroup
    {
        public long Size;
        public List<FoundFile> Files;
        public long ExtraCopiesSize { get { return Size * (Files.Count - 1); } }
    }

    internal sealed class SkippedItem
    {
        public string Path;
        public string Reason;
    }

    internal sealed class FinderResult
    {
        public List<DuplicateGroup> Duplicates = new List<DuplicateGroup>();
        public List<FoundFile> LargeOld = new List<FoundFile>();
        public List<SkippedItem> Skipped = new List<SkippedItem>();
        public int SkippedCount;   // can be more than Skipped.Count: only the first ones are kept
        public int FilesSearched;
    }

    // Finds files with exactly the same content, and large files that haven't changed in a long time.
    // Read-only by design: it opens files only to read them. There is no code here, or anywhere
    // in the finder, that deletes, moves, renames or changes a file. The user decides.
    internal static class FileFinder
    {
        private const int QuickCheckBytes = 64 * 1024;
        private const int MaxSkippedKept = 10000;

        // Throws OperationCanceledException when cancel is triggered.
        public static FinderResult Run(IEnumerable<string> folders, long largeMinBytes, int oldMinDays,
            Action<string, int> progress, CancellationToken cancel)
        {
            var result = new FinderResult();
            var report = new ThrottledProgress(progress);
            var files = new List<FoundFile>();

            foreach (string root in WithoutNestedFolders(folders))
            {
                FolderWalker.Walk(root,
                    (path, size, lastWrite) => files.Add(new FoundFile { Path = path, Size = size, LastWriteUtc = lastWrite }),
                    (path, reason) => AddSkipped(result, path, reason),
                    dir =>
                    {
                        if (report.Due()) report.Send(Lang.F("finder.progressWalk", files.Count.ToString("N0")), -1);
                    },
                    cancel);
            }
            result.FilesSearched = files.Count;

            DateTime oldBefore = DateTime.UtcNow.AddDays(-oldMinDays);
            foreach (FoundFile f in files)
                if (f.Size >= largeMinBytes && f.LastWriteUtc <= oldBefore) result.LargeOld.Add(f);
            result.LargeOld.Sort((a, b) => b.Size.CompareTo(a.Size));

            result.Duplicates = FindDuplicates(files, result, report, cancel);
            return result;
        }

        // C:\Users and C:\Users\Me\Documents both picked: only C:\Users is searched, so no file shows up twice.
        internal static List<string> WithoutNestedFolders(IEnumerable<string> folders)
        {
            var unique = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in folders)
            {
                string withSlash = f.TrimEnd('\\') + @"\";
                if (seen.Add(withSlash)) unique.Add(withSlash);
            }

            var result = new List<string>();
            foreach (string folder in unique)
            {
                bool inside = false;
                foreach (string other in unique)
                    if (folder.Length > other.Length && folder.StartsWith(other, StringComparison.OrdinalIgnoreCase)) inside = true;
                if (!inside) result.Add(folder);
            }
            return result;
        }

        // 1. Group by size: files of different sizes can't be the same, and this costs no reading.
        // 2. Within a size, compare a hash of the first 64 KB. Most look-alikes fail here cheaply.
        // 3. Only files that still match get hashed in full (SHA-256).
        private static List<DuplicateGroup> FindDuplicates(List<FoundFile> files, FinderResult result,
            ThrottledProgress report, CancellationToken cancel)
        {
            var bySize = new Dictionary<long, List<FoundFile>>();
            foreach (FoundFile f in files)
            {
                if (f.Size == 0) continue; // empty files are all "the same" and take no space
                List<FoundFile> same;
                if (!bySize.TryGetValue(f.Size, out same)) bySize[f.Size] = same = new List<FoundFile>();
                same.Add(f);
            }

            var sameSize = new List<List<FoundFile>>();
            int toCheck = 0;
            foreach (List<FoundFile> group in bySize.Values)
            {
                if (group.Count < 2) continue;
                sameSize.Add(group);
                toCheck += group.Count;
            }

            var buffer = new byte[1024 * 1024];
            var seenOnDisk = new HashSet<string>();
            var sameStart = new List<List<FoundFile>>();
            int checkedCount = 0;
            foreach (List<FoundFile> group in sameSize)
            {
                foreach (List<FoundFile> match in GroupByHash(group, QuickCheckBytes, buffer, seenOnDisk, result, cancel, f =>
                {
                    checkedCount++;
                    if (report.Due())
                        report.Send(Lang.F("finder.progressQuick", checkedCount.ToString("N0"), toCheck.ToString("N0")),
                            (int)(checkedCount * 100L / Math.Max(1, toCheck)));
                }, null))
                {
                    sameStart.Add(match);
                }
            }

            long totalBytes = 0, doneBytes = 0;
            foreach (List<FoundFile> group in sameStart)
                if (group[0].Size > QuickCheckBytes) totalBytes += group[0].Size * group.Count;

            var duplicates = new List<DuplicateGroup>();
            foreach (List<FoundFile> group in sameStart)
            {
                if (group[0].Size <= QuickCheckBytes)
                {
                    duplicates.Add(new DuplicateGroup { Size = group[0].Size, Files = group }); // already compared in full
                    continue;
                }
                foreach (List<FoundFile> match in GroupByHash(group, long.MaxValue, buffer, null, result, cancel, null, read =>
                {
                    doneBytes += read;
                    if (report.Due())
                        report.Send(Lang.F("finder.progressFull", Ui.FormatSize(doneBytes), Ui.FormatSize(totalBytes)),
                            (int)(doneBytes * 100 / Math.Max(1, totalBytes)));
                }))
                {
                    duplicates.Add(new DuplicateGroup { Size = match[0].Size, Files = match });
                }
            }

            foreach (DuplicateGroup g in duplicates)
                g.Files.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            duplicates.Sort((a, b) => b.ExtraCopiesSize.CompareTo(a.ExtraCopiesSize));
            return duplicates;
        }

        private static List<List<FoundFile>> GroupByHash(List<FoundFile> group, long maxBytes, byte[] buffer,
            HashSet<string> seenOnDisk, FinderResult result, CancellationToken cancel,
            Action<FoundFile> onFileDone, Action<long> onRead)
        {
            var byHash = new Dictionary<string, List<FoundFile>>();
            foreach (FoundFile f in group)
            {
                string hash = Hash(f, maxBytes, buffer, seenOnDisk, result, cancel, onRead);
                if (onFileDone != null) onFileDone(f);
                if (hash == null) continue;
                List<FoundFile> same;
                if (!byHash.TryGetValue(hash, out same)) byHash[hash] = same = new List<FoundFile>();
                same.Add(f);
            }

            var matches = new List<List<FoundFile>>();
            foreach (List<FoundFile> same in byHash.Values)
                if (same.Count > 1) matches.Add(same);
            return matches;
        }

        // SHA-256 of the first maxBytes of the file, or null if it can't be read (reported as skipped)
        // or if it's a hard link to a file already counted (one file on disk, nothing to free).
        private static string Hash(FoundFile file, long maxBytes, byte[] buffer, HashSet<string> seenOnDisk,
            FinderResult result, CancellationToken cancel, Action<long> onRead)
        {
            cancel.ThrowIfCancellationRequested();
            int error;
            using (SafeFileHandle handle = FolderWalker.OpenForRead(file.Path, out error))
            {
                if (handle.IsInvalid)
                {
                    AddSkipped(result, file.Path, FolderWalker.ErrorText(error));
                    return null;
                }
                if (seenOnDisk != null)
                {
                    string id = FolderWalker.FileId(handle);
                    if (id != null && !seenOnDisk.Add(id)) return null;
                }

                try
                {
                    using (var stream = new FileStream(handle, FileAccess.Read, 4096, false))
                    using (var sha = new SHA256Cng())
                    {
                        if (stream.Length != file.Size)
                        {
                            AddSkipped(result, file.Path, Lang.T("finder.changed"));
                            return null;
                        }
                        long left = maxBytes;
                        while (left > 0)
                        {
                            cancel.ThrowIfCancellationRequested();
                            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                            if (read <= 0) break;
                            sha.TransformBlock(buffer, 0, read, null, 0);
                            left -= read;
                            if (onRead != null) onRead(read);
                        }
                        sha.TransformFinalBlock(buffer, 0, 0);
                        return Convert.ToBase64String(sha.Hash);
                    }
                }
                catch (IOException ex)
                {
                    AddSkipped(result, file.Path, Lang.F("finder.cantRead", ex.Message));
                    return null;
                }
                catch (UnauthorizedAccessException)
                {
                    AddSkipped(result, file.Path, Lang.T("error.noPermission"));
                    return null;
                }
            }
        }

        private static void AddSkipped(FinderResult result, string path, string reason)
        {
            result.SkippedCount++;
            if (result.Skipped.Count < MaxSkippedKept) result.Skipped.Add(new SkippedItem { Path = path, Reason = reason });
        }

        // The search calls in for every folder and file; the window only needs ~10 updates a second.
        private sealed class ThrottledProgress
        {
            private readonly Action<string, int> target;
            private readonly Stopwatch clock = Stopwatch.StartNew();
            private long lastMs = -1000;

            public ThrottledProgress(Action<string, int> target)
            {
                this.target = target;
            }

            public bool Due()
            {
                return clock.ElapsedMilliseconds - lastMs >= 100;
            }

            // percent: 0-100, or -1 when the total isn't known yet.
            public void Send(string text, int percent)
            {
                lastMs = clock.ElapsedMilliseconds;
                if (target != null) target(text, percent);
            }
        }
    }
}
