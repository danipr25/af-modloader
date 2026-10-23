using System;
using System.Drawing;
using System.IO;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AFModLoader.Installer
{
    /// <summary>Reads the Windows light/dark setting. The registry is only read here, never written.</summary>
    public static class ThemeSetting
    {
        private const string KeyName = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        /// <summary>The value of AppsUseLightTheme: 0 means dark. A missing or unreadable value means light.</summary>
        public static bool IsLight(object value)
        {
            return !(value is int) || (int)value != 0;
        }

        /// <summary>True when apps should be dark right now.</summary>
        public static bool AppsAreDark()
        {
            try
            {
                return !IsLight(Registry.GetValue(KeyName, "AppsUseLightTheme", null));
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>The colours of one look of the window: light, dark, or the system colours for high contrast.</summary>
    internal sealed class Theme
    {
        public bool HighContrast { get; private set; }

        public bool Dark { get; private set; }

        public Color Window { get; private set; }

        public Color Header { get; private set; }

        public Color Footer { get; private set; }

        public Color Card { get; private set; }

        public Color Text { get; private set; }

        public Color Secondary { get; private set; }

        /// <summary>Text of things that cannot be used now. Still readable (4.5:1 or more on every surface it is on).</summary>
        public Color Disabled { get; private set; }

        /// <summary>Quiet lines: the edges of the header, footer and status card.</summary>
        public Color Border { get; private set; }

        /// <summary>The edge of text boxes, check boxes and outlined buttons.</summary>
        public Color ControlBorder { get; private set; }

        public Color Accent { get; private set; }

        /// <summary>Text on a button filled with the accent colour.</summary>
        public Color AccentText { get; private set; }

        public Color AccentHover { get; private set; }

        public Color AccentDown { get; private set; }

        public Color OutlineHover { get; private set; }

        public Color OutlineDown { get; private set; }

        /// <summary>Behind the logo: the logo's ink is dark, so on a dark header it sits on a paper-coloured tile.</summary>
        public Color LogoTile { get; private set; }

        public Color Warning { get; private set; }

        public Color Error { get; private set; }

        /// <summary>The look Windows asks for right now.</summary>
        public static Theme Detect()
        {
            if (SystemInformation.HighContrast)
            {
                return ForHighContrast();
            }

            return ThemeSetting.AppsAreDark() ? ForDark() : ForLight();
        }

        // The colours come from the calculator's brand (paper, ink and the red accent). Contrast of every text and
        // background pair was checked against WCAG AA (4.5:1 for text).
        private static Theme ForLight()
        {
            return new Theme
            {
                Window = Hex("#ffffff"),
                Header = Hex("#f2e9d5"),
                Footer = Hex("#fbfaf7"),
                Card = Hex("#faf6ec"),
                Text = Hex("#29271f"),
                Secondary = Hex("#6b6656"),
                Disabled = Hex("#767060"),
                Border = Hex("#cfc8b8"),
                ControlBorder = Hex("#8c8574"),
                Accent = Hex("#a33d25"),
                AccentText = Hex("#ffffff"),
                AccentHover = Hex("#8c321d"),
                AccentDown = Hex("#752a18"),
                OutlineHover = Hex("#f2e9d5"),
                OutlineDown = Hex("#e2d8c2"),
                LogoTile = Hex("#f2e9d5"),
                Warning = Hex("#8a5a00"),
                Error = Hex("#b42318"),
            };
        }

        private static Theme ForDark()
        {
            return new Theme
            {
                Dark = true,
                Window = Hex("#161412"),
                Header = Hex("#221f1a"),
                Footer = Hex("#1b1814"),
                Card = Hex("#1e1b17"),
                Text = Hex("#f1e8d8"),
                Secondary = Hex("#b5ab98"),
                Disabled = Hex("#908777"),
                Border = Hex("#3a362e"),
                ControlBorder = Hex("#6a6456"),
                Accent = Hex("#efa17d"),
                AccentText = Hex("#161412"),
                AccentHover = Hex("#f4b494"),
                AccentDown = Hex("#e8906a"),
                OutlineHover = Hex("#2a2620"),
                OutlineDown = Hex("#3a362e"),
                LogoTile = Hex("#f2e9d5"),
                Warning = Hex("#f0b95a"),
                Error = Hex("#ff8a80"),
            };
        }

        private static Theme ForHighContrast()
        {
            return new Theme
            {
                HighContrast = true,
                Window = SystemColors.Window,
                Header = SystemColors.Window,
                Footer = SystemColors.Control,
                Card = SystemColors.Window,
                Text = SystemColors.WindowText,
                Secondary = SystemColors.WindowText,
                Disabled = SystemColors.GrayText,
                Border = SystemColors.WindowText,
                ControlBorder = SystemColors.WindowText,
                Accent = SystemColors.HotTrack,
                AccentText = SystemColors.Window,
                AccentHover = SystemColors.HotTrack,
                AccentDown = SystemColors.HotTrack,
                OutlineHover = SystemColors.Window,
                OutlineDown = SystemColors.Window,
                LogoTile = SystemColors.Window,
                Warning = SystemColors.WindowText,
                Error = SystemColors.WindowText,
            };
        }

        private static Color Hex(string value)
        {
            return ColorTranslator.FromHtml(value);
        }
    }

    /// <summary>The fonts: Segoe UI when Windows has it, else the system message font.</summary>
    internal static class Fonts
    {
        public static readonly Font Body = Make("Segoe UI", 9.75f, FontStyle.Regular);
        public static readonly Font Small = Make("Segoe UI", 8.25f, FontStyle.Regular);
        public static readonly Font Semibold = MakeSemibold(9.75f);
        public static readonly Font Title = MakeSemibold(15f);
        public static readonly Font Mono = Make("Consolas", 8.25f, FontStyle.Regular);

        /// <summary>The icon font (Windows 11, else Windows 10), or null when neither is installed.</summary>
        public static readonly Font Glyph = MakeIcons(14f);
        public static readonly Font GlyphSmall = MakeIcons(10f);

        private static Font Make(string name, float size, FontStyle style)
        {
            var font = new Font(name, size, style);
            if (font.Name != name)
            {
                // Windows replaced it with another font: use the system one, in the same size and style.
                font.Dispose();
                font = new Font(SystemFonts.MessageBoxFont.FontFamily, size, style);
            }

            return font;
        }

        /// <summary>The Semibold family itself (no synthetic bold); plain bold Segoe UI if Windows lacks it.</summary>
        private static Font MakeSemibold(float size)
        {
            var font = new Font("Segoe UI Semibold", size, FontStyle.Regular);
            if (font.Name == "Segoe UI Semibold")
            {
                return font;
            }

            font.Dispose();
            return Make("Segoe UI", size, FontStyle.Bold);
        }

        private static Font MakeIcons(float size)
        {
            foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                var font = new Font(name, size);
                if (font.Name == name)
                {
                    return font;
                }

                font.Dispose();
            }

            return null;
        }
    }
}
