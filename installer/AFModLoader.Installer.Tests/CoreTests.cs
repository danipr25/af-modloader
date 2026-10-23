using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AFModLoader.Installer.Core;
using Xunit;

namespace AFModLoader.Installer.Tests
{
    public class SteamTests
    {
        public static IEnumerable<object[]> LibraryFiles()
        {
            // New format (path keys) and old format (numbered entries). Backslashes are doubled in the file.
            yield return new object[]
            {
                "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"label\"\t\t\"\"\n"
                + "\t\t\"apps\"\n\t\t{\n\t\t\t\"3669570\"\t\t\"123456\"\n\t\t}\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\Games\\\\SteamLibrary\"\n\t}\n}\n",
                new[] { @"C:\Program Files (x86)\Steam", @"D:\Games\SteamLibrary" },
            };
            yield return new object[]
            {
                "\"LibraryFolders\"\r\n{\r\n\t\"TimeNextStatsReport\"\t\t\"1234\"\r\n\t\"ContentStatsID\"\t\t\"-99\"\r\n"
                + "\t\"1\"\t\t\"D:\\\\Lib\"\r\n\t\"2\"\t\t\"E:\\\\Other Lib\"\r\n}\r\n",
                new[] { @"D:\Lib", @"E:\Other Lib" },
            };
        }

        [Theory]
        [MemberData(nameof(LibraryFiles))]
        public void LibraryPaths_ReadsBothFormats(string vdf, string[] expected)
        {
            Assert.Equal(expected, Vdf.LibraryPaths(vdf));
        }

        [Fact]
        public void FindGame_UsesTheAppManifestInstallDir()
        {
            using (var temp = new TempDir())
            {
                string steam = Path.Combine(temp.Path, "Steam");
                string library = Path.Combine(temp.Path, "Lib");
                Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
                Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "Alchemy Factory"));
                File.WriteAllText(
                    Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
                    "\"libraryfolders\"\n{\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + library.Replace("\\", "\\\\") + "\"\n\t}\n}\n");
                File.WriteAllText(
                    Path.Combine(library, "steamapps", "appmanifest_3669570.acf"),
                    "\"AppState\"\n{\n\t\"appid\"\t\t\"3669570\"\n\t\"installdir\"\t\t\"Alchemy Factory\"\n}\n");

                Assert.Equal(
                    Path.Combine(library, "steamapps", "common", "Alchemy Factory"),
                    SteamLocator.FindGame(new[] { steam }));
            }
        }
    }

    public class ConsoleSettingTests
    {
        [Fact]
        public void Apply_TurnsOnAndOffKeepingEverythingElse()
        {
            string on = ConsoleSetting.Apply(Fakes.Ini, true);
            Assert.True(ConsoleSetting.IsEnabled(on));
            Assert.Equal(
                Fakes.Ini.Replace("ConsoleEnabled = 0", "ConsoleEnabled = 1")
                    .Replace("GuiConsoleVisible = 0", "GuiConsoleVisible = 1")
                    .Replace("GuiConsoleEnabled = 0", "GuiConsoleEnabled = 1")
                    .Replace("GraphicsAPI = opengl", "GraphicsAPI = dx11"),
                on);

            // Off again: the three switches go back, GraphicsAPI stays as it is.
            string off = ConsoleSetting.Apply(on, false);
            Assert.False(ConsoleSetting.IsEnabled(off));
            Assert.Equal(Fakes.Ini.Replace("opengl", "dx11"), off);
        }

        [Fact]
        public void Apply_AddsMissingKeysInsideDebugAndKeepsLineEndings()
        {
            string ini = "[Overrides]\nA = 1\n[Debug]\nConsoleEnabled = 0\n[Other]\nGraphicsAPI = opengl\n";
            string result = ConsoleSetting.Apply(ini, true);
            Assert.Equal(
                "[Overrides]\nA = 1\n[Debug]\nConsoleEnabled = 1\nGuiConsoleEnabled = 1\nGuiConsoleVisible = 1\nGraphicsAPI = dx11\n"
                + "[Other]\nGraphicsAPI = opengl\n",
                result);
            Assert.DoesNotContain("\r", result);
        }
    }

    public class InstallTests
    {
        [Fact]
        public void InstallThenRemove_LeavesOnlyTheGameAndTheModsFolder()
        {
            using (var game = new FakeGame())
            {
                byte[] zip = Fakes.Ue4ssZip();
                var log = new List<string>();
                var installer = Fakes.Installer(zip, log, "Scripts/main.lua", "enabled.txt");

                installer.Install(game.Root, false);

                List<string> files = game.Files();
                Assert.Contains("AlchemyFactory/Binaries/Win64/dwmapi.dll", files);
                Assert.Contains("AlchemyFactory/Binaries/Win64/ue4ss/UE4SS.dll", files);
                Assert.Contains("AlchemyFactory/Binaries/Win64/ue4ss/Mods/shared/UEHelpers/UEHelpers.lua", files);
                Assert.Contains("AlchemyFactory/Binaries/Win64/ue4ss/Mods/AFModLoader/enabled.txt", files);
                Assert.Contains("AlchemyFactory/Binaries/Win64/ue4ss/af-modloader-install.txt", files);
                Assert.True(Directory.Exists(Path.Combine(game.Root, "mods", ".data")));
                Assert.DoesNotContain(files, f => f.Contains("mods.txt") || f.Contains("BPML") || f.Contains("SDK_Backends"));
                Assert.Equal(0, new FileInfo(Path.Combine(game.Win64, "ue4ss", "Mods", "AFModLoader", "enabled.txt")).Length);
                Assert.Equal(StatusKind.Installed, installer.Inspect(game.Root).Kind);

                // UE4SS writes some files of its own while the game runs.
                File.WriteAllText(Path.Combine(game.Win64, "ue4ss", "UE4SS.log"), "log");
                File.WriteAllText(Path.Combine(game.Win64, "ue4ss", "crash_2026.dmp"), "dump");
                File.WriteAllText(Path.Combine(game.Root, "mods", ".data", "mine.json"), "{}");

                installer.Remove(game.Root);

                Assert.Equal(
                    new[]
                    {
                        "AlchemyFactory/Binaries/Win64/" + GameFolder.ExeName,
                        "mods/.data/mine.json",
                    },
                    game.Files());
                Assert.Equal(StatusKind.NotInstalled, installer.Inspect(game.Root).Kind);
            }
        }

        [Fact]
        public void Update_KeepsAnEditedIniAndReplacesLoaderFiles()
        {
            using (var game = new FakeGame())
            {
                byte[] zip = Fakes.Ue4ssZip();
                var log = new List<string>();
                Fakes.Installer(zip, log, "Scripts/main.lua", "Scripts/old.lua", "enabled.txt").Install(game.Root, false);
                string edited = Fakes.Ini.Replace("ModsFolderPath =", "ModsFolderPath = mine");
                File.WriteAllText(game.Settings, edited);

                Fakes.Installer(zip, log, "Scripts/main.lua", "Scripts/new.lua", "enabled.txt").Install(game.Root, false);

                Assert.Equal(edited, File.ReadAllText(game.Settings));
                string loader = Path.Combine(game.Win64, "ue4ss", "Mods", "AFModLoader", "Scripts");
                Assert.False(File.Exists(Path.Combine(loader, "old.lua")));
                Assert.True(File.Exists(Path.Combine(loader, "new.lua")));
                Assert.Contains("ue4ss/UE4SS-settings.ini", game.ReadManifestFiles());
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string newName, string existing, IntPtr reserved);

        [Fact]
        public void Update_NeverWritesThroughAHardLinkToAFileOutsideTheGame()
        {
            using (var game = new FakeGame())
            {
                var installer = Fakes.Installer(Fakes.Ue4ssZip(), new List<string>());
                installer.Install(game.Root, false);
                string outside = Path.Combine(game.TempPath, "outside.txt");
                File.WriteAllText(outside, "outside");
                string license = Path.Combine(game.Win64, "ue4ss", "LICENSE");
                File.Delete(license);
                Assert.True(CreateHardLink(license, outside, IntPtr.Zero));

                installer.Install(game.Root, false);

                Assert.Equal("outside", File.ReadAllText(outside));
                Assert.Equal("license", File.ReadAllText(license));
            }
        }

        [Fact]
        public void Install_RefusesFilesItDidNotInstall()
        {
            using (var game = new FakeGame())
            {
                var installer = Fakes.Installer(Fakes.Ue4ssZip(), new List<string>());
                installer.Install(game.Root, false);
                string manifest = Path.Combine(game.Win64, "ue4ss", "af-modloader-install.txt");
                File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("ue4ss/LICENSE\n", string.Empty));

                Assert.Throws<InstallerException>(() => installer.Install(game.Root, false));
            }
        }

        [Fact]
        public void Install_AppliesTheConsoleChoiceOnUpdate()
        {
            using (var game = new FakeGame())
            {
                byte[] zip = Fakes.Ue4ssZip();
                var installer = Fakes.Installer(zip, new List<string>());

                installer.Install(game.Root, true);
                Assert.True(installer.Inspect(game.Root).ConsoleEnabled);

                installer.Install(game.Root, false);
                Assert.False(installer.Inspect(game.Root).ConsoleEnabled);
            }
        }

        [Fact]
        public void Install_WithABadHashOrABadZip_LeavesTheGameFolderUntouched()
        {
            using (var game = new FakeGame())
            {
                byte[] zip = Fakes.Ue4ssZip();
                List<string> before = game.Files();

                InstallerOptions wrongHash = Fakes.Options(zip);
                wrongHash.Pin = new Ue4ssPin("vTest", "https://example.invalid/x.zip", zip.Length, new string('0', 64));
                Assert.Throws<InstallerException>(() => new ModLoaderInstaller(wrongHash, null, null).Install(game.Root, false));

                byte[] slip = Fakes.Zip(new Dictionary<string, string>
                {
                    { "dwmapi.dll", "x" },
                    { "ue4ss/UE4SS.dll", "x" },
                    { "ue4ss/Mods/shared/../../../evil.dll", "x" },
                });
                Assert.Throws<InstallerException>(() => new ModLoaderInstaller(Fakes.Options(slip), null, null).Install(game.Root, false));

                Assert.Equal(before, game.Files());
                Assert.False(Directory.Exists(Path.Combine(game.Root, "mods")));
            }
        }

        [Fact]
        public void Install_Interrupted_CanBeFinishedOrRemoved()
        {
            byte[] zip = Fakes.Ue4ssZip();
            foreach (bool finishFirst in new[] { true, false })
            {
                using (var game = new FakeGame())
                {
                    InstallerOptions failing = Fakes.Options(zip);
                    failing.BeforeWriteFile = (index, path) =>
                    {
                        if (index == 3)
                        {
                            throw new IOException("disk full");
                        }
                    };
                    Assert.Throws<InstallerException>(() => new ModLoaderInstaller(failing, null, null).Install(game.Root, false));
                    Assert.True(File.Exists(Path.Combine(game.Win64, "ue4ss", "af-modloader-install.txt")));

                    var installer = Fakes.Installer(zip, new List<string>());
                    if (finishFirst)
                    {
                        installer.Install(game.Root, false);
                        Assert.Equal(StatusKind.Installed, installer.Inspect(game.Root).Kind);
                    }

                    installer.Remove(game.Root);
                    Assert.Equal(new[] { "AlchemyFactory/Binaries/Win64/" + GameFolder.ExeName }, game.Files().Where(f => !f.StartsWith("mods/", StringComparison.Ordinal)).ToArray());
                }
            }
        }

        [Fact]
        public void Remove_WorksAfterTheGameFolderWasMoved()
        {
            using (var game = new FakeGame())
            {
                var installer = Fakes.Installer(Fakes.Ue4ssZip(), new List<string>());
                installer.Install(game.Root, false);

                string moved = Path.Combine(game.TempPath, "Moved", "Alchemy Factory");
                CopyDirectory(game.Root, moved);
                installer.Remove(moved);

                Assert.Equal(new[] { "AlchemyFactory/Binaries/Win64/" + GameFolder.ExeName }, FakeGame.Files(moved));
            }
        }

        [Fact]
        public void Remove_IgnoresManifestEntriesThatAreNotOurs()
        {
            using (var game = new FakeGame())
            {
                var installer = Fakes.Installer(Fakes.Ue4ssZip(), new List<string>());
                installer.Install(game.Root, false);
                string outside = Path.Combine(game.Root, "AlchemyFactory", "Binaries", "x");
                File.WriteAllText(outside, "keep");
                string absolute = Path.Combine(game.TempPath, "absolute.txt");
                File.WriteAllText(absolute, "keep");
                string manifest = Path.Combine(game.Win64, "ue4ss", "af-modloader-install.txt");
                File.AppendAllText(manifest, "../x\n" + absolute.Replace('\\', '/') + "\n" + GameFolder.ExeName + "\nue4ss/Mods/shared/../../../x\n");

                installer.Remove(game.Root);

                Assert.True(File.Exists(outside));
                Assert.True(File.Exists(absolute));
                Assert.True(File.Exists(Path.Combine(game.Win64, GameFolder.ExeName)));
                Assert.False(File.Exists(Path.Combine(game.Win64, "dwmapi.dll")));
                Assert.False(Directory.Exists(Path.Combine(game.Win64, "ue4ss")));
            }
        }

        [Fact]
        public void ForeignUe4ss_IsNeverChanged()
        {
            using (var game = new FakeGame())
            {
                string dwmapi = Path.Combine(game.Win64, "dwmapi.dll");
                File.WriteAllText(dwmapi, "someone else's");
                var installer = Fakes.Installer(Fakes.Ue4ssZip(), new List<string>());

                Assert.Equal(StatusKind.Foreign, installer.Inspect(game.Root).Kind);
                Assert.Throws<InstallerException>(() => installer.Install(game.Root, false));
                Assert.Throws<InstallerException>(() => installer.Remove(game.Root));
                Assert.Equal("someone else's", File.ReadAllText(dwmapi));
            }
        }

        [Fact]
        public void LoaderEmbeddedInTheExe_MatchesTheRepositoryFolder()
        {
            string loader = null;
            for (string dir = AppDomain.CurrentDomain.BaseDirectory; dir != null && loader == null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "loader", "AFModLoader");
                loader = Directory.Exists(candidate) ? candidate : null;
            }

            Assert.NotNull(loader);
            Dictionary<string, byte[]> expected = FakeGame.Files(loader).ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(loader, f)));
            Dictionary<string, byte[]> embedded = LoaderFiles.Embedded().ToDictionary(f => f.Path, f => f.Content);

            Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), embedded.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (KeyValuePair<string, byte[]> file in expected)
            {
                Assert.Equal(file.Value, embedded[file.Key]);
            }

            Assert.Contains("enabled.txt", embedded.Keys);
            Assert.Empty(embedded["enabled.txt"]);
        }

        private static void CopyDirectory(string from, string to)
        {
            foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(to, dir.Substring(from.Length).TrimStart('\\')));
            }

            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(to, file.Substring(from.Length).TrimStart('\\')));
            }
        }
    }

    internal static class FakeGameExtensions
    {
        public static List<string> ReadManifestFiles(this FakeGame game)
        {
            string path = Path.Combine(game.Win64, "ue4ss", "af-modloader-install.txt");
            return InstallManifest.Parse(File.ReadAllText(path, Encoding.UTF8)).Files;
        }
    }
}
