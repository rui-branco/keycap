using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;

namespace Keycap
{
    /// <summary>
    /// Checks GitHub releases for a newer build and installs it on click.
    ///
    /// The swap itself cannot be done by the running program - Windows holds
    /// the .exe open - so it writes a tiny batch file that waits for this
    /// process to exit, replaces the file, and starts the new one.
    /// </summary>
    public static class Updater
    {
        /// <summary>owner/repo the releases are published to.</summary>
        public static string Repo = "rui-branco/keycap";

        public static string LatestTag = "";
        public static string DownloadUrl = "";
        public static bool Available;

        /// <summary>A check is in flight, so the UI can say so.</summary>
        public static bool Checking;

        /// <summary>Why the last check failed, empty when it did not.</summary>
        public static string LastError = "";

        public static event EventHandler Checked;

        public static Version Current
        {
            get
            {
                try { return Assembly.GetExecutingAssembly().GetName().Version; }
                catch { return new Version(1, 0, 0, 0); }
            }
        }

        static Version Parse(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            string t = tag.TrimStart('v', 'V').Trim();
            // pad to four parts so 1.2 and 1.2.0.0 compare equal
            string[] parts = t.Split('.');
            int[] n = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (i < parts.Length)
                {
                    int v;
                    if (!int.TryParse(new string(Array.FindAll(parts[i].ToCharArray(), char.IsDigit)), out v))
                        v = 0;
                    n[i] = v;
                }
            }
            return new Version(n[0], n[1], n[2], n[3]);
        }

        /// <summary>
        /// Look for a newer build, off the UI thread. Never throws, and always
        /// raises <see cref="Checked"/> - the settings panel waits on it to
        /// stop saying "checking", success or not.
        /// </summary>
        public static void CheckAsync()
        {
            if (string.IsNullOrEmpty(Repo) || Checking) return;
            Checking = true;
            LastError = "";
            Thread t = new Thread(delegate ()
            {
                try { Check(); }
                catch (Exception ex) { LastError = ex.Message; }
                finally
                {
                    Checking = false;
                    if (Checked != null) Checked(null, EventArgs.Empty);
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>The version without the trailing zeros nobody ships.</summary>
        public static string Pretty(Version v)
        {
            if (v == null) return "";
            return v.Major + "." + v.Minor + "." + v.Build;
        }

        /// <summary>A release tag the way a person would write it.</summary>
        public static string PrettyTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            return tag.TrimStart('v', 'V').Trim();
        }

        static void Check()
        {
            // GitHub refuses anything below TLS 1.2, and .NET 4 does not pick
            // it by default.
            ServicePointManager.SecurityProtocol =
                (SecurityProtocolType)3072 | SecurityProtocolType.Tls;

            string url = "https://api.github.com/repos/" + Repo + "/releases/latest";
            string json;
            using (WebClient wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Keycap");
                wc.Headers.Add("Accept", "application/vnd.github+json");
                json = wc.DownloadString(url);
            }

            JavaScriptSerializer js = new JavaScriptSerializer();
            var root = js.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
            if (root == null || !root.ContainsKey("tag_name")) return;

            string tag = Convert.ToString(root["tag_name"]);
            Version latest = Parse(tag);
            if (latest == null) return;

            string asset = "";
            if (root.ContainsKey("assets"))
            {
                var list = root["assets"] as System.Collections.ArrayList;
                if (list != null)
                    foreach (var a in list)
                    {
                        var d = a as System.Collections.Generic.Dictionary<string, object>;
                        if (d == null) continue;
                        string name = Convert.ToString(d["name"]);
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            asset = Convert.ToString(d["browser_download_url"]);
                            break;
                        }
                    }
            }

            LatestTag = tag;
            DownloadUrl = asset;
            Available = latest > Current && asset.Length > 0;
        }

        /// <summary>
        /// Download the new build and hand over to a batch file that swaps it
        /// once this process has exited.
        /// </summary>
        public static string Install()
        {
            if (!Available || DownloadUrl.Length == 0) return "nothing to install";

            string exe = Application_ExecutablePath();
            string staged = Path.Combine(Path.GetTempPath(), "keycap-update.exe");
            string script = Path.Combine(Path.GetTempPath(), "keycap-update.cmd");

            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | SecurityProtocolType.Tls;
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Keycap");
                    wc.DownloadFile(DownloadUrl, staged);
                }
            }
            catch (Exception ex) { return "download failed: " + ex.Message; }

            if (!File.Exists(staged) || new FileInfo(staged).Length < 1024)
                return "downloaded file looks wrong";

            int pid = Process.GetCurrentProcess().Id;
            string cmd =
                "@echo off\r\n" +
                ":wait\r\n" +
                "tasklist /fi \"PID eq " + pid + "\" | find \"" + pid + "\" >nul\r\n" +
                "if not errorlevel 1 (ping -n 2 127.0.0.1 >nul & goto wait)\r\n" +
                "copy /y \"" + staged + "\" \"" + exe + "\" >nul\r\n" +
                "start \"\" \"" + exe + "\"\r\n" +
                "del \"%~f0\" >nul 2>&1\r\n";
            File.WriteAllText(script, cmd);

            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
            return "";
        }

        static string Application_ExecutablePath()
        {
            return Assembly.GetExecutingAssembly().Location;
        }
    }
}
