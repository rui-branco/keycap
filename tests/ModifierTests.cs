using System;
using System.Collections.Generic;
using System.Reflection;
using Keycap;

// Exercise the real hook's modifier branches without installing a hook or
// sending input to the desktop. Only the modifier output delegate is replaced.
static class ModifierTests
{
    const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    static readonly List<string> Sent = new List<string>();
    static readonly Type EventType = typeof(Hook).GetNestedType("KBDLLHOOKSTRUCT", BindingFlags.NonPublic);
    static int _checks;

    static void Check(bool ok, string description)
    {
        _checks++;
        if (!ok) throw new Exception(description);
    }

    static bool Key(int vk, bool down)
    {
        object e = Activator.CreateInstance(EventType);
        EventType.GetField("vkCode").SetValue(e, (uint)vk);
        return (bool)typeof(Hook).GetMethod("Handle", PrivateStatic).Invoke(null, new object[] { e, down });
    }

    static void Map(string src, string dst)
    {
        Mapping.Mods.RemoveAll(delegate(Mapping.Mod m) { return m.Src == src; });
        if (dst != null) Mapping.Mods.Add(new Mapping.Mod { Src = src, Dst = dst });
    }

    static bool Command { get { return (bool)typeof(Hook).GetField("_cmdDown", PrivateStatic).GetValue(null); } }

    static void Expect(string expected)
    {
        string actual = string.Join(",", Sent.ToArray());
        Check(actual == expected, "Expected " + expected + "; got " + actual);
        Sent.Clear();
    }

    static int Main()
    {
        try
        {
            object state = typeof(Hook).GetField("_modifiers", PrivateStatic).GetValue(null);
            state.GetType().GetField("_send", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(state, new Action<int, bool>(delegate(int vk, bool down)
                { Sent.Add(vk.ToString("X2") + (down ? "+" : "-")); }));

            Map("RWin", "LCtrl");
            Check(Key(0x5C, true), "Right Command press must be handled");
            Check(Command, "Right Command must enable Command shortcuts");
            Check(Key(0x5C, false), "Right Command release must be handled");
            Check(!Command, "Right Command release must clear Command state");
            Expect("A2+,A2-");

            Map("RAlt", "LCtrl");
            Key(0xA5, true); Key(0x5C, true);
            Key(0xA5, false);
            Check(Command, "Releasing right Option must keep right Command active");
            Expect("A2+");
            Key(0x5C, false);
            Expect("A2-");

            Key(0xA5, true);
            Map("RAlt", "LWin");
            Key(0xA5, false);
            Expect("A2+,A2-");
            Map("RAlt", "LCtrl");
            Key(0xA5, true);
            Check(!Command, "Option mapped to Ctrl must remain a plain Ctrl");
            Key(0xA5, false);
            Expect("A2+,A2-");

            Map("LWin", "LCtrl");
            Key(0x5B, true); Key(0x5C, true); Key(0xA5, true);
            Key(0x5B, false); Key(0x5C, false);
            Expect("A2+");
            Key(0xA5, false);
            Expect("A2-");

            Key(0x5B, true); Key(0x5C, true); Key(0x5B, false);
            Check(Command, "Releasing one Command must leave the other active");
            Key(0x5C, false);
            Expect("A2+,A2-");

            Key(0x5C, true); Key(0x5C, true);
            Map("RWin", "LAlt");
            Key(0x5C, true); Key(0x5C, false);
            Expect("A2+,A2-");
            Key(0x5C, true); Key(0x5C, false);
            Expect("A4+,A4-");

            Map("RWin", null); Map("RAlt", null); Map("LWin", null);
            Check(!Key(0x5C, true) && !Key(0x5C, false), "Default right Command passes through");
            Check(!Key(0xA5, true) && !Key(0xA5, false), "Default right Option preserves AltGr");
            Check(!Key(0x5B, true) && !Key(0x5B, false), "Leave alone must also work for left Command");
            Expect("");

            Key(0x5C, true);
            Map("RWin", "LCtrl");
            Check(!Key(0x5C, false), "Pass-through release must survive a rule change");
            Expect("");

            foreach (string target in new string[] { "LCtrl", "RCtrl", "LAlt", "RAlt", "LWin" })
            {
                Map("RWin", target);
                Check(Key(0x5C, true) && Key(0x5C, false), "Right Command target " + target);
            }
            Expect("A2+,A2-,A3+,A3-,A4+,A4-,A5+,A5-,5B+,5B-");

            Map("RWin", "RCtrl"); Map("LWin", "RCtrl");
            Key(0x5C, true);
            Check(Command, "Right Command mapped to right Ctrl enables Command shortcuts");
            Key(0x5B, true); Key(0x5C, false);
            Check(Command, "Left Command mapped to right Ctrl remains active");
            Key(0x5B, false);
            Check(!Command, "Releasing both Commands clears right Ctrl shortcut state");
            Expect("A3+,A3-");

            foreach (string target in new string[] { "LCtrl", "RCtrl", "LAlt", "LWin", "RWin" })
            {
                Map("RAlt", target);
                Check(Key(0xA5, true) && Key(0xA5, false), "Right Option target " + target);
            }
            Expect("A2+,A2-,A3+,A3-,A4+,A4-,5B+,5B-,5C+,5C-");

            Map("RWin", "LCtrl"); Map("RAlt", "LCtrl");
            Key(0x5C, true); Key(0xA5, true);
            int[] targets = (int[])state.GetType().GetMethod("Targets", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(state, null);
            Check(targets.Length == 1 && targets[0] == 0xA2, "Shortcut suspension must deduplicate shared targets");
            MethodInfo restore = state.GetType().GetMethod("Restore", BindingFlags.NonPublic | BindingFlags.Instance);
            Key(0x5C, false);
            restore.Invoke(state, new object[] { 0xA2 });
            Expect("A2+,A2+");
            Key(0xA5, false);
            restore.Invoke(state, new object[] { 0xA2 });
            Expect("A2-");

            Map("RWin", "LCtrl"); Map("RAlt", "LWin");
            Key(0x5C, true); Key(0xA5, true);
            Hook.ReleaseAll();
            Check(!Command, "Recovery must clear Command state");
            Check(Sent.Count == 4 && Sent.Contains("A2-") && Sent.Contains("5B-"), "Recovery releases every mapped target");
            Sent.Clear();
            Check(!Key(0x5C, false) && !Key(0xA5, false), "Late releases after recovery pass through");
            Expect("");

            // Inspect queued requests without running the real lock API.
            Queue<Action> work = (Queue<Action>)typeof(Hook).GetField("_work", PrivateStatic).GetValue(null);
            Check(work.Count == 0, "Worker queue starts empty");
            Check(Key(0x75, true), "F6 lock press is consumed");
            Check(Key(0x75, true) && Key(0x75, true), "F6 repeats are consumed");
            Check(work.Count == 0, "F6 waits for release before requesting lock");
            Check(Key(0x75, false), "F6 lock release is consumed");
            Check(work.Count == 1, "One F6 press queues exactly one lock request");
            Action lockRequest = work.Dequeue();
            Check(lockRequest.Method.Name == "LockDesktop" && lockRequest.Method.DeclaringType == typeof(Hook),
                "F6 queues the system lock handler");
            Check(!Key(0x75, false) && work.Count == 0, "Duplicate F6 release does not lock again");

            Key(0x75, true); Key(0x75, false);
            Check(work.Count == 1, "F6 can request lock on the next press");
            work.Clear();
            Key(0x75, true); Hook.ReleaseAll(); Key(0x75, false);
            Check(work.Count == 0, "Recovery cancels an incomplete lock press");

            typeof(Hook).GetField("_optDown", PrivateStatic).SetValue(null, true);
            typeof(Hook).GetField("_optDst", PrivateStatic).SetValue(null, "LAlt");
            Check(Key(0x75, true), "Option+F6 is handled as a normal function key");
            Check(work.Count == 1 && work.Peek().Method != lockRequest.Method,
                "Option+F6 queues a function key injection rather than lock");
            Check(!(bool)typeof(Hook).GetField("_lockDown", PrivateStatic).GetValue(null),
                "Option+F6 does not arm lock on release");
            Key(0x75, false);
            work.Clear();
            typeof(Hook).GetField("_optDown", PrivateStatic).SetValue(null, false);

            Map("RWin", "RCtrl"); Key(0x5C, true);
            Check(!Key(0x75, true) && !Key(0x75, false) && work.Count == 0,
                "Command+F6 remains a Ctrl shortcut and does not lock");
            Key(0x5C, false);
            Expect("A3+,A3-");
            Check(Shortcuts.MediaFor("F6") == "lock screen", "F6 lock appears in the shortcut reference");
            bool hasLockKey = false;
            foreach (List<PosKey> row in Layouts.Iso())
                foreach (PosKey key in row)
                    if (key.Pos == "FK06" && key.FKey == "F6") hasLockKey = true;
            Check(hasLockKey, "F6 exposes lock in the keyboard inspector");

            Console.WriteLine("PASS: " + _checks + " modifier and lock checks (no desktop input or workstation lock requested)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
