using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedUpMyPC
{
    // Small helpers shared by the app's windows.
    internal static class Ui
    {
        // Fonts are in points, so Windows scales them with the display. Pixel sizes don't
        // scale on their own, so every size and position goes through S().
        private static readonly float DpiScale = ReadDpiScale();

        public static int S(int px)
        {
            return (int)Math.Round(px * DpiScale);
        }

        private static float ReadDpiScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Max(1f, g.DpiX / 96f);
            }
            catch { return 1f; }
        }

        public static string FormatSize(long bytes)
        {
            const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
            if (bytes >= GB) return Lang.F("units.gb", (bytes / GB).ToString("0.0"));
            if (bytes >= MB) return Lang.F("units.mb", (bytes / MB).ToString(bytes < 10 * MB ? "0.0" : "0"));
            if (bytes >= KB) return Lang.F("units.kb", (bytes / KB).ToString("0"));
            return Lang.F("units.bytes", bytes);
        }

        // columns are translation keys.
        public static ListView MakeList(string[] columns, int[] widths)
        {
            var list = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle, // the 3D border stays light in dark mode
            };
            for (int i = 0; i < columns.Length; i++)
                list.Columns.Add(Lang.T(columns[i]), S(widths[i]));
            return list;
        }

        // Adds a cell. A non-null sortKey (a number or date) is what the column sorts by.
        public static void AddCell(ListViewItem item, string text, object sortKey)
        {
            ListViewItem.ListViewSubItem cell = item.SubItems.Add(text);
            cell.Tag = sortKey;
        }

        // Opens Explorer with the file or folder selected. Only shows it; changes nothing.
        public static void ShowInExplorer(string path)
        {
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        // Opens a Windows page such as "ms-settings:appsfeatures". False if Windows couldn't open it.
        public static bool OpenWindowsPage(string target)
        {
            try { Process.Start(target); return true; }
            catch { return false; }
        }

        // Runs on the window's thread. Ignored once the window has closed.
        public static void Post(Control owner, Action action)
        {
            try
            {
                if (owner.IsHandleCreated && !owner.IsDisposed) owner.BeginInvoke(action);
            }
            catch (InvalidOperationException) { } // window closed in the meantime
        }
    }

    // Click a column header to sort by it, click again to reverse. Cells with a sort key
    // (a number or date) sort by that key, others by their text. Cells with no key in a
    // number/date column (an unknown size, say) always go to the bottom.
    internal sealed class ListViewSorter : IComparer
    {
        private readonly ListView list;
        private int column;
        private bool descending;

        private ListViewSorter(ListView list)
        {
            this.list = list;
        }

        public static ListViewSorter Attach(ListView list)
        {
            var sorter = new ListViewSorter(list);
            list.ColumnClick += (s, e) =>
            {
                if (list.ListViewItemSorter == sorter && sorter.column == e.Column)
                    sorter.SortBy(e.Column, !sorter.descending);
                else
                    sorter.SortBy(e.Column, ColumnHasKeys(list, e.Column)); // numbers and dates: biggest/newest first
            };
            return sorter;
        }

        public void SortBy(int col, bool desc)
        {
            column = col;
            descending = desc;
            list.ListViewItemSorter = this;
            list.Sort();
        }

        private static bool ColumnHasKeys(ListView list, int col)
        {
            foreach (ListViewItem item in list.Items)
                if (col < item.SubItems.Count && item.SubItems[col].Tag != null) return true;
            return false;
        }

        public int Compare(object x, object y)
        {
            ListViewItem.ListViewSubItemCollection a = ((ListViewItem)x).SubItems;
            ListViewItem.ListViewSubItemCollection b = ((ListViewItem)y).SubItems;
            object keyA = column < a.Count ? a[column].Tag : null;
            object keyB = column < b.Count ? b[column].Tag : null;

            if (keyA == null && keyB == null)
            {
                string textA = column < a.Count ? a[column].Text : "";
                string textB = column < b.Count ? b[column].Text : "";
                int byText = string.Compare(textA, textB, StringComparison.CurrentCultureIgnoreCase);
                return descending ? -byText : byText;
            }
            if (keyA == null) return 1;  // no value: last, whichever way the list is sorted
            if (keyB == null) return -1;

            int result = keyA.GetType() == keyB.GetType() && keyA is IComparable
                ? ((IComparable)keyA).CompareTo(keyB)
                : 0;
            return descending ? -result : result;
        }
    }
}
