using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace AFModLoader.Installer.Core
{
    public sealed class LoaderFile
    {
        public LoaderFile(string path, byte[] content)
        {
            Path = path;
            Content = content;
        }

        /// <summary>Path inside the loader folder, with "/" separators, for example Scripts/main.lua.</summary>
        public string Path { get; }

        public byte[] Content { get; }
    }

    /// <summary>The loader (loader/AFModLoader in the repository), embedded in the exe at build time.</summary>
    public static class LoaderFiles
    {
        private const string Prefix = "loader/";

        public static List<LoaderFile> Embedded()
        {
            Assembly assembly = typeof(LoaderFiles).Assembly;
            var files = new List<LoaderFile>();
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                using (Stream stream = assembly.GetManifestResourceStream(name))
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);

                    // The build may write "\" in some names (AFModLoader/Scripts\main.lua).
                    files.Add(new LoaderFile(name.Substring(Prefix.Length).Replace('\\', '/'), memory.ToArray()));
                }
            }

            files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return files;
        }
    }
}
