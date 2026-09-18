using System;
using System.IO;
using System.Windows.Forms;

namespace Keycap
{
    static class Program
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keycap");

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                LogError(ex);
                MessageBox.Show(ex.ToString(), "Keycap");
            }
        }

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
