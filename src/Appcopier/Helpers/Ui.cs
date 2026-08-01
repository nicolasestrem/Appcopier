using System.Drawing;

namespace Appcopier
{
    /// <summary>
    /// Shared spacing and typography for the rebuilt views.
    /// </summary>
    /// <remarks>
    /// Deliberately small and deliberately not a theme. Colour tokens, the light/dark palettes and the
    /// system-preference walker are PR 9's job; putting a half-built Theme here would mean PR 9 either
    /// inherits an API it did not design or deletes one that already has callers. What lives here is
    /// only what the shell and Home need to avoid hard-coding the same four numbers in two files.
    ///
    /// The fonts are the pairing RestoreConfirmForm already uses, so the new screens do not introduce a
    /// third typographic voice into an app that is mid-revamp.
    ///
    /// Colours below are today's light values verbatim, moved rather than chosen - the shell has to
    /// paint something, and inventing a palette here would make PR 9's diff a redesign instead of a
    /// theming pass.
    /// </remarks>
    internal static class Ui
    {
        internal const int SpaceXs = 4;
        internal const int SpaceS = 8;
        internal const int SpaceM = 12;
        internal const int SpaceL = 24;

        internal const string BodyFamily = "Segoe UI Variable Text";
        internal const string DisplayFamily = "Segoe UI Variable Display";

        /// <summary>Glyph font. A font, therefore DPI-free - which is why glyphs are not images.</summary>
        internal const string IconFamily = "Segoe Fluent Icons";

        internal static Font Body() => new Font(BodyFamily, 9.75f);

        internal static Font BodyBold() => new Font(BodyFamily, 9.75f, FontStyle.Bold);

        internal static Font Title() => new Font(DisplayFamily, 16f, FontStyle.Bold);

        internal static Font Heading() => new Font(DisplayFamily, 12f);

        internal static Font Icon() => new Font(IconFamily, 12f);

        internal static readonly Color Surface = Color.FromArgb(243, 243, 243);

        internal static readonly Color RailSurface = Color.FromArgb(245, 241, 249);

        internal static readonly Color Muted = Color.DimGray;

        /// <summary>
        /// Failure text. Amber is reserved for Skipped and is never green - the styling has to keep
        /// the distinction the engine's three-state result fought for.
        /// </summary>
        internal static readonly Color Danger = Color.FromArgb(168, 34, 34);
    }
}
