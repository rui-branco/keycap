using System;
using System.Collections.Generic;

namespace Keycap
{
    public enum Glyph
    {
        None, BrightDown, BrightUp, Mission, Spotlight, Dictate, DoNotDisturb,
        Prev, Play, Next, Mute, VolDown, VolUp, Globe,
        ArrowLeft, ArrowRight, ArrowUp, ArrowDown
    }

    /// <summary>
    /// One key position on the physical board. Positions use the XKB naming
    /// convention (AE01 = row E, key 1) so the same skeleton can carry the
    /// legends of any language.
    /// </summary>
    public class PosKey
    {
        public string Pos;          // position id, e.g. "AE12", "LSGT", "SPCE"
        public float Units = 1f;
        public string Scan;         // scancode Windows receives here, when mapped
        public string Mod;          // AHK modifier reported from this position
        public string FKey;         // F7..F12 for the media row
        public Glyph Icon = Glyph.None;
        public bool Half;           // half-height (arrow cluster)
        public bool Tall;           // spans this row and the next (ISO enter)
        public bool Hidden;         // reserves width but is not drawn
        public bool Dead;           // never reaches Windows
        public string Why;

        public PosKey(string pos, float units) { Pos = pos; Units = units; }
    }

    public enum Shape { ISO, ANSI }

    /// <summary>A named set of legends for the positions of a physical board.</summary>
    public class Layout
    {
        public string Name;
        public Shape Form;
        // position -> { primary, shifted }
        public Dictionary<string, string[]> Legends = new Dictionary<string, string[]>();

        public string Primary(string pos)
        {
            string[] v;
            return Legends.TryGetValue(pos, out v) ? v[0] : "";
        }
        public string Shifted(string pos)
        {
            string[] v;
            return Legends.TryGetValue(pos, out v) && v.Length > 1 ? v[1] : "";
        }
    }

    public static class Layouts
    {
        // ---- physical skeletons ------------------------------------------

        static PosKey P(string pos, float u) { return new PosKey(pos, u); }

        /// <summary>
        /// Hardware scancode for every position on the Apple board, so any key
        /// can be inspected and reassigned - not just the six that happened to
        /// need fixing. Note TLDE and LSGT: Apple crosses those two versus a PC.
        /// </summary>
        static readonly string[][] ScanTable = new string[][] {
            new string[]{"ESC","001"},
            new string[]{"FK01","03B"}, new string[]{"FK02","03C"}, new string[]{"FK03","03D"},
            new string[]{"FK04","03E"}, new string[]{"FK05","03F"}, new string[]{"FK06","040"},
            new string[]{"FK07","041"}, new string[]{"FK08","042"}, new string[]{"FK09","043"},
            new string[]{"FK10","044"}, new string[]{"FK11","057"}, new string[]{"FK12","058"},

            new string[]{"TLDE","056"},   // Apple: key below Esc
            new string[]{"AE01","002"}, new string[]{"AE02","003"}, new string[]{"AE03","004"},
            new string[]{"AE04","005"}, new string[]{"AE05","006"}, new string[]{"AE06","007"},
            new string[]{"AE07","008"}, new string[]{"AE08","009"}, new string[]{"AE09","00A"},
            new string[]{"AE10","00B"}, new string[]{"AE11","00C"}, new string[]{"AE12","00D"},
            new string[]{"BKSP","00E"},

            new string[]{"TAB","00F"},
            new string[]{"AD01","010"}, new string[]{"AD02","011"}, new string[]{"AD03","012"},
            new string[]{"AD04","013"}, new string[]{"AD05","014"}, new string[]{"AD06","015"},
            new string[]{"AD07","016"}, new string[]{"AD08","017"}, new string[]{"AD09","018"},
            new string[]{"AD10","019"}, new string[]{"AD11","01A"}, new string[]{"AD12","01B"},
            new string[]{"RTRN","01C"},

            new string[]{"CAPS","03A"},
            new string[]{"AC01","01E"}, new string[]{"AC02","01F"}, new string[]{"AC03","020"},
            new string[]{"AC04","021"}, new string[]{"AC05","022"}, new string[]{"AC06","023"},
            new string[]{"AC07","024"}, new string[]{"AC08","025"}, new string[]{"AC09","026"},
            new string[]{"AC10","027"}, new string[]{"AC11","028"}, new string[]{"AC12","02B"},
            new string[]{"BKSL","02B"},

            new string[]{"LFSH","02A"},
            new string[]{"LSGT","029"},   // Apple: key left of Z
            new string[]{"AB01","02C"}, new string[]{"AB02","02D"}, new string[]{"AB03","02E"},
            new string[]{"AB04","02F"}, new string[]{"AB05","030"}, new string[]{"AB06","031"},
            new string[]{"AB07","032"}, new string[]{"AB08","033"}, new string[]{"AB09","034"},
            new string[]{"AB10","035"}, new string[]{"RTSH","036"},

            new string[]{"LCTL","01D"}, new string[]{"LALT","038"}, new string[]{"SPCE","039"},
        };

        static string ScanFor(string pos)
        {
            foreach (string[] r in ScanTable) if (r[0] == pos) return r[1];
            return null;
        }

        /// <summary>Fill in the scancode of every position that has one.</summary>
        static void Stamp(List<List<PosKey>> rows)
        {
            foreach (List<PosKey> row in rows)
                foreach (PosKey k in row)
                    if (k.Scan == null) k.Scan = ScanFor(k.Pos);
        }


        /// <summary>
        /// Apple ISO board. Every row sums to 15 units.
        ///
        /// Apple crosses two scancodes relative to a PC: the key below Esc
        /// (TLDE) reports SC056, and the key left of Z (LSGT) reports SC029.
        /// </summary>
        public static List<List<PosKey>> Iso()
        {
            List<List<PosKey>> rows = new List<List<PosKey>>();

            List<PosKey> e0 = new List<PosKey>();
            e0.Add(P("ESC", 1.5f));
            Glyph[] icons = new Glyph[] {
                Glyph.BrightDown, Glyph.BrightUp, Glyph.Mission, Glyph.Spotlight,
                Glyph.Dictate, Glyph.DoNotDisturb, Glyph.Prev, Glyph.Play,
                Glyph.Next, Glyph.Mute, Glyph.VolDown, Glyph.VolUp
            };
            for (int i = 0; i < 12; i++)
            {
                PosKey k = P("FK" + (i + 1).ToString("00"), 1.125f);
                k.Icon = icons[i];
                if (i >= 6) k.FKey = "F" + (i + 1);
                rows_addFKeyMeta(k);
                e0.Add(k);
            }
            rows.Add(e0);

            List<PosKey> e = new List<PosKey>();
            PosKey tlde = P("TLDE", 1f);
            tlde.Scan = "056";
            tlde.Why = "Apple reports the key below Esc as SC056 - a PC reports SC029 here. "
                     + "That crossing is why so many keys landed on the wrong character.";
            e.Add(tlde);
            for (int i = 1; i <= 12; i++)
            {
                PosKey k = P("AE" + i.ToString("00"), 1f);
                if (i == 12) { k.Scan = "00D"; k.Why = "Apple puts this key where pt-PT expects the guillemets."; }
                e.Add(k);
            }
            e.Add(P("BKSP", 2f));
            rows.Add(e);

            List<PosKey> d = new List<PosKey>();
            d.Add(P("TAB", 1.5f));
            for (int i = 1; i <= 12; i++)
            {
                PosKey k = P("AD" + i.ToString("00"), 1f);
                if (i == 11) { k.Scan = "01A"; k.Why = "Apple's key right of P sits where pt-PT expects + *."; }
                d.Add(k);
            }
            d.Add(P("RTRN", 1.5f));      // upper half of the ISO enter
            rows.Add(d);

            List<PosKey> c = new List<PosKey>();
            c.Add(P("CAPS", 1.75f));
            for (int i = 1; i <= 12; i++)
            {
                PosKey k = P("AC" + i.ToString("00"), 1f);
                if (i == 11) { k.Scan = "028"; k.Why = "This scancode was falling through to the ordinal, so two keys typed it and nothing typed the tilde."; }
                if (i == 12) { k.Scan = "02B"; k.Why = "Apple puts backslash on the scancode pt-PT reserves for the dead tilde."; }
                c.Add(k);
            }
            PosKey below = P("RTRN2", 1.25f);
            below.Hidden = true;           // the tall enter already covers this slot
            c.Add(below);
            rows.Add(c);

            List<PosKey> b = new List<PosKey>();
            b.Add(P("LFSH", 1.25f));
            PosKey lsgt = P("LSGT", 1f);
            lsgt.Scan = "029";
            lsgt.Why = "Apple reports the key left of Z as SC029, the scancode a PC uses below Esc.";
            b.Add(lsgt);
            for (int i = 1; i <= 10; i++) b.Add(P("AB" + i.ToString("00"), 1f));
            b.Add(P("RTSH", 2.75f));
            rows.Add(b);

            rows.Add(BottomRow());
            Stamp(rows);
            return rows;
        }

        /// <summary>Apple ANSI board: no key left of Z, wide enter, tall backslash.</summary>
        public static List<List<PosKey>> Ansi()
        {
            List<List<PosKey>> rows = Iso();

            // row E keeps TLDE but it is the backtick key on ANSI
            // row D: backslash takes the ISO enter's slot
            List<PosKey> d = rows[2];
            d[d.Count - 1] = P("BKSL", 1.5f);

            // row C: full-width enter instead of the ISO enter's lower half
            List<PosKey> c = rows[3];
            c[c.Count - 1] = P("RTRN", 2.25f);
            c.RemoveAt(c.Count - 2);     // AC12 does not exist on ANSI

            // row B: no LSGT, wider left shift
            List<PosKey> b = rows[4];
            b.RemoveAt(1);
            b[0] = P("LFSH", 2.25f);

            Stamp(rows);
            return rows;
        }

        static void rows_addFKeyMeta(PosKey k) { }

        static List<PosKey> BottomRow()
        {
            // globe + control + option + command + space + command + option,
            // then the inverted-T arrow cluster. 15 units.
            List<PosKey> a = new List<PosKey>();

            PosKey globe = P("GLBE", 1f);
            globe.Icon = Glyph.Globe;
            globe.Dead = true;
            globe.Why = "Consumed inside the keyboard. Raw Input on both the keyboard collection "
                      + "and Apple's vendor page 0xFF00 receives nothing from this key, so no "
                      + "script can bind it.";
            a.Add(globe);

            PosKey ctl = P("LCTL", 1f); ctl.Mod = "LCtrl"; a.Add(ctl);
            PosKey opt = P("LALT", 1f); opt.Mod = "LAlt"; a.Add(opt);
            PosKey cmd = P("LWIN", 1.25f); cmd.Mod = "LWin"; a.Add(cmd);
            a.Add(P("SPCE", 5.5f));
            PosKey cmd2 = P("RWIN", 1.25f); cmd2.Mod = "RWin"; a.Add(cmd2);
            PosKey opt2 = P("RALT", 1f); opt2.Mod = "RAlt"; a.Add(opt2);

            PosKey left = P("LEFT", 1f); left.Icon = Glyph.ArrowLeft; a.Add(left);

            PosKey up = P("UP", 1f); up.Icon = Glyph.ArrowUp; up.Half = true; a.Add(up);
            PosKey dn = P("DOWN", 1f); dn.Icon = Glyph.ArrowDown; dn.Half = true; a.Add(dn);

            PosKey right = P("RGHT", 1f); right.Icon = Glyph.ArrowRight; a.Add(right);
            return a;
        }

        // ---- legend tables -------------------------------------------------

        static void Set(Layout l, string pos, string a, string b)
        {
            l.Legends[pos] = new string[] { a, b };
        }

        static void Letters(Layout l, string rowPrefix, string letters)
        {
            for (int i = 0; i < letters.Length; i++)
                Set(l, rowPrefix + (i + 1).ToString("00"), letters[i].ToString(), "");
        }

        static void Common(Layout l)
        {
            Set(l, "ESC", "esc", "");
            Set(l, "BKSP", "delete", "");
            Set(l, "TAB", "tab", "");
            Set(l, "CAPS", "caps", "");
            Set(l, "RTRN", "enter", "");
            Set(l, "RTRN2", "", "");
            Set(l, "LFSH", "shift", "");
            Set(l, "RTSH", "shift", "");
            Set(l, "GLBE", "", "");
            Set(l, "LCTL", "control", "");
            Set(l, "LALT", "option", "");
            Set(l, "LWIN", "command", "");
            Set(l, "SPCE", "", "");
            Set(l, "RWIN", "command", "");
            Set(l, "RALT", "option", "");
            Set(l, "LEFT", "", ""); Set(l, "RGHT", "", "");
            Set(l, "UP", "", ""); Set(l, "DOWN", "", "");
            for (int i = 1; i <= 12; i++) Set(l, "FK" + i.ToString("00"), "F" + i, "");
        }

        static Layout Make(string name, Shape form, string[] rowE, string[] rowD,
                           string[] rowC, string[] rowB, string tlde, string lsgt)
        {
            Layout l = new Layout();
            l.Name = name;
            l.Form = form;
            Common(l);

            // rowE: 13 entries "primary|shift" for AE01..AE12 preceded by TLDE
            Set(l, "TLDE", tlde.Split('|')[0], tlde.Split('|')[1]);
            for (int i = 0; i < rowE.Length; i++)
            {
                string[] parts = rowE[i].Split('|');
                Set(l, "AE" + (i + 1).ToString("00"), parts[0], parts.Length > 1 ? parts[1] : "");
            }
            for (int i = 0; i < rowD.Length; i++)
            {
                string[] parts = rowD[i].Split('|');
                Set(l, "AD" + (i + 1).ToString("00"), parts[0], parts.Length > 1 ? parts[1] : "");
            }
            for (int i = 0; i < rowC.Length; i++)
            {
                string[] parts = rowC[i].Split('|');
                Set(l, "AC" + (i + 1).ToString("00"), parts[0], parts.Length > 1 ? parts[1] : "");
            }
            for (int i = 0; i < rowB.Length; i++)
            {
                string[] parts = rowB[i].Split('|');
                Set(l, "AB" + (i + 1).ToString("00"), parts[0], parts.Length > 1 ? parts[1] : "");
            }
            if (lsgt != null)
                Set(l, "LSGT", lsgt.Split('|')[0], lsgt.Split('|')[1]);
            Set(l, "BKSL", "\\", "|");
            return l;
        }

        static readonly string[] DigitsCommon = new string[] {
            "1|!", "2|\"", "3|#", "4|$", "5|%", "6|&", "7|/", "8|(", "9|)", "0|="
        };

        public static List<Layout> All()
        {
            List<Layout> all = new List<Layout>();

            // Portuguese (Apple, ISO) - the board actually on this desk
            all.Add(Make("Portuguese", Shape.ISO,
                Join(DigitsCommon, new string[] { "'|?", "+|*" }),
                Split("Q W E R T Y U I O P º|ª ´|`"),
                Split("A S D F G H J K L Ç ~|^ \\||"),
                Split("Z X C V B N M ,|; .|: -|_"),
                "±|§", "<|>"));

            // Spanish (Apple, ISO)
            all.Add(Make("Spanish", Shape.ISO,
                Join(DigitsCommon, new string[] { "'|?", "¡|¿" }),
                Split("Q W E R T Y U I O P `|^ +|*"),
                Split("A S D F G H J K L Ñ ´|¨ Ç|}"),
                Split("Z X C V B N M ,|; .|: -|_"),
                "º|ª", "<|>"));

            // German (Apple, ISO)
            all.Add(Make("German", Shape.ISO,
                Join(new string[] { "1|!", "2|\"", "3|§", "4|$", "5|%", "6|&",
                                    "7|/", "8|(", "9|)", "0|=" },
                     new string[] { "ß|?", "´|`" }),
                Split("Q W E R T Z U I O P Ü +|*"),
                Split("A S D F G H J K L Ö Ä #|'"),
                Split("Y X C V B N M ,|; .|: -|_"),
                "^|°", "<|>"));

            // French (Apple, ISO)
            all.Add(Make("French", Shape.ISO,
                Join(new string[] { "&|1", "é|2", "\"|3", "'|4", "(|5", "§|6",
                                    "è|7", "!|8", "ç|9", "à|0" },
                     new string[] { ")|°", "-|_" }),
                Split("A Z E R T Y U I O P ^|¨ $|*"),
                Split("Q S D F G H J K L M ù|% `|£"),
                Split("W X C V B N ,|? ;|. :|/ =|+"),
                "@|#", "<|>"));

            // British (Apple, ISO)
            all.Add(Make("British", Shape.ISO,
                Join(new string[] { "1|!", "2|@", "3|£", "4|$", "5|%", "6|^",
                                    "7|&", "8|*", "9|(", "0|)" },
                     new string[] { "-|_", "=|+" }),
                Split("Q W E R T Y U I O P [|{ ]|}"),
                Split("A S D F G H J K L ;|: '|@ #|~"),
                Split("Z X C V B N M ,|< .|> /|?"),
                "§|±", "`|~"));

            // US (Apple, ANSI)
            all.Add(Make("US", Shape.ANSI,
                Join(new string[] { "1|!", "2|@", "3|#", "4|$", "5|%", "6|^",
                                    "7|&", "8|*", "9|(", "0|)" },
                     new string[] { "-|_", "=|+" }),
                Split("Q W E R T Y U I O P [|{ ]|}"),
                Split("A S D F G H J K L ;|: '|\""),
                Split("Z X C V B N M ,|< .|> /|?"),
                "`|~", null));

            return all;
        }

        static string[] Split(string s) { return s.Split(' '); }

        static string[] Join(string[] a, string[] b)
        {
            List<string> l = new List<string>(a);
            l.AddRange(b);
            return l.ToArray();
        }
    }
}
