using System;
using System.Diagnostics;
using System.IO;

namespace AFModLoader.Installer.Core
{
    /// <summary>The game folder: checks on it and the paths inside it.</summary>
    public sealed class GameFolder
    {
        public const string ExeName = "AlchemyFactory-Win64-Shipping.exe";

        private static readonly string[] ProcessNames = { "AlchemyFactory-Win64-Shipping", "AlchemyFactory" };

        private GameFolder(string root)
        {
            Root = root;
            Win64 = Path.Combine(root, "AlchemyFactory", "Binaries", "Win64");
        }

        /// <summary>The folder Steam installs the game in.</summary>
        public string Root { get; }

        public string Win64 { get; }

        public string ModsDir
        {
            get { return Path.Combine(Root, "mods"); }
        }

        public string Settings
        {
            get { return Path.Combine(Win64, "ue4ss", "UE4SS-settings.ini"); }
        }

        /// <summary>Checks that this is the game's folder. Throws a message the player can act on.</summary>
        public static GameFolder Open(string root, bool requireAscii, bool requireWritable)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new InstallerException("Choose the Alchemy Factory folder first.");
            }

            string full;
            try
            {
                full = Path.GetFullPath(root.Trim()).TrimEnd('\\');
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new InstallerException("That is not a valid folder path.", ex);
            }

            var folder = new GameFolder(full);
            if (!File.Exists(Path.Combine(folder.Win64, ExeName)))
            {
                throw new InstallerException(
                    "This is not the Alchemy Factory folder: " + ExeName + " was not found in AlchemyFactory\\Binaries\\Win64. "
                    + "Choose the folder Steam installed the game in (usually ...\\steamapps\\common\\Alchemy Factory).");
            }

            if (requireAscii && !IsAscii(full))
            {
                throw new InstallerException(
                    "The game folder path has letters like n with a tilde or accents. Mods cannot read their files from such a "
                    + "folder. Move the Steam library to a folder with plain English letters, digits and symbols only.");
            }

            if (requireWritable)
            {
                folder.EnsureWritable();
            }

            return folder;
        }

        public static bool IsAscii(string text)
        {
            foreach (char c in text)
            {
                if (c > 127)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when the game process is running, by name.</summary>
        public static bool IsGameProcessRunning()
        {
            foreach (string name in ProcessNames)
            {
                Process[] found = Process.GetProcessesByName(name);
                bool any = found.Length > 0;
                foreach (Process process in found)
                {
                    process.Dispose();
                }

                if (any)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when dwmapi.dll is open somewhere else: a loaded DLL cannot be opened for writing.</summary>
        public bool IsDllLocked()
        {
            string dll = Path.Combine(Win64, PathRules.Dwmapi);
            if (!File.Exists(dll))
            {
                return false;
            }

            try
            {
                using (new FileStream(dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>True when UE4SS files are there that this installer did not put there (including the old flat layout).</summary>
        public bool HasForeignUe4ss(out string found)
        {
            found = null;
            foreach (string relative in new[] { PathRules.Dwmapi, "ue4ss/UE4SS.dll", "UE4SS.dll", "UE4SS-settings.ini" })
            {
                string path = Path.Combine(Win64, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                {
                    found = path;
                    return true;
                }
            }

            return false;
        }

        public InstallManifest ReadManifest()
        {
            string path = InstallManifest.PathIn(Win64);
            return File.Exists(path) ? InstallManifest.Parse(File.ReadAllText(path)) : null;
        }

        public bool ManifestFileExists()
        {
            return File.Exists(InstallManifest.PathIn(Win64));
        }

        /// <summary>The installer never follows links: these folders must be real folders if they exist.</summary>
        public void EnsureNoLinks()
        {
            foreach (string relative in new[] { "ue4ss", "ue4ss/Mods", "ue4ss/Mods/shared", "ue4ss/Mods/AFModLoader" })
            {
                string path = PathRules.Resolve(Win64, relative);
                if (Directory.Exists(path) && IsReparsePoint(path))
                {
                    throw new InstallerException(
                        path + " is a link (a junction or symbolic link), and the installer never follows links. "
                        + "Replace it with a normal folder, or delete the link, then try again.");
                }
            }
        }

        /// <summary>Throws when a folder between Win64 and the file is a link.</summary>
        public static void EnsureNoLinksAbove(string win64, string file)
        {
            string stop = Path.GetFullPath(win64).TrimEnd('\\');
            string dir = Path.GetDirectoryName(file);
            while (dir != null && dir.Length > stop.Length)
            {
                if (Directory.Exists(dir) && IsReparsePoint(dir))
                {
                    throw new InstallerException(
                        dir + " is a link (a junction or symbolic link), and the installer never follows links. "
                        + "Replace it with a normal folder, or delete the link, then try again.");
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        public static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private void EnsureWritable()
        {
            string probe = Path.Combine(Win64, "af-modloader-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(probe, new byte[0]);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new InstallerException(
                    "The installer cannot write to " + Win64 + " (" + ex.Message + "). Check that the folder is not "
                    + "read-only and that no antivirus blocks it.",
                    ex);
            }
            finally
            {
                try
                {
                    File.Delete(probe);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Nothing more to do: the probe is empty and named so it can be recognized.
                }
            }
        }
    }
}
