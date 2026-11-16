using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SpeedUpMyPC
{
    internal enum TextRole { Normal, Muted, Faint }

    // Every color the app uses. Text colors are picked to stay readable on their
    // background (at least 4.5:1, the WCAG AA level for normal text).
    internal sealed class Palette
    {
        public bool IsDark;
        public bool UseSystemButtons;   // light and high contrast: Windows draws buttons as usual
        public Color Background;        // windows
        public Color Surface;           // lists and input boxes
        public Color Text;
        public Color Muted;             // status lines and notes
        public Color Faint;             // the version number
        public Color Link;
        public Color Border;
        public Color Button;
        public Color ButtonHover;
        public Color Selected;          // the chosen tab, pressed buttons
    }

    // Applies the palette to a window and everything in it. Follows Windows' light/dark
    // setting (live, while the app is open) unless the user picked one in Settings.
    // In Windows' high contrast mode it always uses the high contrast colors.
    internal static class Theme
    {
        public static readonly Palette Light = new Palette
        {
            IsDark = false,
            UseSystemButtons = true,
            Background = Color.White,
            Surface = Color.White,
            Text = Color.Black,
            Muted = Color.DimGray,                       // 5.5:1 on white
            Faint = Color.FromArgb(0x76, 0x76, 0x76),    // 4.5:1 on white
            Link = Color.FromArgb(0x00, 0x00, 0xFF),     // Windows' usual link blue, 8.6:1
            Border = Color.FromArgb(0xAD, 0xAD, 0xAD),
            Button = Color.FromArgb(0xFB, 0xFB, 0xFB),
            ButtonHover = Color.FromArgb(0xE5, 0xF1, 0xFB),
            Selected = Color.FromArgb(0xCC, 0xE4, 0xF7),
        };

        public static readonly Palette Dark = new Palette
        {
            IsDark = true,
            UseSystemButtons = false,
            Background = Color.FromArgb(0x20, 0x20, 0x20),
            Surface = Color.FromArgb(0x2B, 0x2B, 0x2B),
            Text = Color.FromArgb(0xF2, 0xF2, 0xF2),     // 14.6:1 on the background
            Muted = Color.FromArgb(0xBE, 0xBE, 0xBE),    // 8.8:1
            Faint = Color.FromArgb(0x9E, 0x9E, 0x9E),    // 6.1:1
            Link = Color.FromArgb(0x7A, 0xB8, 0xFF),     // 7.9:1
            Border = Color.FromArgb(0x60, 0x60, 0x60),
            Button = Color.FromArgb(0x33, 0x33, 0x33),
            ButtonHover = Color.FromArgb(0x40, 0x40, 0x40),
            Selected = Color.FromArgb(0x26, 0x4F, 0x78),
        };

        private static Palette HighContrast()
        {
            return new Palette
            {
                IsDark = false,
                UseSystemButtons = true,
                Background = SystemColors.Window,
                Surface = SystemColors.Window,
                Text = SystemColors.WindowText,
                Muted = SystemColors.WindowText,
                Faint = SystemColors.WindowText,
                Link = SystemColors.HotTrack,
                Border = SystemColors.WindowFrame,
                Button = SystemColors.Control,
                ButtonHover = SystemColors.Highlight,
                Selected = SystemColors.Highlight,
            };
        }

        private static string mode = Settings.Auto;
        private static bool listening;
        private static bool appliedDark;
        private static bool appliedHighContrast;
        private static readonly ConditionalWeakTable<Control, object> roles = new ConditionalWeakTable<Control, object>();
        private static readonly ConditionalWeakTable<Control, object> hooked = new ConditionalWeakTable<Control, object>();
        private static readonly ConditionalWeakTable<Control, object> headerHooked = new ConditionalWeakTable<Control, object>();

        public static void Init(string setting)
        {
            mode = setting;
            if (!listening)
            {
                SystemEvents.UserPreferenceChanged += OnWindowsSettingChanged;
                listening = true;
            }
        }

        public static Palette Current
        {
            get
            {
                if (SystemInformation.HighContrast) return HighContrast();
                if (mode == Settings.Dark) return Dark;
                if (mode == Settings.Light) return Light;
                return WindowsUsesDarkApps() ? Dark : Light;
            }
        }

        public static void SetRole(Control control, TextRole role)
        {
            roles.Remove(control);
            roles.Add(control, role);
        }

        // Colors a window (or a panel added later) and everything in it.
        public static void Apply(Control root)
        {
            Palette p = Current;
            appliedDark = p.IsDark;
            appliedHighContrast = SystemInformation.HighContrast;
            ApplyTree(root, p);
        }

        private static void ApplyTree(Control control, Palette p)
        {
            ApplyOne(control, p);
            foreach (Control child in control.Controls) ApplyTree(child, p);
        }

        private static void ApplyOne(Control c, Palette p)
        {
            if (c is Form)
            {
                c.BackColor = p.Background;
                c.ForeColor = p.Text;
                HookNative(c);
            }
            else if (c is LinkLabel)
            {
                var link = (LinkLabel)c;
                link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = p.Link;
                link.DisabledLinkColor = p.Faint;
                link.ForeColor = RoleColor(c, p);
            }
            else if (c is Label)
            {
                c.ForeColor = RoleColor(c, p);
            }
            else if (c is RadioButton && ((RadioButton)c).Appearance == Appearance.Button)
            {
                // Tab buttons: always flat, so the chosen one stands out in every theme.
                var tab = (RadioButton)c;
                tab.FlatStyle = FlatStyle.Flat;
                tab.BackColor = p.Button;
                tab.ForeColor = p.Text;
                tab.FlatAppearance.BorderColor = p.Border;
                tab.FlatAppearance.CheckedBackColor = p.Selected;
                tab.FlatAppearance.MouseOverBackColor = p.ButtonHover;
            }
            else if (c is RadioButton || c is CheckBox)
            {
                c.ForeColor = p.Text; // option buttons: Windows draws the circle, only the text needs a color
            }
            else if (c is ButtonBase)
            {
                var button = (ButtonBase)c;
                if (p.UseSystemButtons)
                {
                    button.FlatStyle = FlatStyle.Standard;
                    button.BackColor = Color.Empty;   // back to Windows' own look
                    button.ForeColor = Color.Empty;
                }
                else
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.BackColor = p.Button;
                    button.ForeColor = p.Text;
                    button.FlatAppearance.BorderColor = p.Border;
                    button.FlatAppearance.MouseOverBackColor = p.ButtonHover;
                    button.FlatAppearance.MouseDownBackColor = p.Selected;
                }
            }
            else if (c is ListView || c is ListBox || c is TextBoxBase || c is UpDownBase || c is ComboBox)
            {
                c.BackColor = p.Surface;
                c.ForeColor = p.Text;
                if (c is ComboBox) ((ComboBox)c).FlatStyle = p.UseSystemButtons ? FlatStyle.Standard : FlatStyle.Flat;
                if (c is ListView) DrawHeadersInDark((ListView)c, p.IsDark);
                if (c is ListView || c is ListBox) HookNative(c);
            }
            // Panels and the rest take their parent's colors.
        }

        // Windows' dark list theme darkens the column headers but leaves their text dark gray,
        // which is unreadable. In dark mode the headers are drawn here; rows are still drawn by Windows.
        private static void DrawHeadersInDark(ListView list, bool dark)
        {
            object ignored;
            if (!headerHooked.TryGetValue(list, out ignored))
            {
                headerHooked.Add(list, new object());
                list.DrawItem += (s, e) => e.DrawDefault = true;
                list.DrawSubItem += (s, e) => e.DrawDefault = true;
                list.DrawColumnHeader += (s, e) =>
                {
                    Palette p = Current;
                    if (!p.IsDark) { e.DrawDefault = true; return; }
                    Rectangle r = e.Bounds;
                    using (var back = new SolidBrush(p.Surface)) e.Graphics.FillRectangle(back, r);
                    using (var line = new Pen(p.Border))
                    {
                        e.Graphics.DrawLine(line, r.Right - 1, r.Top + Ui.S(4), r.Right - 1, r.Bottom - Ui.S(4));
                        e.Graphics.DrawLine(line, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
                    }
                    var text = new Rectangle(r.X + Ui.S(6), r.Y, r.Width - Ui.S(10), r.Height);
                    TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, text, p.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                };
            }
            list.OwnerDraw = dark; // light: Windows draws everything, as before
            list.Invalidate();
        }

        private static Color RoleColor(Control c, Palette p)
        {
            object role;
            if (!roles.TryGetValue(c, out role)) return p.Text;
            switch ((TextRole)role)
            {
                case TextRole.Muted: return p.Muted;
                case TextRole.Faint: return p.Faint;
                default: return p.Text;
            }
        }

        // The title bar and scroll bars are drawn by Windows, so they're switched with
        // Windows' own dark mode. Redone whenever the control's window is recreated.
        private static void HookNative(Control c)
        {
            object ignored;
            if (!hooked.TryGetValue(c, out ignored))
            {
                hooked.Add(c, new object());
                c.HandleCreated += (s, e) => ApplyNative((Control)s, appliedDark);
            }
            if (c.IsHandleCreated) ApplyNative(c, appliedDark);
        }

        private static void ApplyNative(Control c, bool dark)
        {
            try
            {
                if (c is Form)
                {
                    int on = dark ? 1 : 0;
                    if (DwmSetWindowAttribute(c.Handle, 20, ref on, 4) != 0)  // Windows 10 20H1 and later
                        DwmSetWindowAttribute(c.Handle, 19, ref on, 4);       // older Windows 10
                    return;
                }
                string theme = dark ? (c is ListView ? "DarkMode_ItemsView" : "DarkMode_Explorer") : null; // null = Windows' default
                SetWindowTheme(c.Handle, theme, null);
                if (c is ListView)
                {
                    IntPtr header = SendMessage(c.Handle, LvmGetHeader, IntPtr.Zero, IntPtr.Zero);
                    if (header != IntPtr.Zero) SetWindowTheme(header, theme, null);
                }
            }
            catch { } // older Windows without these: the app's own colors still apply
        }

        private static bool WindowsUsesDarkApps()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch { return false; }
        }

        private static void OnWindowsSettingChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Application.OpenForms.Count == 0) return;
            Form first = Application.OpenForms[0];
            Ui.Post(first, () =>
            {
                if (Current.IsDark == appliedDark && SystemInformation.HighContrast == appliedHighContrast) return;
                foreach (Form form in Application.OpenForms)
                {
                    Apply(form);
                    form.Invalidate(true);
                }
            });
        }

        private const int LvmGetHeader = 0x101F;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
