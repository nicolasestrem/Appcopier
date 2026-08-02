using Microsoft.Win32;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Appcopier
{
    /// <summary>One theme's colour tokens. Two instances exist: <see cref="Theme.Light"/> and Dark.</summary>
    internal sealed class Palette
    {
        internal Color Surface;
        internal Color RailSurface;
        internal Color CardSurface;
        internal Color TextPrimary;
        internal Color TextMuted;
        internal Color Border;
        internal Color Danger;
        internal Color Caution;
        internal Color ChipSucceededBack;
        internal Color ChipSucceededFore;
        internal Color ChipSkippedBack;
        internal Color ChipSkippedFore;
        internal Color ChipFailedBack;
        internal Color ChipFailedFore;
        internal Color InputBack;
    }

    /// <summary>
    /// Controls that paint themselves - state chips, backup cards, the primary action button - and
    /// which <see cref="Theme.Apply"/> therefore steps over.
    /// </summary>
    /// <remarks>
    /// A marker TYPE rather than a registry of instances: chips and cards are rebuilt on every
    /// render, so a HashSet of opted-out controls would grow without bound and hold disposed
    /// controls alive. The walker still recurses into their children.
    /// </remarks>
    internal sealed class AccentLabel : Label { }

    /// <inheritdoc cref="AccentLabel"/>
    internal sealed class AccentButton : Button { }

    /// <inheritdoc cref="AccentLabel"/>
    internal sealed class AccentPanel : TableLayoutPanel { }

    /// <summary>
    /// Light and dark colour tokens plus the control-tree walker that applies them.
    /// </summary>
    /// <remarks>
    /// Hand-rolled on purpose. <c>Application.SetColorMode</c> is .NET 9+ and experimental
    /// (WFO5001); it is not available on net8.0-windows. .NET 8 reaches end of life in November
    /// 2026, and a later retarget would let SetColorMode replace most of this - so this class is
    /// deliberately thin and disposable rather than a framework to grow.
    ///
    /// Light is today's values moved, not chosen, so switching to it is a no-op against the
    /// pre-Phase-4 look. Skipped is amber in BOTH palettes and never green: the styling has to keep
    /// the distinction the engine's three-state result fought for.
    ///
    /// MessageBoxes and common dialogs stay light no matter what. That is disclosed, not chased -
    /// owner-drawing our way out of it is a budget this phase does not have, and Path D already cut
    /// the remaining MessageBoxes down to the consent-class prompts.
    /// </remarks>
    internal static class Theme
    {
        internal static readonly Palette Light = new Palette
        {
            Surface = Color.FromArgb(243, 243, 243),
            RailSurface = Color.FromArgb(245, 241, 249),
            CardSurface = Color.FromArgb(250, 250, 250),
            TextPrimary = Color.Black,
            TextMuted = Color.DimGray,
            Border = Color.FromArgb(220, 220, 220),
            Danger = Color.FromArgb(168, 34, 34),
            Caution = Color.FromArgb(150, 92, 0),
            ChipSucceededBack = Color.FromArgb(39, 124, 74),
            ChipSucceededFore = Color.White,
            ChipSkippedBack = Color.FromArgb(150, 92, 0),
            ChipSkippedFore = Color.White,
            ChipFailedBack = Color.FromArgb(168, 34, 34),
            ChipFailedFore = Color.White,
            InputBack = Color.FromArgb(250, 250, 250),
        };

        internal static readonly Palette Dark = new Palette
        {
            Surface = Color.FromArgb(32, 32, 32),
            RailSurface = Color.FromArgb(43, 43, 43),
            CardSurface = Color.FromArgb(43, 43, 43),
            TextPrimary = Color.FromArgb(240, 240, 240),
            TextMuted = Color.FromArgb(170, 170, 170),
            Border = Color.FromArgb(65, 65, 65),
            Danger = Color.FromArgb(255, 120, 120),
            Caution = Color.FromArgb(240, 190, 90),
            ChipSucceededBack = Color.FromArgb(38, 104, 66),
            ChipSucceededFore = Color.White,
            ChipSkippedBack = Color.FromArgb(140, 96, 20),
            ChipSkippedFore = Color.White,
            ChipFailedBack = Color.FromArgb(150, 48, 48),
            ChipFailedFore = Color.White,
            InputBack = Color.FromArgb(43, 43, 43),
        };

        internal static Palette Current { get; private set; } = Light;

        internal static bool IsDark { get; private set; }

        /// <summary>Switches the active palette. Callers re-apply afterwards.</summary>
        internal static void Use(bool dark)
        {
            IsDark = dark;
            Current = dark ? Dark : Light;
        }

        /// <summary>
        /// Whether Windows is in dark app mode. Any failure reads as light.
        /// </summary>
        /// <remarks>
        /// AppsUseLightTheme (0 = dark) rather than SystemUsesLightTheme: the former is the app
        /// setting, the latter is the taskbar/Start setting, and they are set independently.
        /// </remarks>
        internal static bool IsDarkOs()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null)
                        return false;

                    return key.GetValue("AppsUseLightTheme") is int light && light == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Paints a control tree with the current palette, by control kind.
        /// </summary>
        /// <remarks>
        /// Called by MainForm for the whole shell and by the two dialogs on themselves, since they
        /// are constructed after startup and are not in the shell's tree.
        /// </remarks>
        internal static void Apply(Control root)
        {
            if (root == null)
                return;

            // Paints itself: step over it but still theme what it contains.
            if (root is AccentLabel || root is AccentButton || root is AccentPanel)
            {
                foreach (Control accentChild in root.Controls)
                    Apply(accentChild);

                return;
            }

            Palette p = Current;

            switch (root)
            {
                case TextBox textBox:
                    textBox.BackColor = textBox.ReadOnly ? p.Surface : p.InputBack;
                    textBox.ForeColor = p.TextPrimary;
                    break;

                case RichTextBox richTextBox:
                    richTextBox.BackColor = p.Surface;
                    richTextBox.ForeColor = p.TextMuted;
                    break;

                case TreeView tree:
                    tree.BackColor = p.Surface;
                    tree.ForeColor = p.TextPrimary;
                    ApplyExplorerTheme(tree);
                    break;

                case ListBox list:
                    list.BackColor = p.InputBack;
                    list.ForeColor = p.TextPrimary;
                    ApplyExplorerTheme(list);
                    break;

                case ComboBox combo:
                    combo.BackColor = p.InputBack;
                    combo.ForeColor = p.TextPrimary;
                    break;

                case LinkLabel link:
                    link.BackColor = Color.Transparent;
                    link.LinkColor = p.TextPrimary;
                    link.ActiveLinkColor = p.TextPrimary;
                    break;

                case Button button:
                    // Chips and the primary action button paint themselves; leave any control that
                    // has opted out of the palette alone rather than flattening it.
                    button.ForeColor = p.TextPrimary;
                    button.BackColor = p.CardSurface;
                    button.FlatAppearance.BorderColor = p.Border;
                    break;

                case CheckBox check:
                    check.BackColor = Color.Transparent;
                    check.ForeColor = p.TextPrimary;
                    break;

                case RadioButton radio:
                    radio.BackColor = Color.Transparent;
                    radio.ForeColor = p.TextPrimary;
                    break;

                case Label label:
                    label.BackColor = Color.Transparent;
                    // Muted and caution labels keep their own semantic colour.
                    if (label.ForeColor != Light.TextMuted && label.ForeColor != Dark.TextMuted
                        && label.ForeColor != Light.Caution && label.ForeColor != Dark.Caution
                        && label.ForeColor != Light.Danger && label.ForeColor != Dark.Danger)
                    {
                        label.ForeColor = p.TextPrimary;
                    }
                    break;

                default:
                    root.BackColor = p.Surface;
                    root.ForeColor = p.TextPrimary;
                    break;
            }

            foreach (Control child in root.Controls)
                Apply(child);
        }

        // ---------------------------------------------------------------------------------------------
        //  Cosmetic P/Invokes. BOTH are wrapped: these are decoration in an elevated process and must
        //  never be able to throw. If a future Windows build breaks them, deleting the calls is safe -
        //  nothing depends on them.
        // ---------------------------------------------------------------------------------------------

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        /// <summary>Paints the title bar to match the theme.</summary>
        internal static void ApplyTitleBar(Form form)
        {
            if (form == null || !form.IsHandleCreated)
                return;

            try
            {
                int on = IsDark ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }

        /// <summary>
        /// Dark scrollbars on a TreeView/ListBox. Undocumented but stable, and used by essentially
        /// every dark WinForms app.
        /// </summary>
        private static void ApplyExplorerTheme(Control control)
        {
            if (control == null || !control.IsHandleCreated)
                return;

            try
            {
                SetWindowTheme(control.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }
    }
}
