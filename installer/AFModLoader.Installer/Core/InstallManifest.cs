using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AFModLoader.Installer.Core
{
    /// <summary>
    /// ue4ss/af-modloader-install.txt: three header lines (installer version, UE4SS build, UTC date), then one
    /// Win64-relative path per line with "/" separators. It is how Remove knows which files are ours.
    /// </summary>
    public sealed class InstallManifest
    {
        private const string HeaderPrefix = "AF Mod Loader installer ";
        private const string BuildPrefix = "UE4SS ";

        public InstallManifest(string installerVersion, string ue4ssBuild, string dateUtc, IEnumerable<string> files)
        {
            InstallerVersion = installerVersion;
            Ue4ssBuild = ue4ssBuild;
            DateUtc = dateUtc;
            Files = new List<string>(files);
        }

        public string InstallerVersion { get; }

        public string Ue4ssBuild { get; }

        public string DateUtc { get; }

        public List<string> Files { get; }

        public static string PathIn(string win64)
        {
            return Path.Combine(win64, "ue4ss", "af-modloader-install.txt");
        }

        /// <summary>Returns null when the text is not a manifest written by this installer.</summary>
        public static InstallManifest Parse(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 3 || !lines[0].StartsWith(HeaderPrefix, StringComparison.Ordinal)
                || !lines[1].StartsWith(BuildPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            var files = new List<string>();
            for (int i = 3; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length > 0)
                {
                    files.Add(lines[i].Trim());
                }
            }

            return new InstallManifest(
                lines[0].Substring(HeaderPrefix.Length),
                lines[1].Substring(BuildPrefix.Length),
                lines[2],
                files);
        }

        public string ToText()
        {
            var text = new StringBuilder();
            text.Append(HeaderPrefix).Append(InstallerVersion).Append('\n');
            text.Append(BuildPrefix).Append(Ue4ssBuild).Append('\n');
            text.Append(DateUtc).Append('\n');
            foreach (string file in Files)
            {
                text.Append(file).Append('\n');
            }

            return text.ToString();
        }

        public void Save(string win64)
        {
            Directory.CreateDirectory(Path.Combine(win64, "ue4ss"));
            File.WriteAllText(PathIn(win64), ToText(), new UTF8Encoding(false));
        }
    }
}
