using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedUpMyPC
{
    // Lists duplicate files and large files that haven't changed in a long time.
    // This window only shows files. It has no way to delete, move or change them:
    // the user opens the folder in Explorer and decides there.
    internal sealed class FileFinderForm : Form
    {
        private const int MaxRowsShown = 5000;

        private readonly ListBox folderList;
        private readonly Button addFolderButton;
        private readonly Button removeFolderButton;
        private readonly NumericUpDown minSizeMb;
        private readonly NumericUpDown minAgeDays;
        private readonly Button findButton;
        private readonly ProgressBar progressBar;
        private readonly Label statusLabel;
        private readonly RadioButton duplicatesTab;
        private readonly RadioButton largeTab;
        private readonly RadioButton skippedTab;
        private readonly ListView duplicatesList;
        private readonly ListView largeList;
        private readonly ListView skippedList;
        private CancellationTokenSource search;

        public FileFinderForm()
        {
            Text = Lang.T("finder.title");
            AutoScaleMode = AutoScaleMode.None; // scaling is done by S()
            ClientSize = new Size(S(800), S(600));
            MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int w = ClientSize.Width;
            int h = ClientSize.Height;

            var foldersLabel = new Label { Text = Lang.T("finder.folders"), AutoSize = true, Location = new Point(S(12), S(12)) };
            folderList = new ListBox
            {
                Location = new Point(S(12), S(36)),
                Size = new Size(w - S(160), S(84)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.FixedSingle,
                HorizontalScrollbar = true,
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended,
            };
            addFolderButton = new Button
            {
                Text = Lang.T("finder.addFolder"),
                Size = new Size(S(128), S(32)),
                Location = new Point(w - S(140), S(36)),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            removeFolderButton = new Button
            {
                Text = Lang.T("finder.remove"),
                Size = new Size(S(128), S(32)),
                Location = new Point(w - S(140), S(74)),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            addFolderButton.Click += OnAddFolder;
            removeFolderButton.Click += OnRemoveFolder;

            minSizeMb = new NumericUpDown { Minimum = 1, Maximum = 1000000, Value = 100, Width = S(84), ThousandsSeparator = true };
            minAgeDays = new NumericUpDown { Minimum = 0, Maximum = 36500, Value = 365, Width = S(72), ThousandsSeparator = true };
            var thresholds = new FlowLayoutPanel
            {
                Location = new Point(S(9), S(130)),
                Size = new Size(w - S(21), S(32)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                WrapContents = false,
            };
            thresholds.Controls.Add(InlineLabel(Lang.T("finder.largeAtLeast")));
            thresholds.Controls.Add(minSizeMb);
            thresholds.Controls.Add(InlineLabel(Lang.T("finder.mbAndNotChanged")));
            thresholds.Controls.Add(minAgeDays);
            thresholds.Controls.Add(InlineLabel(Lang.T("finder.days")));

            findButton = new Button { Text = Lang.T("finder.find"), Size = new Size(S(140), S(34)), Location = new Point(S(12), S(170)) };
            findButton.Click += OnFindClicked;
            progressBar = new ProgressBar
            {
                Location = new Point(S(164), S(177)),
                Size = new Size(w - S(176), S(20)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            statusLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(S(12), S(212)),
                Size = new Size(w - S(24), S(24)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = Lang.T("finder.intro"),
            };
            Theme.SetRole(statusLabel, TextRole.Muted);

            duplicatesList = Ui.MakeList(new[] { "col.name", "col.size", "col.lastChanged", "col.folder" }, new[] { 220, 80, 110, 330 });
            duplicatesList.ShowGroups = true;
            largeList = Ui.MakeList(new[] { "col.name", "col.size", "col.lastChanged", "col.folder" }, new[] { 220, 80, 110, 330 });
            skippedList = Ui.MakeList(new[] { "col.fileOrFolder", "col.skipReason" }, new[] { 470, 270 });

            // The lists sit on top of each other; the tab buttons pick which one shows.
            var listHost = new Panel
            {
                Location = new Point(S(12), S(278)),
                Size = new Size(w - S(24), h - S(278) - S(60)),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };
            var tabStrip = new FlowLayoutPanel
            {
                Location = new Point(S(12), S(242)),
                Size = new Size(w - S(24), S(34)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                WrapContents = false,
            };
            duplicatesTab = TabButton("finder.tabDuplicates", duplicatesList);
            largeTab = TabButton("finder.tabLarge", largeList);
            skippedTab = TabButton("finder.tabSkipped", skippedList);
            foreach (RadioButton tab in new[] { duplicatesTab, largeTab, skippedTab })
            {
                tabStrip.Controls.Add(tab);
                listHost.Controls.Add((ListView)tab.Tag);
            }
            foreach (ListView list in new[] { duplicatesList, largeList, skippedList })
            {
                ListViewSorter.Attach(list);
                list.DoubleClick += (s, e) => OnShowInFolder(s, e);
            }
            duplicatesTab.Checked = true;

            var showButton = new Button
            {
                Text = Lang.T("finder.showInFolder"),
                Size = new Size(S(140), S(32)),
                Location = new Point(S(12), h - S(46)),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            var copyButton = new Button
            {
                Text = Lang.T("finder.copyPath"),
                Size = new Size(S(120), S(32)),
                Location = new Point(S(160), h - S(46)),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            showButton.Click += OnShowInFolder;
            copyButton.Click += OnCopyPath;
            var note = new Label
            {
                Text = Lang.T("finder.note"),
                AutoSize = false,
                Location = new Point(S(292), h - S(52)),
                Size = new Size(w - S(304), S(44)),
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };
            Theme.SetRole(note, TextRole.Muted);

            Controls.Add(foldersLabel);
            Controls.Add(folderList);
            Controls.Add(addFolderButton);
            Controls.Add(removeFolderButton);
            Controls.Add(thresholds);
            Controls.Add(findButton);
            Controls.Add(progressBar);
            Controls.Add(statusLabel);
            Controls.Add(tabStrip);
            Controls.Add(listHost);
            Controls.Add(showButton);
            Controls.Add(copyButton);
            Controls.Add(note);

            AddDefaultFolders();
            FormClosing += (s, e) => { if (search != null) search.Cancel(); };
            Theme.Apply(this);
        }

        private static int S(int px)
        {
            return Ui.S(px);
        }

        private static Label InlineLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(S(3), S(6), S(3), 0) };
        }

        private static RadioButton TabButton(string key, ListView list)
        {
            var tab = new RadioButton
            {
                Appearance = Appearance.Button,
                Text = Lang.T(key),
                AutoSize = true,
                MinimumSize = new Size(S(110), S(30)),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, S(4), 0),
                Tag = list,
            };
            tab.CheckedChanged += (s, e) => list.Visible = tab.Checked;
            list.Visible = false;
            return tab;
        }

        // The user's own folders, where forgotten copies usually pile up.
        private void AddDefaultFolders()
        {
            AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddFolder(KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B"))); // Downloads
            AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        }

        private void AddFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                path = Path.GetFullPath(path);
                if (!Directory.Exists(path)) return;
            }
            catch { return; }
            foreach (object item in folderList.Items)
                if (string.Equals((string)item, path, StringComparison.OrdinalIgnoreCase)) return;
            folderList.Items.Add(path);
        }

        private void OnAddFolder(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = Lang.T("finder.pickFolder"), ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) AddFolder(dialog.SelectedPath);
            }
        }

        private void OnRemoveFolder(object sender, EventArgs e)
        {
            for (int i = folderList.SelectedIndices.Count - 1; i >= 0; i--)
                folderList.Items.RemoveAt(folderList.SelectedIndices[i]);
        }

        private async void OnFindClicked(object sender, EventArgs e)
        {
            if (search != null)
            {
                search.Cancel();
                findButton.Enabled = false;
                statusLabel.Text = Lang.T("finder.stopping");
                return;
            }

            var folders = new List<string>();
            foreach (object item in folderList.Items) folders.Add((string)item);
            if (folders.Count == 0)
            {
                statusLabel.Text = Lang.T("finder.needFolder");
                return;
            }
            long minBytes = (long)minSizeMb.Value * 1024 * 1024;
            int minDays = (int)minAgeDays.Value;

            search = new CancellationTokenSource();
            CancellationToken token = search.Token;
            SetSearching(true);
            Action<string, int> progress = (text, percent) => Ui.Post(this, () => ShowProgress(text, percent));

            FinderResult result = null;
            string failure = null;
            try
            {
                result = await Task.Run(() => FileFinder.Run(folders, minBytes, minDays, progress, token));
            }
            catch (OperationCanceledException) { failure = Lang.T("finder.stopped"); }
            catch (Exception ex) { failure = Lang.F("finder.error", ex.Message); }
            finally
            {
                search.Dispose();
                search = null;
            }
            if (IsDisposed) return;

            SetSearching(false);
            if (failure != null)
            {
                statusLabel.Text = failure;
                return;
            }
            ShowResults(result);
        }

        private void SetSearching(bool on)
        {
            findButton.Text = Lang.T(on ? "finder.stop" : "finder.find");
            findButton.Enabled = true;
            folderList.Enabled = addFolderButton.Enabled = removeFolderButton.Enabled = !on;
            minSizeMb.Enabled = minAgeDays.Enabled = !on;
            progressBar.Style = on ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            progressBar.Value = 0;
            if (on) statusLabel.Text = Lang.T("finder.starting");
        }

        private void ShowProgress(string text, int percent)
        {
            if (search == null) return; // a late update from a search that already ended
            statusLabel.Text = text;
            if (percent < 0)
            {
                progressBar.Style = ProgressBarStyle.Marquee;
            }
            else
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = Math.Max(0, Math.Min(100, percent));
            }
        }

        private void ShowResults(FinderResult r)
        {
            progressBar.Value = 100;
            long extra = 0;
            foreach (DuplicateGroup g in r.Duplicates) extra += g.ExtraCopiesSize;

            bool cut = FillDuplicates(r.Duplicates);
            cut |= FillFiles(largeList, r.LargeOld);
            cut |= FillSkipped(r.Skipped);

            duplicatesTab.Text = Lang.F("finder.tabCount", Lang.T("finder.tabDuplicates"), r.Duplicates.Count.ToString("N0"));
            largeTab.Text = Lang.F("finder.tabCount", Lang.T("finder.tabLarge"), r.LargeOld.Count.ToString("N0"));
            skippedTab.Text = Lang.F("finder.tabCount", Lang.T("finder.tabSkipped"), r.SkippedCount.ToString("N0"));

            var text = new List<string>();
            text.Add(Lang.F("finder.summarySearched", r.FilesSearched.ToString("N0")));
            text.Add(r.Duplicates.Count == 0
                ? Lang.T("finder.summaryNoDuplicates")
                : Lang.F("finder.summaryDuplicates", r.Duplicates.Count.ToString("N0"), Ui.FormatSize(extra)));
            text.Add(Lang.F("finder.summaryLarge", r.LargeOld.Count.ToString("N0")));
            if (cut) text.Add(Lang.F("finder.summaryCut", MaxRowsShown.ToString("N0")));
            statusLabel.Text = string.Join(" ", text.ToArray());

            if (r.Duplicates.Count == 0 && r.LargeOld.Count > 0) largeTab.Checked = true;
            else duplicatesTab.Checked = true;
        }

        // Returns true if the list was cut short.
        private bool FillDuplicates(List<DuplicateGroup> groups)
        {
            duplicatesList.BeginUpdate();
            duplicatesList.ListViewItemSorter = null;
            duplicatesList.Items.Clear();
            duplicatesList.Groups.Clear();
            int rows = 0;
            bool cut = false;
            foreach (DuplicateGroup g in groups)
            {
                if (rows > 0 && rows + g.Files.Count > MaxRowsShown) { cut = true; break; }
                var group = new ListViewGroup(Lang.F("finder.group", g.Files.Count, Ui.FormatSize(g.Size), Ui.FormatSize(g.ExtraCopiesSize)));
                duplicatesList.Groups.Add(group);
                foreach (FoundFile f in g.Files)
                {
                    duplicatesList.Items.Add(FileRow(f, group));
                    rows++;
                }
            }
            duplicatesList.EndUpdate();
            return cut;
        }

        private static bool FillFiles(ListView list, List<FoundFile> files)
        {
            list.BeginUpdate();
            list.ListViewItemSorter = null;
            list.Items.Clear();
            for (int i = 0; i < files.Count && i < MaxRowsShown; i++)
                list.Items.Add(FileRow(files[i], null));
            list.EndUpdate();
            return files.Count > MaxRowsShown;
        }

        private bool FillSkipped(List<SkippedItem> skipped)
        {
            skippedList.BeginUpdate();
            skippedList.ListViewItemSorter = null;
            skippedList.Items.Clear();
            for (int i = 0; i < skipped.Count && i < MaxRowsShown; i++)
            {
                var item = new ListViewItem(skipped[i].Path) { Tag = skipped[i].Path };
                Ui.AddCell(item, skipped[i].Reason, null);
                skippedList.Items.Add(item);
            }
            skippedList.EndUpdate();
            return skipped.Count > MaxRowsShown;
        }

        private static ListViewItem FileRow(FoundFile f, ListViewGroup group)
        {
            var item = group == null ? new ListViewItem(f.Name) : new ListViewItem(f.Name, group);
            item.Tag = f.Path;
            Ui.AddCell(item, Ui.FormatSize(f.Size), f.Size);
            Ui.AddCell(item, f.LastWriteUtc.ToLocalTime().ToString("d"), f.LastWriteUtc);
            Ui.AddCell(item, f.Folder, null);
            return item;
        }

        private string SelectedPath()
        {
            ListView list = largeTab.Checked ? largeList : skippedTab.Checked ? skippedList : duplicatesList;
            if (list.SelectedItems.Count == 0)
            {
                statusLabel.Text = Lang.T("finder.pickFile");
                return null;
            }
            return (string)list.SelectedItems[0].Tag;
        }

        private void OnShowInFolder(object sender, EventArgs e)
        {
            string path = SelectedPath();
            if (path != null) Ui.ShowInExplorer(path);
        }

        private void OnCopyPath(object sender, EventArgs e)
        {
            string path = SelectedPath();
            if (path == null) return;
            try
            {
                Clipboard.SetText(path);
                statusLabel.Text = Lang.F("finder.copied", path);
            }
            catch (ExternalException) { statusLabel.Text = Lang.T("finder.copyFailed"); }
        }

        private static string KnownFolder(Guid id)
        {
            try
            {
                string path;
                return SHGetKnownFolderPath(id, 0, IntPtr.Zero, out path) == 0 ? path : null;
            }
            catch { return null; }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags,
            IntPtr token, [MarshalAs(UnmanagedType.LPWStr)] out string path);
    }
}
