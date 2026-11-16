using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedUpMyPC
{
    internal sealed class MainForm : Form
    {
        private readonly Button mainButton;
        private readonly Label statusLabel;
        private readonly LinkLabel undoLink;
        private Panel resultsPanel;

        public MainForm()
        {
            Text = "Speed up my PC";
            BackColor = Color.White;
            ClientSize = new Size(480, 400);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10f);

            mainButton = new Button
            {
                Text = "Speed up my PC",
                Size = new Size(240, 64),
                Font = new Font("Segoe UI", 14f),
                Anchor = AnchorStyles.None,
            };
            mainButton.Location = new Point((ClientSize.Width - mainButton.Width) / 2, (ClientSize.Height - mainButton.Height) / 2 - 20);
            mainButton.Click += OnMainButtonClick;

            statusLabel = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.DimGray,
                Size = new Size(ClientSize.Width - 40, 40),
                Location = new Point(20, mainButton.Bottom + 16),
                AutoEllipsis = true,
            };

            undoLink = new LinkLabel
            {
                Text = "Undo startup changes",
                AutoSize = true,
                Visible = StartupManager.HasUndo(),
            };
            undoLink.Location = new Point(12, ClientSize.Height - 28);
            undoLink.LinkClicked += OnUndoClicked;

            Controls.Add(mainButton);
            Controls.Add(statusLabel);
            Controls.Add(undoLink);
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

            resultsPanel = new Panel { Location = new Point(24, 24), Size = new Size(ClientSize.Width - 48, ClientSize.Height - 64) };

            var done = new Label
            {
                Text = "Done! Freed " + FormatSize(freedBytes) + ".",
                Font = new Font("Segoe UI", 16f),
                AutoSize = true,
                Location = new Point(0, 0),
            };
            var note = new Label
            {
                Text = "Only temporary files were removed. Windows makes new ones when it needs them.",
                ForeColor = Color.DimGray,
                AutoSize = false,
                Size = new Size(resultsPanel.Width, 40),
                Location = new Point(0, 40),
            };
            resultsPanel.Controls.Add(done);
            resultsPanel.Controls.Add(note);

            if (suggestions.Count == 0)
            {
                var fine = new Label { Text = "Your startup apps look fine.", AutoSize = true, Location = new Point(0, 100) };
                var close = new Button { Text = "Close", Size = new Size(100, 32), Location = new Point(0, 140) };
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
                    Size = new Size(resultsPanel.Width, 44),
                    Location = new Point(0, 92),
                };
                var list = new CheckedListBox
                {
                    CheckOnClick = true,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(0, 140),
                    Size = new Size(resultsPanel.Width, 120),
                };
                foreach (StartupEntry entry in suggestions)
                    list.Items.Add(entry, entry.App.CheckedByDefault);

                var turnOff = new Button { Text = "Turn off selected", Size = new Size(150, 32), Location = new Point(0, 272) };
                var skip = new Button { Text = "Skip", Size = new Size(90, 32), Location = new Point(160, 272) };

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
            int n = StartupManager.UndoAll();
            undoLink.Visible = StartupManager.HasUndo();
            MessageBox.Show(this,
                n == 0 ? "There was nothing to undo." : "Done. Your startup apps are back the way they were.",
                "Speed up my PC", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string FormatSize(long bytes)
        {
            double mb = bytes / (1024.0 * 1024.0);
            if (mb >= 1024) return (mb / 1024).ToString("0.0") + " GB";
            return mb.ToString("0") + " MB";
        }
    }
}
