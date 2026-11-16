using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedUpMyPC
{
    internal sealed class MainForm : Form
    {
        private readonly Button mainButton;
        private readonly Label statusLabel;
        private readonly LinkLabel undoLink;
        private readonly LinkLabel findFilesLink;
        private readonly LinkLabel appsLink;
        private readonly LinkLabel settingsLink;
        private Panel resultsPanel;

        // True when the window closed because the language or theme changed; Program reopens it.
        public bool RestartRequested { get; private set; }

        public MainForm()
        {
            Text = Lang.T("app.title");
            AutoScaleMode = AutoScaleMode.None; // scaling is done by S()
            ClientSize = new Size(S(480), S(400));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            mainButton = new Button
            {
                Text = Lang.T("main.button"),
                Font = new Font("Segoe UI", 14f),
                Anchor = AnchorStyles.None,
            };
            // Wide enough for the text in every language.
            mainButton.Size = new Size(Math.Max(S(240), TextRenderer.MeasureText(mainButton.Text, mainButton.Font).Width + S(40)), S(64));
            mainButton.Location = new Point((ClientSize.Width - mainButton.Width) / 2, (ClientSize.Height - mainButton.Height) / 2 - S(20));
            mainButton.Click += OnMainButtonClick;

            statusLabel = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(ClientSize.Width - S(40), S(40)),
                Location = new Point(S(20), mainButton.Bottom + S(16)),
                AutoEllipsis = true,
            };
            Theme.SetRole(statusLabel, TextRole.Muted);

            undoLink = new LinkLabel
            {
                Text = Lang.T("main.undo"),
                AutoSize = true,
                Visible = StartupManager.HasUndo(),
            };
            undoLink.Location = new Point(S(12), ClientSize.Height - S(28));
            undoLink.LinkClicked += OnUndoClicked;

            var versionLabel = new Label
            {
                Text = "v" + VersionText(),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f),
            };
            Theme.SetRole(versionLabel, TextRole.Faint);
            versionLabel.Location = new Point(ClientSize.Width - versionLabel.PreferredWidth - S(12), ClientSize.Height - S(26));

            // Two read-only tools: they list things, the user decides what to do.
            findFilesLink = new LinkLabel { Text = Lang.T("main.findFiles"), AutoSize = true };
            appsLink = new LinkLabel { Text = Lang.T("main.apps"), AutoSize = true };
            findFilesLink.LinkClicked += (s, e) => { using (var f = new FileFinderForm()) f.ShowDialog(this); };
            appsLink.LinkClicked += (s, e) => { using (var f = new UninstallForm()) f.ShowDialog(this); };

            settingsLink = new LinkLabel { Text = Lang.T("main.settings"), AutoSize = true };
            settingsLink.LinkClicked += OnSettingsClicked;

            Controls.Add(mainButton);
            Controls.Add(statusLabel);
            Controls.Add(undoLink);
            Controls.Add(versionLabel);
            Controls.Add(findFilesLink);
            Controls.Add(appsLink);
            Controls.Add(settingsLink);

            // Measured after Controls.Add, once the links use the form's font.
            int linksWidth = findFilesLink.PreferredWidth + S(24) + appsLink.PreferredWidth;
            findFilesLink.Location = new Point((ClientSize.Width - linksWidth) / 2, ClientSize.Height - S(58));
            appsLink.Location = new Point(findFilesLink.Left + findFilesLink.PreferredWidth + S(24), findFilesLink.Top);
            settingsLink.Location = new Point(versionLabel.Left - settingsLink.PreferredWidth - S(12), ClientSize.Height - S(28));

            Theme.Apply(this);
        }

        private async void OnMainButtonClick(object sender, EventArgs e)
        {
            mainButton.Enabled = false;
            settingsLink.Enabled = false; // reopening the window mid-clean would lose the results
            mainButton.Text = Lang.T("main.working");
            statusLabel.Text = Lang.T("main.wait");

            Action<string> progress = msg => Ui.Post(this, () => statusLabel.Text = msg);
            long freed = 0;
            List<StartupEntry> suggestions = null;

            await Task.Run(() =>
            {
                freed = JunkCleaner.Run(progress);
                suggestions = StartupManager.FindSuggestions();
            });
            if (IsDisposed) return;

            settingsLink.Enabled = true;
            ShowResults(freed, suggestions);
        }

        private void ShowResults(long freedBytes, List<StartupEntry> suggestions)
        {
            mainButton.Visible = false;
            statusLabel.Visible = false;

            // Ends above the tool links at the bottom.
            resultsPanel = new Panel { Location = new Point(S(24), S(24)), Size = new Size(ClientSize.Width - S(48), ClientSize.Height - S(90)) };

            var done = new Label
            {
                Text = Lang.F("main.done", FormatSize(freedBytes)),
                Font = new Font("Segoe UI", 16f),
                AutoSize = true,
                Location = new Point(0, 0),
            };
            var note = new Label
            {
                Text = Lang.T(JunkCleaner.SkippedWindowsUpdate ? "main.noteUpdateSkipped" : "main.note"),
                AutoSize = false,
                Size = new Size(resultsPanel.Width, S(48)),
                Location = new Point(0, S(40)),
            };
            Theme.SetRole(note, TextRole.Muted);
            resultsPanel.Controls.Add(done);
            resultsPanel.Controls.Add(note);

            if (suggestions.Count == 0)
            {
                var fine = new Label { Text = Lang.T("main.startupFine"), AutoSize = true, Location = new Point(0, S(100)) };
                var close = new Button { Text = Lang.T("main.close"), Size = new Size(S(100), S(32)), Location = new Point(0, S(140)) };
                close.Click += (s, a) => Close();
                resultsPanel.Controls.Add(fine);
                resultsPanel.Controls.Add(close);
            }
            else
            {
                var heading = new Label
                {
                    Text = Lang.T("main.startupHeading"),
                    AutoSize = false,
                    Size = new Size(resultsPanel.Width, S(44)),
                    Location = new Point(0, S(92)),
                };
                var list = new CheckedListBox
                {
                    CheckOnClick = true,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(0, S(140)),
                    Size = new Size(resultsPanel.Width, S(120)),
                };
                foreach (StartupEntry entry in suggestions)
                    list.Items.Add(entry, entry.App.CheckedByDefault);

                var turnOff = new Button { Text = Lang.T("main.turnOff"), Size = new Size(S(150), S(32)), Location = new Point(0, S(272)) };
                var skip = new Button { Text = Lang.T("main.skip"), Size = new Size(S(90), S(32)), Location = new Point(S(160), S(272)) };

                turnOff.Click += (s, a) =>
                {
                    var chosen = new List<StartupEntry>();
                    foreach (object item in list.CheckedItems) chosen.Add((StartupEntry)item);
                    int n = StartupManager.Disable(chosen);
                    heading.Text = n == 1 ? Lang.T("main.turnedOffOne") : Lang.F("main.turnedOffMany", n);
                    list.Enabled = false;
                    turnOff.Enabled = false;
                    skip.Text = Lang.T("main.close");
                    undoLink.Visible = StartupManager.HasUndo();
                };
                skip.Click += (s, a) => Close();

                resultsPanel.Controls.Add(heading);
                resultsPanel.Controls.Add(list);
                resultsPanel.Controls.Add(turnOff);
                resultsPanel.Controls.Add(skip);
            }

            Controls.Add(resultsPanel);
            Theme.Apply(resultsPanel);
            undoLink.BringToFront();
        }

        private void OnSettingsClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (var settings = new SettingsForm())
            {
                if (settings.ShowDialog(this) != DialogResult.OK) return;
            }
            RestartRequested = true;
            Close();
        }

        private void OnUndoClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            int n;
            try
            {
                n = StartupManager.UndoAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.F("main.undoFailed", ex.Message),
                    Lang.T("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            undoLink.Visible = StartupManager.HasUndo();
            string message;
            if (n == 0) message = Lang.T("main.undoNothing");
            else if (StartupManager.HasUndo()) message = Lang.T("main.undoPartial");
            else message = Lang.T("main.undoDone");
            MessageBox.Show(this, message, Lang.T("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static int S(int px)
        {
            return Ui.S(px);
        }

        private static string VersionText()
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            return v.Major + "." + v.Minor + "." + v.Build;
        }

        private static string FormatSize(long bytes)
        {
            double mb = bytes / (1024.0 * 1024.0);
            if (mb >= 1024) return Lang.F("units.gb", (mb / 1024).ToString("0.0"));
            return Lang.F("units.mb", mb.ToString("0"));
        }
    }
}
