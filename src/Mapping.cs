using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Keycap
{
    /// <summary>
    /// What the remapper does, and where it is stored. This replaces the
    /// AutoHotkey script entirely: a small text file Keycap owns, rather than
    /// another program's source code parsed at arm's length.
    /// </summary>
    public static class Mapping
    {
        public class Rule
        {
            public string From;       // hardware scancode, 3 hex digits
            public string To;         // scancode to send instead
            public string Text;       // or literal text, when no scancode fits
            public string ShiftText;
            public int Lang;          // 0 = every layout, else a Windows langid
            public string Note;
        }

        /// <summary>
        /// Something that types a block of text: either a key combination, or
        /// a word that expands as soon as you finish typing it.
        /// </summary>
        public class Phrase
        {
            public int Mods;      // 1 = Command, 2 = Option, 4 = Shift
            public int Vk;        // the key pressed with them
            public string Word = "";   // or a typed trigger, instead of a combination
            public string Text;

            public bool IsWord { get { return Word != null && Word.Length > 0; } }

            public string Combo
            {
                get
                {
                    if (IsWord) return Word;
                    string s = "";
                    if ((Mods & 1) != 0) s += "Cmd+";
                    if ((Mods & 2) != 0) s += "Opt+";
                    if ((Mods & 4) != 0) s += "Shift+";
                    return s + KeyName(Vk);
                }
            }

            /// <summary>Single-line rendering of the text, for lists.</summary>
            public string Preview
            {
                get { return Text.Replace("\r", "").Replace("\n", "  "); }
            }

            public static string KeyName(int vk)
            {
                if (vk >= 0x30 && vk <= 0x5A) return ((char)vk).ToString();
                switch (vk)
                {
                    case 0x20: return "Space";
                    case 0x0D: return "Enter";
                    case 0xBC: return ",";
                    case 0xBE: return ".";
                    case 0xBD: return "-";
                }
                if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x6F);
                return "VK" + vk.ToString("X2");
            }
        }

        public class Mod
        {
            public string Src;        // LWin, RWin, LAlt, RAlt, LCtrl, RCtrl
            public string Dst;        // what it acts as
            public string Label, Act;
        }

        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keycap");
        public static readonly string File_ = Path.Combine(Dir, "mapping.txt");

        public static List<Rule> Scans = new List<Rule>();
        public static List<Mod> Mods = new List<Mod>();
        public static List<Phrase> Phrases = new List<Phrase>();
        public static DateTime Modified;

        /// <summary>Whether the on-screen indicator is shown at all.</summary>
        public static bool ShowIndicator = true;

        /// <summary>F1 and F2 drive display brightness, rather than being plain F1 and F2.</summary>
        public static bool BrightnessKeys = true;

        /// <summary>How far one press of F1 or F2 moves the brightness, in percent.</summary>
        public static int BrightnessStep = 10;

        /// <summary>F1 and F2 move every display together, rather than the one under the mouse.</summary>
        public static bool BrightnessAll = false;

        /// <summary>Dim, in software, a display that does not answer DDC/CI.</summary>
        public static bool SoftDim = true;

        // Software brightness by monitor interface path, only below 100. The
        // brightness worker reads it while the UI thread writes it, so it is
        // only touched under _dimLock.
        static readonly Dictionary<string, int> _dims =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static readonly object _dimLock = new object();
        static System.Windows.Forms.Timer _dimSave;

        /// <summary>The software brightness saved for a monitor - 100 when there is none.</summary>
        public static int DimFor(string path)
        {
            if (path == null) return 100;
            lock (_dimLock)
            {
                int v;
                return _dims.TryGetValue(path, out v) ? v : 100;
            }
        }

        /// <summary>
        /// Remember a monitor's software brightness. UI thread. The file is
        /// written a moment after the last change rather than on every one: a
        /// held key sets a new level many times a second, and each save would
        /// rewrite mapping.txt.
        /// </summary>
        public static void SetDim(string path, int percent)
        {
            if (path == null) return;
            lock (_dimLock)
            {
                if (percent >= 100) _dims.Remove(path);
                else _dims[path] = Math.Max(0, percent);
            }
            if (_dimSave == null)
            {
                _dimSave = new System.Windows.Forms.Timer();
                _dimSave.Interval = 1500;
                _dimSave.Tick += delegate { _dimSave.Stop(); Save(); };
            }
            _dimSave.Stop();
            _dimSave.Start();
        }

        /// <summary>Langid whose rules the UI is currently showing.</summary>
        public static int ViewLang = 0;

        /// <summary>pt-PT scancode -> what Windows produces from it.</summary>
        /// <summary>
        /// What a key can be made to type. Worded the way the key is read, not
        /// by scancode - the number matters to the hook, never to the reader.
        /// </summary>
        public static readonly string[][] Targets = new string[][] {
            new string[] { "00C", "'  and  ?" },
            new string[] { "00D", "«  and  »" },
            new string[] { "01A", "+  and  *" },
            new string[] { "01B", "accents  ´  `" },
            new string[] { "027", "ç  and  Ç" },
            new string[] { "028", "º  and  ª" },
            new string[] { "029", "\\  and  |" },
            new string[] { "02B", "accents  ~  ^" },
            new string[] { "056", "<  and  >" },
        };

        public static string TargetLabel(string sc)
        {
            foreach (string[] t in Targets) if (t[0] == sc) return t[1];
            return "SC" + sc;
        }

        static Rule R(string from, string to, int lang, string note)
        {
            Rule r = new Rule();
            r.From = from; r.To = to; r.Lang = lang; r.Note = note;
            return r;
        }

        static Mod M(string src, string dst)
        {
            Mod m = new Mod();
            m.Src = src; m.Dst = dst;
            m.Label = Label(src); m.Act = Act(dst);
            return m;
        }

        static string Label(string src)
        {
            switch (src)
            {
                case "LWin": return "left Command";
                case "RWin": return "right Command";
                case "LAlt": return "left Option";
                case "RAlt": return "right Option";
                case "LCtrl": return "left Control";
                case "RCtrl": return "right Control";
            }
            return src;
        }

        static string Act(string dst)
        {
            switch (dst)
            {
                case "LCtrl": case "RCtrl": return "Ctrl";
                case "LWin": case "RWin": return "Windows key";
                case "LAlt": case "RAlt": return "Alt";
            }
            return dst;
        }

        /// <summary>The defaults: exactly what the script ended up doing.</summary>
        public static void LoadDefaults()
        {
            Phrases.Clear();
            Mods.Clear();
            Mods.Add(M("LWin", "LCtrl"));    // Command -> Ctrl
            Mods.Add(M("LAlt", "LAlt"));     // Option  -> Alt, keeping the Mac editing keys

            Scans.Clear();
            // Portuguese: Apple's board disagrees with Microsoft pt-PT on six keys.
            Scans.Add(R("00D", "01A", 0x0816, "key printed + *"));
            Scans.Add(R("01A", "028", 0x0816, "key printed º ª"));
            Scans.Add(R("028", "02B", 0x0816, "key printed ~ ^  (dead key)"));
            Scans.Add(R("02B", "029", 0x0816, "key printed \\ |"));
            Scans.Add(R("029", "056", 0x0816, "key printed < >"));

            Rule pm = new Rule();
            pm.From = "056"; pm.Text = "±"; pm.ShiftText = "§";
            pm.Lang = 0x0816; pm.Note = "key below Esc, as printed";
            Scans.Add(pm);

            // US: no key left of Z, so only Apple's hardware crossing matters.
            Scans.Add(R("056", "029", 0x0409, "key below Esc -> US ` ~"));
            Scans.Add(R("029", "056", 0x0409, "extra ISO key, inert on US"));
        }

        public static bool InScope(int lang)
        {
            return lang == 0 || ViewLang == 0 || lang == ViewLang;
        }

        public static Rule FindScan(string sc)
        {
            foreach (Rule r in Scans) if (r.From == sc && InScope(r.Lang)) return r;
            return null;
        }

        /// <summary>Lookup used by the hook: exact layout, no UI scoping.</summary>
        public static Rule FindScan(string sc, int lang)
        {
            foreach (Rule r in Scans)
                if (r.From == sc && (r.Lang == 0 || r.Lang == lang)) return r;
            return null;
        }

        public static Phrase FindPhrase(int mods, int vk)
        {
            foreach (Phrase p in Phrases)
                if (!p.IsWord && p.Mods == mods && p.Vk == vk) return p;
            return null;
        }

        public static void AddPhrase(int mods, int vk, string text)
        {
            Phrase existing = FindPhrase(mods, vk);
            if (existing != null) { existing.Text = text; Save(); return; }
            Phrase p = new Phrase();
            p.Mods = mods; p.Vk = vk; p.Text = text;
            Phrases.Add(p);
            Save();
        }

        public static Phrase FindWord(string word)
        {
            foreach (Phrase p in Phrases)
                if (p.IsWord && string.Equals(p.Word, word, StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        public static void AddWord(string word, string text)
        {
            Phrase existing = FindWord(word);
            if (existing != null) { existing.Text = text; Save(); return; }
            Phrase p = new Phrase();
            p.Word = word; p.Text = text;
            Phrases.Add(p);
            Save();
        }

        /// <summary>True while at least one phrase is triggered by typing.</summary>
        public static bool AnyWords
        {
            get
            {
                foreach (Phrase p in Phrases) if (p.IsWord) return true;
                return false;
            }
        }

        /// <summary>
        /// The phrase whose trigger word has just been completed - that is, the
        /// one matching the end of what was typed. The longest match wins, so
        /// "mail" and "mymail" can both exist.
        /// </summary>
        public static Phrase FindWordAt(string typed)
        {
            Phrase best = null;
            foreach (Phrase p in Phrases)
            {
                if (!p.IsWord || typed.Length < p.Word.Length) continue;
                if (string.Compare(typed, typed.Length - p.Word.Length,
                                   p.Word, 0, p.Word.Length,
                                   StringComparison.OrdinalIgnoreCase) != 0) continue;
                if (best == null || p.Word.Length > best.Word.Length) best = p;
            }
            return best;
        }

        public static void RemovePhrase(Phrase p)
        {
            Phrases.Remove(p);
            Save();
        }

        public static Mod FindMod(string src)
        {
            foreach (Mod m in Mods) if (m.Src == src) return m;
            return null;
        }

        /// <summary>Set, change or clear one key's remap.</summary>
        public static void SetScan(string from, string to, int lang)
        {
            for (int i = 0; i < Scans.Count; i++)
                if (Scans[i].From == from && Scans[i].Lang == lang)
                {
                    if (to == null) { Scans.RemoveAt(i); Save(); return; }
                    Scans[i].To = to;
                    Scans[i].Text = null;
                    Save();
                    return;
                }
            if (to != null) Scans.Add(R(from, to, lang, "set in Keycap"));
            Save();
        }

        /// <summary>Change what a physical modifier acts as. null = leave alone.</summary>
        public static void SetMod(string src, string dst)
        {
            for (int i = 0; i < Mods.Count; i++)
                if (Mods[i].Src == src)
                {
                    if (dst == null) { Mods.RemoveAt(i); Save(); return; }
                    Mods[i] = M(src, dst);
                    Save();
                    return;
                }
            if (dst != null) Mods.Add(M(src, dst));
            Save();
        }

        // ---- persistence ----------------------------------------------------
        // A plain, readable file: one rule per line, so it can be inspected and
        // hand-edited without Keycap running.

        /// <summary>One phrase per line, so newlines have to survive a round trip.</summary>
        static string Escape(string text)
        {
            return text.Replace("\\", "\\\\")
                       .Replace("\r", "")
                       .Replace("\n", "\\n");
        }

        static string Unescape(string text)
        {
            return text.Replace("\\n", "\n")
                       .Replace("\\\\", "\\");
        }

        /// <summary>
        /// Writes the mapping file, and survives not being able to.
        ///
        /// Load() calls this on first run, and Load() runs inside the MainForm constructor,
        /// which is the argument to Application.Run - so an unwritable folder used to come
        /// out of the top-level handler as a stack trace in a message box, and Keycap never
        /// opened at all. A mapping that cannot be saved is worth losing; the remaps that
        /// are already loaded and running are not.
        /// </summary>
        public static void Save()
        {
            try
            {
                Write();
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
            }
        }

        static void Write()
        {
            Directory.CreateDirectory(Dir);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Keycap mapping. Lines: mod <src> <dst>");
            sb.AppendLine("#                       scan <from> <to> <langid>");
            sb.AppendLine("#                       text <from> <plain> <shifted> <langid>");
            sb.AppendLine("#                       phrase <mods> <vk> <text>");
            sb.AppendLine("#                       word <trigger> <text>");
            sb.AppendLine("#                       brightness <0|1>, brightness-step <percent>, brightness-all <0|1>");
            sb.AppendLine("#                       softdim <0|1>, dim <monitor path> <percent>");
            sb.AppendLine("indicator " + (ShowIndicator ? "1" : "0"));
            sb.AppendLine("brightness " + (BrightnessKeys ? "1" : "0"));
            sb.AppendLine("brightness-step " + BrightnessStep);
            sb.AppendLine("brightness-all " + (BrightnessAll ? "1" : "0"));
            sb.AppendLine("softdim " + (SoftDim ? "1" : "0"));
            lock (_dimLock)
                foreach (KeyValuePair<string, int> d in _dims)
                    sb.AppendLine("dim " + d.Key + " " + d.Value);
            foreach (Mod m in Mods)
                sb.AppendLine("mod " + m.Src + " " + m.Dst);
            foreach (Rule r in Scans)
            {
                if (r.Text != null)
                    sb.AppendLine("text " + r.From + " " + r.Text + " " +
                                  (r.ShiftText ?? r.Text) + " " + r.Lang.ToString("X4"));
                else
                    sb.AppendLine("scan " + r.From + " " + r.To + " " + r.Lang.ToString("X4"));
            }
            foreach (Phrase ph in Phrases)
                if (ph.IsWord)
                    sb.AppendLine("word " + ph.Word + " " + Escape(ph.Text));
                else
                    sb.AppendLine("phrase " + ph.Mods + " " + ph.Vk + " " + Escape(ph.Text));
            System.IO.File.WriteAllText(File_, sb.ToString(), new UTF8Encoding(true));
            Modified = DateTime.Now;
        }

        public static void Load()
        {
            if (!System.IO.File.Exists(File_)) { LoadDefaults(); Save(); return; }

            Mods.Clear(); Scans.Clear(); Phrases.Clear();
            lock (_dimLock) _dims.Clear();
            foreach (string raw in System.IO.File.ReadAllLines(File_, new UTF8Encoding(true)))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] p = line.Split(' ');
                try
                {
                    if (p[0] == "indicator" && p.Length >= 2) ShowIndicator = p[1] != "0";
                    else if (p[0] == "brightness" && p.Length >= 2) BrightnessKeys = p[1] != "0";
                    else if (p[0] == "brightness-step" && p.Length >= 2)
                        BrightnessStep = Math.Max(1, Math.Min(50, int.Parse(p[1])));
                    else if (p[0] == "brightness-all" && p.Length >= 2) BrightnessAll = p[1] != "0";
                    else if (p[0] == "softdim" && p.Length >= 2) SoftDim = p[1] != "0";
                    else if (p[0] == "dim" && p.Length >= 3)
                    {
                        // The path never holds a space, so it is one field.
                        int v = Math.Max(0, Math.Min(100, int.Parse(p[2])));
                        lock (_dimLock) if (v < 100) _dims[p[1]] = v;
                    }
                    else if (p[0] == "mod" && p.Length >= 3) Mods.Add(M(p[1], p[2]));
                    else if (p[0] == "scan" && p.Length >= 4)
                        Scans.Add(R(p[1], p[2], int.Parse(p[3], NumberStyles.HexNumber), ""));
                    else if (p[0] == "word" && p.Length >= 3)
                    {
                        Phrase ph = new Phrase();
                        ph.Word = p[1];
                        ph.Text = Unescape(string.Join(" ", p, 2, p.Length - 2));
                        Phrases.Add(ph);
                    }
                    else if (p[0] == "phrase" && p.Length >= 4)
                    {
                        Phrase ph = new Phrase();
                        ph.Mods = int.Parse(p[1]);
                        ph.Vk = int.Parse(p[2]);
                        ph.Text = Unescape(string.Join(" ", p, 3, p.Length - 3));
                        Phrases.Add(ph);
                    }
                    else if (p[0] == "text" && p.Length >= 5)
                    {
                        Rule r = new Rule();
                        r.From = p[1]; r.Text = p[2]; r.ShiftText = p[3];
                        r.Lang = int.Parse(p[4], NumberStyles.HexNumber);
                        Scans.Add(r);
                    }
                }
                catch { }
            }
            if (Mods.Count == 0 && Scans.Count == 0) LoadDefaults();
            Modified = System.IO.File.GetLastWriteTime(File_);
        }
    }
}
