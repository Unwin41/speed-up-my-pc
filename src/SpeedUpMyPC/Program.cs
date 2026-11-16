using System;
using System.Windows.Forms;

namespace SpeedUpMyPC
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Settings.Load();

            // Changing the language or theme in Settings reopens the main window with the new choice.
            while (true)
            {
                Lang.Init(Settings.Language);
                Theme.Init(Settings.ThemeMode);
                var form = new MainForm();
                Application.Run(form);
                if (!form.RestartRequested) break;
            }
        }
    }
}
