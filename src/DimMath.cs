using System;

namespace Keycap
{
    /// <summary>
    /// The arithmetic behind software dimming, with no windows and no native
    /// calls in it - so it can be checked by the small harness in tests/
    /// without the rest of Keycap, and so the veil and the gamma ramp are
    /// guaranteed to darken by the same amount at the same percentage.
    ///
    /// Both ways of dimming share one darkness: the veil covers the picture
    /// with black at an opacity of VeilAlpha, and the gamma ramp scales the
    /// display's output by GammaScale, which is exactly what is left once the
    /// same opacity of black is laid over it. A display that refuses its ramp
    /// and falls back to the veil therefore does not jump in brightness, and
    /// neither does one that starts accepting it.
    ///
    /// Below the gamma floor the two are used together on purpose: the ramp
    /// is held at GammaFloor, the darkest output Windows will accept in a
    /// ramp, and HybridVeilAlpha of veil over it makes up the rest - so
    /// GammaRampScale * (1 - HybridVeilAlpha) is GammaScale at every
    /// percentage, and the picture reaches the chosen level while the
    /// pointer and dialogs still dim as far as the output stage allows.
    /// </summary>
    public static class DimMath
    {
        /// <summary>
        /// The darkest either dimming goes. Even at 0% it lets 15% of the
        /// picture through: a screen dimmed to pure black would leave no way
        /// to find the pointer, the Settings window or the key that brings it
        /// back.
        /// </summary>
        public const float MaxDark = 0.85f;

        /// <summary>How dark the veil is at this brightness, as an opacity from
        /// 0 (nothing covered, at 100%) to MaxDark (at 0%).</summary>
        public static float VeilAlpha(int percent)
        {
            int p = Math.Max(0, Math.Min(100, percent));
            return (100 - p) / 100f * MaxDark;
        }

        /// <summary>
        /// How much of the display's own output survives at this brightness:
        /// 1 at 100% - the untouched picture - down to 1 - MaxDark at 0%.
        /// </summary>
        public static float GammaScale(int percent)
        {
            return 1f - VeilAlpha(percent);
        }

        /// <summary>
        /// The darkest output scale Windows will accept in a gamma ramp: its
        /// entries may not stray too far from the identity ramp - in practice
        /// the top entry must stay at about half of 65535 - and a ramp that
        /// asks for more darkness than this is refused outright.
        /// </summary>
        public const float GammaFloor = 0.5f;

        /// <summary>
        /// The scale a gamma ramp is actually set to at this brightness:
        /// GammaScale, but never below the floor Windows accepts. At 100%
        /// GammaScale is already 1, so the floor only bites at the dark end.
        /// </summary>
        public static float GammaRampScale(int percent)
        {
            return Math.Max(GammaScale(percent), GammaFloor);
        }

        /// <summary>
        /// The veil opacity that, laid over a display already scaled by
        /// GammaRampScale, lands on the same total darkness as GammaScale
        /// alone: nothing wherever the ramp can carry the level by itself,
        /// growing as the level drops below what the ramp is allowed to do,
        /// to 0.70 at 0%.
        /// </summary>
        public static float HybridVeilAlpha(int percent)
        {
            return 1f - GammaScale(percent) / GammaRampScale(percent);
        }

        /// <summary>
        /// One entry of a gamma ramp, scaled: the entry the display (or its
        /// calibration) asked for, multiplied by how much output survives.
        /// The scale is clamped before it is used - a caller passing more than
        /// 1 would otherwise brighten the display past what it was, which is
        /// never what dimming means - and the result is rounded and clamped to
        /// what a ramp entry can hold.
        /// </summary>
        public static ushort ScaleEntry(ushort original, float scale)
        {
            float s = Math.Max(0f, Math.Min(1f, scale));
            int v = (int)Math.Round(original * s);
            if (v < 0) v = 0;
            if (v > 65535) v = 65535;
            return (ushort)v;
        }

        /// <summary>
        /// Whether full dimming is in force: it only ever rides on software
        /// dimming, and only for displays that are dimmed in software at all,
        /// so both switches have to be on before a ramp is touched.
        /// </summary>
        public static bool Applies(bool softDim, bool fullDim)
        {
            return softDim && fullDim;
        }
    }
}
