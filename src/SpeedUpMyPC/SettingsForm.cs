using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedUpMyPC
{
    // Language and light/dark choice. Returns DialogResult.OK only if something changed.
    // A list and option buttons rather than drop-downs: Windows' drop-downs ignore the dark colors.
    internal sealed class SettingsForm : Form
    {
        private sealed class Choice
        {
            public string Code;
            public string Text;
            public override string ToString() { return Text; }
        }

        private readonly ListBox languageList;
        private readonly RadioButton[] themeButtons;

        public SettingsForm()
        {
            Text = Lang.T("settings.title");
            AutoScaleMode = AutoScaleMode.None; // scaling is done by S()
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10f);
            int w = S(380);

            var languageLabel = new Label { Text = Lang.T("settings.language"), AutoSize = true, Location = new Point(S(16), S(14)) };
            languageList = new ListBox
            {
                Location = new Point(S(16), S(38)),
                Width = w - S(32),
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
            };
            languageList.Items.Add(new Choice { Code = Settings.Auto, Text = Lang.T("settings.languageAuto") });
            foreach (KeyValuePair<string, string> language in Lang.Available())
                languageList.Items.Add(new Choice { Code = language.Key, Text = language.Value });
            // ItemHeight isn't real until the list is on screen, so rows are sized from the font. Scrolls past 6 languages.
            languageList.Height = Math.Min(languageList.Items.Count, 6) * (Font.Height + S(2)) + S(4);
            languageList.SelectedIndex = 0;
            for (int i = 0; i < languageList.Items.Count; i++)
                if (((Choice)languageList.Items[i]).Code == Settings.Language) languageList.SelectedIndex = i;

            int y = languageList.Bottom + S(16);
            var themeLabel = new Label { Text = Lang.T("settings.theme"), AutoSize = true, Location = new Point(S(16), y) };
            y += S(26);
            var themes = new[]
            {
                new Choice { Code = Settings.Auto, Text = Lang.T("settings.themeAuto") },
                new Choice { Code = Settings.Light, Text = Lang.T("settings.light") },
                new Choice { Code = Settings.Dark, Text = Lang.T("settings.dark") },
            };
            themeButtons = new RadioButton[themes.Length];
            for (int i = 0; i < themes.Length; i++)
            {
                themeButtons[i] = new RadioButton
                {
                    Text = themes[i].Text,
                    Tag = themes[i].Code,
                    AutoSize = true,
                    Location = new Point(S(18), y),
                    Checked = themes[i].Code == Settings.ThemeMode,
                };
                y += S(28);
            }

            var highContrast = new Label
            {
                Text = Lang.T("settings.highContrast"),
                AutoSize = false,
                Location = new Point(S(16), y),
                Size = new Size(w - S(32), S(40)),
                Visible = SystemInformation.HighContrast,
            };
            Theme.SetRole(highContrast, TextRole.Muted);
            if (highContrast.Visible) y += S(44);

            y += S(12);
            var ok = new Button { Text = Lang.T("settings.ok"), Size = new Size(S(96), S(32)), Location = new Point(w - S(16) - S(96) * 2 - S(8), y) };
            var cancel = new Button { Text = Lang.T("settings.cancel"), Size = new Size(S(96), S(32)), Location = new Point(w - S(16) - S(96), y) };
            ok.Click += OnOk;
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(w, y + S(32) + S(16));

            Controls.Add(languageLabel);
            Controls.Add(languageList);
            Controls.Add(themeLabel);
            Controls.AddRange(themeButtons);
            Controls.Add(highContrast);
            Controls.Add(ok);
            Controls.Add(cancel);
            Theme.Apply(this);
        }

        private static int S(int px)
        {
            return Ui.S(px);
        }

        private void OnOk(object sender, EventArgs e)
        {
            string language = ((Choice)languageList.SelectedItem).Code;
            string theme = Settings.ThemeMode;
            foreach (RadioButton button in themeButtons)
                if (button.Checked) theme = (string)button.Tag;

            bool changed = language != Settings.Language || theme != Settings.ThemeMode;
            Settings.Language = language;
            Settings.ThemeMode = theme;
            if (changed) Settings.Save();
            DialogResult = changed ? DialogResult.OK : DialogResult.Cancel;
        }
    }
}
