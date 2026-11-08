using System;
using System.Collections.Generic;

namespace Keycap
{
    /// <summary>Pairs each physical modifier's release with its original target.
    /// Several physical keys may own the same injected modifier.</summary>
    internal sealed class ModifierState
    {
        readonly Dictionary<int, int> _pressed = new Dictionary<int, int>();
        readonly Action<int, bool> _send;
        readonly object _gate = new object();

        internal ModifierState(Action<int, bool> send) { _send = send; }

        internal bool Any { get { lock (_gate) return _pressed.Count > 0; } }

        internal bool IsPressed(int source)
        {
            lock (_gate) return _pressed.ContainsKey(source);
        }

        internal bool IsPressed(int source, int target)
        {
            lock (_gate)
            {
                int held;
                return _pressed.TryGetValue(source, out held) && held == target;
            }
        }

        internal bool Holds(int target)
        {
            lock (_gate) return _pressed.ContainsValue(target);
        }

        internal bool Handle(int source, int target, bool down)
        {
            lock (_gate)
            {
                int held;
                if (_pressed.TryGetValue(source, out held))
                {
                    if (!down)
                    {
                        _pressed.Remove(source);
                        if (held != 0 && !_pressed.ContainsValue(held)) _send(held, false);
                    }
                    return held != 0;
                }
                if (!down) return false;

                // Zero records a pass-through press too: changing its rule
                // while held must not turn its release into a remapped one.
                if (target == source) target = 0;
                bool alreadyHeld = target != 0 && _pressed.ContainsValue(target);
                _pressed.Add(source, target);
                if (target != 0 && !alreadyHeld) _send(target, true);
                return target != 0;
            }
        }

        internal int[] Targets()
        {
            lock (_gate)
            {
                HashSet<int> targets = new HashSet<int>(_pressed.Values);
                targets.Remove(0);
                int[] result = new int[targets.Count];
                targets.CopyTo(result);
                return result;
            }
        }

        internal void Restore(int target)
        {
            // The worker may restore while the hook processes a release.
            lock (_gate)
                if (_pressed.ContainsValue(target)) _send(target, true);
        }

        internal void Clear()
        {
            lock (_gate)
            {
                int[] targets = Targets();
                _pressed.Clear();
                foreach (int target in targets) _send(target, false);
            }
        }
    }
}
