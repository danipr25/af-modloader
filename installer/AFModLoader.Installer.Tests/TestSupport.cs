using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AFModLoader.Installer.Core;

namespace AFModLoader.Installer.Tests
{
    /// <summary>A temporary folder that is deleted when the test ends.</summary>
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "afml-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // A test folder left behind is not worth failing a test for.
            }
        }
    }

    /// <summary>A game folder with only the executable in it, like a fresh Steam install.</summary>
    internal sealed class FakeGame : IDisposable
    {
        private readonly TempDir temp = new TempDir();

        public FakeGame()
        {
            Root = System.IO.Path.Combine(temp.Path, "Alchemy Factory");
            Directory.CreateDirectory(Win64);
            File.WriteAllText(System.IO.Path.Combine(Win64, GameFolder.ExeName), "exe");
        }

        public string Root { get; }

        public string Win64
        {
            get { return System.IO.Path.Combine(Root, "AlchemyFactory", "Binaries", "Win64"); }
        }

        public string Settings
        {
            get { return System.IO.Path.Combine(Win64, "ue4ss", "UE4SS-settings.ini"); }
        }

        public string TempPath
        {
            get { return temp.Path; }
        }

        /// <summary>Every file under the root, as relative paths with "/".</summary>
        public static List<string> Files(string root)
        {
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(root.Length).TrimStart('\\').Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }

        public List<string> Files()
        {
            return Files(Root);
        }

        public void Dispose()
        {
            temp.Dispose();
        }
    }

    internal sealed class FakeDownloader : IDownloader
    {
        private readonly byte[] bytes;

        public FakeDownloader(byte[] bytes)
        {
            this.bytes = bytes;
        }

        public int Calls { get; private set; }

        public void Download(
            string url,
            string destination,
            long expectedSize,
            Action<long, long> progress,
            Action<string> log,
            System.Threading.CancellationToken cancel)
        {
            Calls++;
            File.WriteAllBytes(destination, bytes);
            progress(bytes.Length, bytes.Length);
        }
    }

    internal static class Fakes
    {
        public const string Ini =
            "; UE4SS settings\r\n[Overrides]\r\nModsFolderPath =\r\n\r\n[Debug]\r\n; console\r\nConsoleEnabled = 0\r\n"
            + "GuiConsoleEnabled = 0\r\nGuiConsoleVisible = 0\r\nGraphicsAPI = opengl\r\nRenderMode = ExternalThread\r\n\r\n[Other]\r\nX = 1\r\n";

        public static byte[] Zip(IDictionary<string, string> entries)
        {
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    foreach (KeyValuePair<string, string> entry in entries)
                    {
                        ZipArchiveEntry e = zip.CreateEntry(entry.Key);
                        using (Stream stream = e.Open())
                        {
                            byte[] data = Encoding.UTF8.GetBytes(entry.Value);
                            stream.Write(data, 0, data.Length);
                        }
                    }
                }

                return memory.ToArray();
            }
        }

        /// <summary>A small zip shaped like the real UE4SS one, with files the installer must skip.</summary>
        public static byte[] Ue4ssZip()
        {
            return Zip(new Dictionary<string, string>
            {
                { "dwmapi.dll", "proxy" },
                { "ue4ss/", string.Empty },
                { "ue4ss/UE4SS.dll", "ue4ss" },
                { "ue4ss/LICENSE", "license" },
                { "ue4ss/UE4SS-settings.ini", Ini },
                { "ue4ss/Mods/shared/UEHelpers/UEHelpers.lua", "helpers" },
                { "ue4ss/Mods/mods.txt", "sample" },
                { "ue4ss/Mods/mods.json", "[]" },
                { "ue4ss/Mods/BPML/Scripts/main.lua", "sample" },
                { "ue4ss/UE4SS_SDK_Backends/UE4SS.json", "{}" },
            });
        }

        public static Ue4ssPin PinFor(byte[] zip)
        {
            using (SHA256 sha = SHA256.Create())
            {
                string hash = string.Concat(sha.ComputeHash(zip).Select(b => b.ToString("x2")));
                return new Ue4ssPin("vTest", "https://example.invalid/ue4ss.zip", zip.Length, hash);
            }
        }

        public static List<LoaderFile> Loader(params string[] names)
        {
            var files = new List<LoaderFile>();
            foreach (string name in names)
            {
                files.Add(new LoaderFile(name, Encoding.UTF8.GetBytes(name.EndsWith("enabled.txt", StringComparison.Ordinal) ? string.Empty : "lua " + name)));
            }

            return files;
        }

        public static ModLoaderInstaller Installer(byte[] zip, List<string> log, params string[] loader)
        {
            return new ModLoaderInstaller(Options(zip, loader), log.Add, null);
        }

        public static InstallerOptions Options(byte[] zip, params string[] loader)
        {
            List<LoaderFile> files = Loader(loader.Length > 0 ? loader : new[] { "Scripts/main.lua", "enabled.txt" });
            return new InstallerOptions
            {
                Pin = PinFor(zip),
                Downloader = new FakeDownloader(zip),
                Loader = () => files,
                IsGameRunning = () => false,
                Version = "9.9.9",
            };
        }
    }
}
