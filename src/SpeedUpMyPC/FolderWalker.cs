using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SpeedUpMyPC
{
    // Walks a folder tree with the Win32 Find API. Read-only: it lists, it never changes anything.
    // - Never follows junctions or symbolic links (they can lead outside the folder, or loop forever).
    // - Never opens online-only cloud files (opening them would make OneDrive download them).
    // - Never throws for something it can't open: it reports it through onSkipped and moves on.
    // Paths longer than 260 characters work because every call uses the \\?\ form.
    internal static class FolderWalker
    {
        public delegate void FileFound(string path, long size, DateTime lastWriteUtc);

        public static void Walk(string root, FileFound onFile, Action<string, string> onSkipped,
            Action<string> onFolder, CancellationToken cancel)
        {
            var pending = new Stack<string>();
            pending.Push(root.TrimEnd('\\'));

            while (pending.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                string dir = pending.Pop();
                if (onFolder != null) onFolder(dir);

                FindData data;
                IntPtr find = FindFirstFileExW(ToLongPath(dir) + @"\*", FindExInfoBasic, out data,
                    FindExSearchNameMatch, IntPtr.Zero, FindFirstExLargeFetch);
                if (find == InvalidHandle)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != ErrorFileNotFound) onSkipped(dir, ErrorText(error)); // FileNotFound = empty folder
                    continue;
                }

                try
                {
                    do
                    {
                        string name = data.FileName;
                        if (name == "." || name == "..") continue;
                        string path = dir + @"\" + name;
                        bool isReparsePoint = (data.Attributes & AttrReparsePoint) != 0;

                        if ((data.Attributes & AttrDirectory) != 0)
                        {
                            // OneDrive folders are reparse points too, but they're real folders.
                            if (isReparsePoint && !IsCloudTag(data.ReparseTag))
                                onSkipped(path, Lang.T("walker.folderLink"));
                            else
                                pending.Push(path);
                        }
                        else if (isReparsePoint && IsLinkTag(data.ReparseTag))
                            onSkipped(path, Lang.T("walker.fileLink"));
                        else if ((data.Attributes & AttrNotOnThisPc) != 0)
                            onSkipped(path, Lang.T("walker.cloudOnly"));
                        else
                            onFile(path, ((long)data.SizeHigh << 32) | data.SizeLow, ToDate(data.WriteHigh, data.WriteLow));
                    }
                    while (FindNextFileW(find, out data));
                }
                finally
                {
                    FindClose(find);
                }
            }
        }

        // Opens a file for reading only, without stopping other programs from using it.
        public static SafeFileHandle OpenForRead(string path, out int error)
        {
            SafeFileHandle handle = CreateFileW(ToLongPath(path), GenericRead, ShareReadWriteDelete,
                IntPtr.Zero, OpenExisting, FlagSequentialScan, IntPtr.Zero);
            error = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
            return handle;
        }

        // Identifies the file on disk. Two paths with the same id are hard links to one file.
        public static string FileId(SafeFileHandle handle)
        {
            HandleInfo info;
            if (!GetFileInformationByHandle(handle, out info)) return null;
            return info.VolumeSerial.ToString("X8") + ":" + info.IndexHigh.ToString("X8") + info.IndexLow.ToString("X8");
        }

        public static string ErrorText(int error)
        {
            switch (error)
            {
                case 2:
                case 3: return Lang.T("error.notThere");
                case 5: return Lang.T("error.noPermission");
                case 32:
                case 33: return Lang.T("error.inUse");
                case 1920: return Lang.T("error.notAllowed");
                default:
                    return new Win32Exception(error).Message.TrimEnd('.', ' ', '\r', '\n');
            }
        }

        private static string ToLongPath(string path)
        {
            if (path.StartsWith(@"\\?\")) return path;
            if (path.StartsWith(@"\\")) return @"\\?\UNC\" + path.Substring(2);
            return @"\\?\" + path;
        }

        private static DateTime ToDate(uint high, uint low)
        {
            try { return DateTime.FromFileTimeUtc(((long)high << 32) | low); }
            catch (ArgumentOutOfRangeException) { return DateTime.MaxValue; } // nonsense date: never counts as "old"
        }

        private static bool IsCloudTag(uint tag)
        {
            return (tag & 0xFFFF0FFF) == 0x9000001A; // IO_REPARSE_TAG_CLOUD and CLOUD_1..F
        }

        private static bool IsLinkTag(uint tag)
        {
            return tag == 0xA000000C    // symbolic link
                || tag == 0xA0000003    // junction / mount point
                || tag == 0x8000001B    // app execution alias
                || tag == 0xA000001D;   // WSL symbolic link
        }

        private const uint AttrDirectory = 0x10;
        private const uint AttrReparsePoint = 0x400;
        private const uint AttrNotOnThisPc = 0x1000 | 0x40000 | 0x400000; // offline, recall on open, recall on data access
        private const int ErrorFileNotFound = 2;
        private const int FindExInfoBasic = 1;
        private const int FindExSearchNameMatch = 0;
        private const int FindFirstExLargeFetch = 2;
        private const uint GenericRead = 0x80000000;
        private const uint ShareReadWriteDelete = 7;
        private const uint OpenExisting = 3;
        private const uint FlagSequentialScan = 0x08000000;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FindData
        {
            public uint Attributes;
            public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
            public uint SizeHigh, SizeLow;
            public uint ReparseTag;
            public uint Reserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateFileName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HandleInfo
        {
            public uint Attributes;
            public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
            public uint VolumeSerial;
            public uint SizeHigh, SizeLow;
            public uint Links;
            public uint IndexHigh, IndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileExW(string fileName, int infoLevel, out FindData data,
            int searchOp, IntPtr searchFilter, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool FindNextFileW(IntPtr find, out FindData data);

        [DllImport("kernel32.dll")]
        private static extern bool FindClose(IntPtr find);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share,
            IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out HandleInfo info);
    }
}
