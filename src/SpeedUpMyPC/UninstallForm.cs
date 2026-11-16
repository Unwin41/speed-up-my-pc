using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedUpMyPC
{
    // Lists installed apps with their sizes. It never uninstalls anything itself:
    // the button opens Windows' own Installed apps page, where the user uninstalls.
    internal sealed class UninstallForm : Form
    {
        private const int SizeColumn = 2;
        private const int SizeFromColumn = 3;

        private readonly ListView appList;
        private readonly ListViewSorter sorter;
        private readonly ProgressBar progressBar;
        private readonly Label statusLabel;
        private readonly Button stopButton;
        private readonly Dictionary<InstalledApp, ListViewItem> rows = new Dictionary<InstalledApp, ListViewItem>();
        private List<InstalledApp> apps;
        private CancellationTokenSource measuring;

        public UninstallForm()
        {
            Text = Lang.T("apps.title");
            AutoScaleMode = AutoScaleMode.None; // scaling is done by S()
            ClientSize = new Size(S(800), S(560));
            MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int w = ClientSize.Width;
            int h = ClientSize.Height;

            var note = new Label
            {
                Text = Lang.T("apps.note"),
                AutoSize = false,
                Location = new Point(S(12), S(10)),
                Size = new Size(w - S(24), S(44)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            appList = Ui.MakeList(
                new[] { "col.name", "col.publisher", "col.size", "col.sizeFrom", "col.installed", "col.lastUsed" },
                new[] { 210, 140, 70, 145, 85, 100 });
            appList.Dock = DockStyle.None;
            appList.Location = new Point(S(12), S(60));
            appList.Size = new Size(w - S(24), h - S(60) - S(110));
            appList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            appList.DoubleClick += OnUninstallClicked;
            sorter = ListViewSorter.Attach(appList);

            progressBar = new ProgressBar
            {
                Location = new Point(S(12), h - S(98)),
                Size = new Size(S(160), S(18)),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Visible = false,
            };
            statusLabel = new Label
            {
                AutoSize = false,
                Location = new Point(S(184), h - S(104)),
                Size = new Size(w - S(196), S(44)),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };
            Theme.SetRole(statusLabel, TextRole.Muted);

            var uninstallButton = new Button
            {
                Text = Lang.T("apps.uninstall"),
                Size = new Size(S(260), S(34)),
                Location = new Point(S(12), h - S(48)),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            uninstallButton.Click += OnUninstallClicked;
            stopButton = new Button
            {
                Text = Lang.T("apps.stopMeasuring"),
                Size = new Size(S(150), S(34)),
                Location = new Point(S(282), h - S(48)),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Visible = false,
            };
            stopButton.Click += (s, e) => { if (measuring != null) measuring.Cancel(); };
            var classicLink = new LinkLabel { Text = Lang.T("apps.classic"), AutoSize = true, Font = Font };
            classicLink.Location = new Point(w - classicLink.PreferredWidth - S(12), h - S(40));
            classicLink.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            classicLink.LinkClicked += (s, e) => Ui.OpenWindowsPage("appwiz.cpl");

            Controls.Add(note);
            Controls.Add(appList);
            Controls.Add(progressBar);
            Controls.Add(statusLabel);
            Controls.Add(uninstallButton);
            Controls.Add(stopButton);
            Controls.Add(classicLink);

            Shown += OnShown;
            FormClosing += (s, e) => { if (measuring != null) measuring.Cancel(); };
            Theme.Apply(this);
        }

        private static int S(int px)
        {
            return Ui.S(px);
        }

        private async void OnShown(object sender, EventArgs e)
        {
            statusLabel.Text = Lang.T("apps.reading");
            apps = await Task.Run(() => InstalledApps.Read());
            if (IsDisposed) return;
            Fill();
            await MeasureMissingSizes();
        }

        private void Fill()
        {
            appList.BeginUpdate();
            foreach (InstalledApp app in apps)
            {
                var item = new ListViewItem(app.Name) { Tag = app };
                Ui.AddCell(item, app.Publisher, null);
                Ui.AddCell(item, "", null);
                Ui.AddCell(item, "", null);
                Ui.AddCell(item, app.InstallDate.HasValue ? app.InstallDate.Value.ToString("d") : "", app.InstallDate);
                Ui.AddCell(item, Lang.T("apps.noData"), null);
                rows[app] = item;
                ShowSize(app, item);
                appList.Items.Add(item);
            }
            sorter.SortBy(SizeColumn, true);
            appList.EndUpdate();
            ShowSummary(false);
        }

        private static void ShowSize(InstalledApp app, ListViewItem item)
        {
            item.SubItems[SizeColumn].Text = app.Size.HasValue ? Ui.FormatSize(app.Size.Value) : Lang.T("apps.sizeUnknown");
            item.SubItems[SizeColumn].Tag = app.Size;
            item.SubItems[SizeFromColumn].Text = Lang.T(!app.Size.HasValue ? "apps.sizeNotListed"
                : app.SizeMeasured ? "apps.sizeMeasured" : "apps.sizeEstimate");
        }

        private async Task MeasureMissingSizes()
        {
            measuring = new CancellationTokenSource();
            CancellationToken token = measuring.Token;
            progressBar.Visible = stopButton.Visible = true;
            Action<InstalledApp, int, int> progress = (app, done, total) => Ui.Post(this, () => OnMeasured(app, done, total));

            bool stopped = false;
            try
            {
                await Task.Run(() => InstalledApps.MeasureMissingSizes(apps, progress, token));
            }
            catch (OperationCanceledException) { stopped = true; }
            catch (Exception) { stopped = true; }
            finally
            {
                measuring.Dispose();
                measuring = null;
            }
            if (IsDisposed) return;

            progressBar.Visible = stopButton.Visible = false;
            appList.Sort();
            ShowSummary(stopped);
        }

        private void OnMeasured(InstalledApp app, int done, int total)
        {
            if (measuring == null) return;
            ListViewItem item;
            if (app != null && rows.TryGetValue(app, out item)) ShowSize(app, item);
            progressBar.Value = total == 0 ? 100 : done * 100 / total;
            statusLabel.Text = Lang.F("apps.measuring", done, total);
        }

        private void ShowSummary(bool stopped)
        {
            long known = 0;
            int unknown = 0;
            foreach (InstalledApp app in apps)
            {
                if (app.Size.HasValue) known += app.Size.Value;
                else unknown++;
            }
            var text = new List<string>();
            text.Add(Lang.F("apps.summary", apps.Count, Ui.FormatSize(known)));
            if (unknown > 0) text.Add(Lang.F("apps.summaryUnknown", unknown));
            if (stopped) text.Add(Lang.T("apps.summaryStopped"));
            text.Add(Lang.T("apps.summaryStore"));
            statusLabel.Text = string.Join(" ", text.ToArray());
        }

        private void OnUninstallClicked(object sender, EventArgs e)
        {
            if (appList.SelectedItems.Count == 0)
            {
                statusLabel.Text = Lang.T("apps.pickApp");
                return;
            }
            var app = (InstalledApp)appList.SelectedItems[0].Tag;
            bool copied = true;
            try { Clipboard.SetText(app.Name); }
            catch (ExternalException) { copied = false; }

            if (Ui.OpenWindowsPage("ms-settings:appsfeatures"))
                statusLabel.Text = Lang.F(copied ? "apps.openedSettingsCopied" : "apps.openedSettings", app.Name);
            else if (Ui.OpenWindowsPage("appwiz.cpl"))
                statusLabel.Text = Lang.F("apps.openedClassic", app.Name);
            else
                statusLabel.Text = Lang.T("apps.openFailed");
        }
    }
}
