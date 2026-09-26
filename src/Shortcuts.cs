using System.Collections.Generic;

namespace Keycap
{
    /// <summary>
    /// What the hook does beyond the scancode fixes, described once so the
    /// reference lists and the key inspector stay in step with Hook.cs.
    /// </summary>
    public static class Shortcuts
    {
        public static readonly List<string[]> All = new List<string[]> {
            new string[] { "Cmd+C / V / X / A / Z", "copy, paste, cut, select all, undo" },
            new string[] { "Cmd+Left / Right",      "line start / end" },
            new string[] { "Cmd+Up / Down",         "document start / end" },
            new string[] { "Cmd+Shift+arrows",      "select to that point" },
            new string[] { "Cmd+Delete",            "forward delete" },
            new string[] { "Cmd+Q",                 "quit app" },
            new string[] { "Cmd+M",                 "minimise window" },
            new string[] { "Cmd+Space",             "Windows Search" },
            new string[] { "Opt+Left / Right",      "jump word by word" },
            new string[] { "Opt+Shift+arrows",      "select word by word" },
            new string[] { "Opt+Delete",            "delete previous word" },
            new string[] { "Opt+F3 - F12",          "the real function keys" },
            new string[] { "right Cmd + .",         "emoji panel" },
        };

        public static readonly List<string[]> FRow = new List<string[]> {
            new string[] { "F3",  "Task View" },
            new string[] { "F5",  "microphone mute" },
            new string[] { "F7",  "previous track" },
            new string[] { "F8",  "play / pause" },
            new string[] { "F9",  "next track" },
            new string[] { "F10", "volume mute" },
            new string[] { "F11", "volume down" },
            new string[] { "F12", "volume up" },
        };

        public static string MediaFor(string fkey)
        {
            foreach (string[] r in FRow) if (r[0] == fkey) return r[1];
            return null;
        }
    }
}
