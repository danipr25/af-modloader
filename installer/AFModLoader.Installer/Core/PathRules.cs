using System;
using System.IO;

namespace AFModLoader.Installer.Core
{
    /// <summary>
    /// What the installer may create or delete, and how a relative path from a manifest or a zip is turned into a real
    /// path. Every path is relative to the game's Win64 folder and uses "/".
    /// </summary>
    public static class PathRules
    {
        public const string Dwmapi = "dwmapi.dll";
        public const string Ue4ssDll = "ue4ss/UE4SS.dll";
        public const string License = "ue4ss/LICENSE";
        public const string Settings = "ue4ss/UE4SS-settings.ini";
        public const string SharedPrefix = "ue4ss/Mods/shared/";
        public const string LoaderPrefix = "ue4ss/Mods/AFModLoader/";

        /// <summary>True for the only paths the installer may write or delete.</summary>
        public static bool IsAllowed(string relative)
        {
            return Same(relative, Dwmapi)
                || Same(relative, Ue4ssDll)
                || Same(relative, License)
                || Same(relative, Settings)
                || relative.StartsWith(SharedPrefix, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(LoaderPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True for the paths a UE4SS zip may put in the game: the allow-list without the loader folder.</summary>
        public static bool IsAllowedFromZip(string relative)
        {
            return IsAllowed(relative) && !relative.StartsWith(LoaderPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks a relative path (no "..", not rooted, no drive or stream colon, "/" separators) and returns the full
        /// path. Throws when it is unsafe or would leave the Win64 folder.
        /// </summary>
        public static string Resolve(string win64, string relative)
        {
            if (string.IsNullOrEmpty(relative) || relative.IndexOf('\\') >= 0 || relative.IndexOf(':') >= 0
                || relative.StartsWith("/", StringComparison.Ordinal))
            {
                throw new InstallerException("Unsafe path: " + relative);
            }

            foreach (string part in relative.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal)
                    || part.EndsWith(" ", StringComparison.Ordinal))
                {
                    throw new InstallerException("Unsafe path: " + relative);
                }
            }

            string root = Path.GetFullPath(win64).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', '\\')));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InstallerException("Path leaves the game folder: " + relative);
            }

            return full;
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
