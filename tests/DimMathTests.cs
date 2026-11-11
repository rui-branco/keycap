using System;
using Keycap;

namespace KeycapTests
{
    /// <summary>
    /// The DimMath checks. There is no test framework in this repo and no
    /// dependency is worth adding for a handful of sums, so this is a plain
    /// console program: every check says FAIL and names itself when it does
    /// not hold, and the exit code is 0 only when they all hold. Run it with
    /// tests/run-tests.ps1, which compiles it with DimMath.cs alone.
    /// </summary>
    static class DimMathTests
    {
        static int _failures;

        static void Check(bool ok, string name)
        {
            if (ok) Console.WriteLine("PASS " + name);
            else { _failures++; Console.WriteLine("FAIL " + name); }
        }

        static bool Near(float a, float b) { return Math.Abs(a - b) < 0.0001f; }

        static int Main()
        {
            // The veil: nothing covered at 100%, the full floor at 0%.
            Check(Near(DimMath.VeilAlpha(100), 0f), "VeilAlpha 100 -> 0");
            Check(Near(DimMath.VeilAlpha(0), 0.85f), "VeilAlpha 0 -> 0.85");
            Check(Near(DimMath.VeilAlpha(0), DimMath.MaxDark), "VeilAlpha 0 is MaxDark");

            // The darker the request, the darker the veil: as the percentage
            // rises the opacity must never rise with it.
            bool monotone = true;
            float last = float.MaxValue;
            for (int p = 0; p <= 100; p += 5)
            {
                float a = DimMath.VeilAlpha(p);
                if (a > last) monotone = false;
                last = a;
            }
            Check(monotone, "VeilAlpha falls (or holds) as percent rises");

            // Percentages outside 0..100 are clamped, not trusted.
            Check(Near(DimMath.VeilAlpha(-40), DimMath.VeilAlpha(0)), "VeilAlpha clamps below 0");
            Check(Near(DimMath.VeilAlpha(140), DimMath.VeilAlpha(100)), "VeilAlpha clamps above 100");
            Check(Near(DimMath.GammaScale(-40), DimMath.GammaScale(0)), "GammaScale clamps below 0");
            Check(Near(DimMath.GammaScale(140), DimMath.GammaScale(100)), "GammaScale clamps above 100");

            // The ramp: untouched at 100%, the same 15% floor as the veil at 0%.
            Check(Near(DimMath.GammaScale(100), 1f), "GammaScale 100 -> 1");
            Check(Near(DimMath.GammaScale(0), 0.15f), "GammaScale 0 -> 0.15");

            // The two dimmings agree: what the ramp lets through is what is
            // left once the veil's opacity of black is laid over it.
            bool agree = true;
            foreach (int p in new int[] { 0, 1, 15, 25, 50, 85, 99, 100 })
                if (!Near(DimMath.GammaScale(p), 1f - DimMath.VeilAlpha(p))) agree = false;
            Check(agree, "GammaScale is 1 - VeilAlpha");

            // Scaling a ramp entry: identity at scale 1, all the way across.
            Check(DimMath.ScaleEntry(0, 1f) == 0, "ScaleEntry 0 at scale 1");
            Check(DimMath.ScaleEntry(32768, 1f) == 32768, "ScaleEntry 32768 at scale 1");
            Check(DimMath.ScaleEntry(65535, 1f) == 65535, "ScaleEntry 65535 at scale 1");

            // The darkest floor on the brightest entry: 15% of 65535.
            int floor = DimMath.ScaleEntry(65535, 0.15f);
            Check(Math.Abs(floor - 9830) <= 1, "ScaleEntry 65535 at 0.15 is about 9830 (got " + floor + ")");

            Check(DimMath.ScaleEntry(65535, 0f) == 0, "ScaleEntry at scale 0 -> 0");
            Check(DimMath.ScaleEntry(12345, 0f) == 0, "ScaleEntry at scale 0 -> 0 for any entry");

            // A scale over 1 is clamped rather than brightening the display.
            Check(DimMath.ScaleEntry(65535, 2f) == 65535, "ScaleEntry clamps scale above 1");
            Check(DimMath.ScaleEntry(32768, 2f) == 32768, "ScaleEntry clamps scale above 1 mid-ramp");

            // The gamma floor: Windows refuses a ramp darker than about half
            // output, so the ramp never asks for more darkness than that.
            Check(Near(DimMath.GammaRampScale(0), 0.5f), "GammaRampScale 0 -> 0.5 (the floor)");
            Check(Near(DimMath.GammaRampScale(40), 0.5f), "GammaRampScale 40 -> 0.5 (the floor)");
            Check(Near(DimMath.GammaRampScale(50), DimMath.GammaScale(50)), "GammaRampScale 50 is GammaScale 50");
            Check(Near(DimMath.GammaRampScale(50), 0.575f), "GammaRampScale 50 -> 0.575");
            Check(Near(DimMath.GammaRampScale(100), 1f), "GammaRampScale 100 -> 1");

            // The ramp brightens (or holds) as the percentage rises.
            bool rampMonotone = true;
            float lastScale = float.MinValue;
            for (int p = 0; p <= 100; p += 5)
            {
                float s = DimMath.GammaRampScale(p);
                if (s < lastScale) rampMonotone = false;
                lastScale = s;
            }
            Check(rampMonotone, "GammaRampScale rises (or holds) as percent rises");

            // The hybrid veil: nothing wherever the ramp carries the level
            // alone, 0.70 of veil over the floored ramp at 0%.
            Check(Near(DimMath.HybridVeilAlpha(100), 0f), "HybridVeilAlpha 100 -> 0");
            Check(Near(DimMath.HybridVeilAlpha(50), 0f), "HybridVeilAlpha 50 -> 0");
            Check(Near(DimMath.HybridVeilAlpha(42), 0f), "HybridVeilAlpha 42 -> 0");
            Check(Math.Abs(DimMath.HybridVeilAlpha(0) - 0.70f) < 0.001f,
                "HybridVeilAlpha 0 is about 0.70 (got " + DimMath.HybridVeilAlpha(0) + ")");

            // Ramp and hybrid veil together let through exactly what the
            // level asks for - the identity the hybrid rests on:
            // GammaRampScale * (1 - HybridVeilAlpha) == GammaScale.
            bool combined = true;
            foreach (int p in new int[] { 0, 10, 25, 40, 41, 42, 50, 75, 99, 100 })
            {
                float through = DimMath.GammaRampScale(p) * (1f - DimMath.HybridVeilAlpha(p));
                if (Math.Abs(through - DimMath.GammaScale(p)) > 0.001f) combined = false;
            }
            Check(combined, "GammaRampScale * (1 - HybridVeilAlpha) is GammaScale");

            // Full dimming needs both switches.
            Check(DimMath.Applies(true, true), "Applies true, true -> true");
            Check(!DimMath.Applies(true, false), "Applies true, false -> false");
            Check(!DimMath.Applies(false, true), "Applies false, true -> false");
            Check(!DimMath.Applies(false, false), "Applies false, false -> false");

            if (_failures == 0) Console.WriteLine("ALL PASS");
            else Console.WriteLine(_failures + " FAILED");
            return _failures == 0 ? 0 : 1;
        }
    }
}
