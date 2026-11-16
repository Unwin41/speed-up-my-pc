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
        // Fonts are in points, so Windows scales them with the display. Pixel sizes don't
        // scale on their own, so every size and position below goes through S().
        private static readonly float DpiScale = ReadDpiScale();

        private readonly Button mainButton;
        private readonly Label statusLabel;
        private readonly LinkLabel undoLink;
        private Panel resultsPanel;

        public MainForm()
        {
            Text = "Speed up my PC";
            BackColor = Color.White;
            AutoScaleMode = AutoScaleMode.None; // scaling is done by S()
            ClientSize = new Size(S(480), S(400));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            mainButton = new Button
            {
                Text = "Speed up my PC",
                Size = new Size(S(240), S(64)),
                Font = new Font("Segoe UI", 14f),
                Anchor = AnchorStyles.None,
            };
            mainButton.Location = new Point((ClientSize.Width - mainButton.Width) / 2, (ClientSize.Height - mainButton.Height) / 2 - S(20));
            mainButton.Click += OnMainButtonClick;

            statusLabel = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.DimGray,
                Size = new Size(ClientSize.Width - S(40), S(40)),
                Location = new Point(S(20), mainButton.Bottom + S(16)),
                AutoEllipsis = true,
            };

            undoLink = new LinkLabel
            {
                Text = "Undo startup changes",
                AutoSize = true,
                Visible = StartupManager.HasUndo(),
            };
            undoLink.Location = new Point(S(12), ClientSize.Height - S(28));
            undoLink.LinkClicked += OnUndoClicked;

            var versionLabel = new Label
            {
                Text = "v" + VersionText(),
                AutoSize = true,
                ForeColor = Color.DarkGray,
                Font = new Font("Segoe UI", 8.5f),
            };
            versionLabel.Location = new Point(ClientSize.Width - versionLabel.PreferredWidth - S(12), ClientSize.Height - S(26));

            Controls.Add(mainButton);
            Controls.Add(statusLabel);
            Controls.Add(undoLink);
            Controls.Add(versionLabel);
        }

        private async void OnMainButtonClick(object sender, EventArgs e)
        {
            mainButton.Enabled = false;
            mainButton.Text = "Working...";
            statusLabel.Text = "This can take a minute.";

            Action<string> progress = msg => BeginInvoke((Action)(() => statusLabel.Text = msg));
            long freed = 0;
            List<StartupEntry> suggestions = null;

            await Task.Run(() =>
            {
                freed = JunkCleaner.Run(progress);
                suggestions = StartupManager.FindSuggestions();
            });

            ShowResults(freed, suggestions);
        }

        private void ShowResults(long freedBytes, List<StartupEntry> suggestions)
        {
            mainButton.Visible = false;
            statusLabel.Visible = false;

            resultsPanel = new Panel { Location = new Point(S(24), S(24)), Size = new Size(ClientSize.Width - S(48), ClientSize.Height - S(64)) };

            var done = new Label
            {
                Text = "Done! Freed " + FormatSize(freedBytes) + ".",
                Font = new Font("Segoe UI", 16f),
                AutoSize = true,
                Location = new Point(0, 0),
            };
            string noteText = JunkCleaner.SkippedWindowsUpdate
                ? "Only temporary files were removed. Windows Update files were skipped because an update is in progress."
                : "Only temporary files were removed. Windows makes new ones when it needs them.";
            var note = new Label
            {
                Text = noteText,
                ForeColor = Color.DimGray,
                AutoSize = false,
                Size = new Size(resultsPanel.Width, S(48)),
                Location = new Point(0, S(40)),
            };
            resultsPanel.Controls.Add(done);
            resultsPanel.Controls.Add(note);

            if (suggestions.Count == 0)
            {
                var fine = new Label { Text = "Your startup apps look fine.", AutoSize = true, Location = new Point(0, S(100)) };
                var close = new Button { Text = "Close", Size = new Size(S(100), S(32)), Location = new Point(0, S(140)) };
                close.Click += (s, a) => Close();
                resultsPanel.Controls.Add(fine);
                resultsPanel.Controls.Add(close);
            }
            else
            {
                var heading = new Label
                {
                    Text = "These apps start with Windows and slow it down.\nTurning them off doesn't uninstall them.",
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

                var turnOff = new Button { Text = "Turn off selected", Size = new Size(S(150), S(32)), Location = new Point(0, S(272)) };
                var skip = new Button { Text = "Skip", Size = new Size(S(90), S(32)), Location = new Point(S(160), S(272)) };

                turnOff.Click += (s, a) =>
                {
                    var chosen = new List<StartupEntry>();
                    foreach (object item in list.CheckedItems) chosen.Add((StartupEntry)item);
                    int n = StartupManager.Disable(chosen);
                    heading.Text = n == 1
                        ? "Turned off 1 app. It will stay off the next time you start your PC."
                        : "Turned off " + n + " apps. They will stay off the next time you start your PC.";
                    list.Enabled = false;
                    turnOff.Enabled = false;
                    skip.Text = "Close";
                    undoLink.Visible = StartupManager.HasUndo();
                };
                skip.Click += (s, a) => Close();

                resultsPanel.Controls.Add(heading);
                resultsPanel.Controls.Add(list);
                resultsPanel.Controls.Add(turnOff);
                resultsPanel.Controls.Add(skip);
            }

            Controls.Add(resultsPanel);
            undoLink.BringToFront();
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
                MessageBox.Show(this,
                    "Couldn't undo the startup changes: " + ex.Message + "\n\nClose any other copy of this app and try again.",
                    "Speed up my PC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            undoLink.Visible = StartupManager.HasUndo();
            string message;
            if (n == 0) message = "There was nothing to undo.";
            else if (StartupManager.HasUndo()) message = "Some startup apps couldn't be restored. Try Undo again, or turn them back on in Task Manager's Startup tab.";
            else message = "Done. Your startup apps are back the way they were.";
            MessageBox.Show(this, message, "Speed up my PC", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static int S(int px)
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

        private static string VersionText()
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            return v.Major + "." + v.Minor + "." + v.Build;
        }

        private static string FormatSize(long bytes)
        {
            double mb = bytes / (1024.0 * 1024.0);
            if (mb >= 1024) return (mb / 1024).ToString("0.0") + " GB";
            return mb.ToString("0") + " MB";
        }
    }
}
