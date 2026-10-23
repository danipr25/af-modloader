using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace AFModLoader.Installer.Core
{
    /// <summary>Reads the small part of Steam's .vdf / .acf text format that is needed to find the game.</summary>
    public static class Vdf
    {
        /// <summary>
        /// Splits a line into its first two quoted strings. Value is empty when there is only one string.
        /// Backslash pairs (\\) become a single backslash.
        /// </summary>
        public static bool TryParseLine(string line, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;
            var strings = new List<string>();
            int i = 0;
            while (i < line.Length && strings.Count < 2)
            {
                if (line[i] != '"')
                {
                    i++;
                    continue;
                }

                int end = i + 1;
                while (end < line.Length && line[end] != '"')
                {
                    end += line[end] == '\\' ? 2 : 1;
                }

                if (end >= line.Length)
                {
                    break;
                }

                strings.Add(line.Substring(i + 1, end - i - 1).Replace("\\\\", "\\"));
                i = end + 1;
            }

            if (strings.Count == 0)
            {
                return false;
            }

            key = strings[0];
            value = strings.Count > 1 ? strings[1] : string.Empty;
            return true;
        }

        /// <summary>The library folders in libraryfolders.vdf, in both formats ("path" keys, or numbered entries).</summary>
        public static List<string> LibraryPaths(string text)
        {
            var result = new List<string>();
            foreach (string line in text.Split('\n'))
            {
                string key, value;
                if (!TryParseLine(line, out key, out value) || !LooksLikePath(value))
                {
                    continue;
                }

                if (key.Equals("path", StringComparison.OrdinalIgnoreCase) || IsDigits(key))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        /// <summary>The "installdir" value of an appmanifest_*.acf, or null.</summary>
        public static string InstallDir(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                string key, value;
                if (TryParseLine(line, out key, out value)
                    && key.Equals("installdir", StringComparison.OrdinalIgnoreCase)
                    && value.Length > 0)
                {
                    return value;
                }
            }

            return null;
        }

        private static bool IsDigits(string text)
        {
            if (text.Length == 0)
            {
                return false;
            }

            foreach (char c in text)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LooksLikePath(string value)
        {
            return value.Length >= 3 && (value[1] == ':' || value.StartsWith("\\\\", StringComparison.Ordinal));
        }
    }

    /// <summary>Finds the game through Steam. Only reads the registry and Steam's files.</summary>
    public static class SteamLocator
    {
        public const string AppId = "3669570";

        /// <summary>Steam's folders from the registry: the current user's first, then the machine's.</summary>
        public static List<string> SteamRoots()
        {
            var roots = new List<string>();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    AddRoot(roots, key == null ? null : key.GetValue("SteamPath") as string);
                }
            }
            catch (Exception)
            {
                // No access to the key: same as Steam not being there.
            }

            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (RegistryKey key = hklm.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                {
                    AddRoot(roots, key == null ? null : key.GetValue("InstallPath") as string);
                }
            }
            catch (Exception)
            {
                // Same.
            }

            return roots;
        }

        /// <summary>
        /// Looks for the game in each Steam folder and in every library its libraryfolders.vdf lists. A folder or file
        /// that cannot be read, or has a broken path, is skipped: the player can always choose the folder by hand.
        /// </summary>
        public static string FindGame(IEnumerable<string> steamRoots)
        {
            foreach (string root in steamRoots)
            {
                string game = FindInRoot(root);
                if (game != null)
                {
                    return game;
                }
            }

            return null;
        }

        public static string FindGame()
        {
            try
            {
                return FindGame(SteamRoots());
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Steam stores its path in lower case ("c:/program files (x86)/steam"). Returns the same folder with the
        /// letter case the disk uses, so the window shows it as the player knows it. Falls back to the input.
        /// </summary>
        public static string WithDiskCasing(string path)
        {
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\');
                string root = Path.GetPathRoot(full);
                string result = root.ToUpperInvariant();
                string[] parts = full.Substring(root.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string part in parts)
                {
                    string[] match = Directory.GetDirectories(result, part);
                    result = match.Length == 1 ? match[0] : Path.Combine(result, part);
                }

                return result;
            }
            catch (Exception)
            {
                return path;
            }
        }

        private static string FindInRoot(string root)
        {
            var libraries = new List<string> { root };
            try
            {
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    libraries.AddRange(Vdf.LibraryPaths(File.ReadAllText(vdf)));
                }
            }
            catch (Exception)
            {
                // Use the Steam folder alone.
            }

            foreach (string library in libraries)
            {
                try
                {
                    string acf = Path.Combine(library, "steamapps", "appmanifest_" + AppId + ".acf");
                    string installDir = File.Exists(acf) ? Vdf.InstallDir(File.ReadAllText(acf)) : null;
                    if (installDir == null)
                    {
                        continue;
                    }

                    string game = Path.Combine(library, "steamapps", "common", installDir);
                    if (Directory.Exists(game))
                    {
                        return WithDiskCasing(game);
                    }
                }
                catch (Exception)
                {
                    // Next library.
                }
            }

            return null;
        }

        private static void AddRoot(List<string> roots, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string normal = path.Replace('/', '\\').TrimEnd('\\');
            if (!roots.Exists(r => r.Equals(normal, StringComparison.OrdinalIgnoreCase)))
            {
                roots.Add(normal);
            }
        }
    }
}
