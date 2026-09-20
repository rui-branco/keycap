using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Keycap
{
    static class Program
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keycap");

        /// <summary>
        /// Broadcast by a second launch so the instance already sitting in the
        /// tray pops its window up, instead of a duplicate process appearing.
        /// </summary>
        public static readonly int WmShow = RegisterWindowMessage("KeycapShowWindow");

        /// <summary>
        /// Passed by the Startup shortcut, so logging in lands Keycap in the
        /// tray rather than in the window.
        /// </summary>
        public const string BackgroundArg = "--background";

        static Mutex _only;   // held for the life of the process

        [STAThread]
        static void Main(string[] args)
        {
            bool first;
            _only = new Mutex(true, "Local\\Keycap.SingleInstance", out first);
            if (!first)
            {
                // Hand our foreground rights over first, otherwise the running
                // instance is only allowed to flash in the taskbar.
                AllowSetForegroundWindow(ASFW_ANY);
                PostMessage(HWND_BROADCAST, WmShow, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            bool background = IsBackground(args);

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(background));
            }
            catch (Exception ex)
            {
                LogError(ex);
                MessageBox.Show(ex.ToString(), "Keycap");
            }
            finally
            {
                GC.KeepAlive(_only);
            }
        }

        static bool IsBackground(string[] args)
        {
            foreach (string a in args)
            {
                if (string.Equals(a, BackgroundArg, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-background", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/background", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-b", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        const uint ASFW_ANY = 0xffffffff;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(uint pid);

        static void LogError(Exception ex)
        {
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, "error.log"),
                    DateTime.Now.ToString("s") + "  " + ex.ToString() + Environment.NewLine);
            }
            catch { }
        }
    }
}
