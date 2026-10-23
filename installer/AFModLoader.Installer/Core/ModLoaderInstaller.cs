using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AFModLoader.Installer.Core
{
    public enum StatusKind
    {
        NotInstalled,
        Installed,
        Foreign,
        Running,
        FolderProblem,
    }

    /// <summary>What the main button does for the inspected folder.</summary>
    public enum InstallAction
    {
        Install,
        Update,
        Reinstall,
    }

    public sealed class GameStatus
    {
        public StatusKind Kind { get; set; }

        /// <summary>A short headline for the status field, for example "Not installed".</summary>
        public string Title { get; set; }

        /// <summary>What to know or do about it, in plain words. Empty when the title says it all.</summary>
        public string Detail { get; set; }

        /// <summary>Title and detail as one text.</summary>
        public string Message
        {
            get { return string.IsNullOrEmpty(Detail) ? Title : Title + ". " + Detail; }
        }

        /// <summary>Set when this installer's install list was found and read.</summary>
        public InstallManifest Manifest { get; set; }

        /// <summary>Whether the console windows are on in UE4SS-settings.ini (false when there is no file).</summary>
        public bool ConsoleEnabled { get; set; }

        /// <summary>The game folder that was inspected, when it is a game folder (null when it is not).</summary>
        public string Root { get; set; }

        /// <summary>The mods folder of that game (it may not exist yet).</summary>
        public string ModsDir { get; set; }

        /// <summary>Install when there is no install list, Update when this installer is newer, else Reinstall.</summary>
        public InstallAction Action { get; set; }
    }

    /// <summary>What a run of the installer can be told, mostly so tests can replace the network and the game.</summary>
    public sealed class InstallerOptions
    {
        public Ue4ssPin Pin = Ue4ssPin.Current;

        public IDownloader Downloader = new HttpDownloader();

        public Func<IReadOnlyList<LoaderFile>> Loader = () => LoaderFiles.Embedded();

        public Func<bool> IsGameRunning = GameFolder.IsGameProcessRunning;

        /// <summary>Called with the file's number and path just before each file is written (tests make it throw).</summary>
        public Action<int, string> BeforeWriteFile;

        public string Version = typeof(InstallerOptions).Assembly.GetName().Version.ToString(3);
    }

    /// <summary>Install, update and remove. No UI: it reports through log and progress.</summary>
    public sealed class ModLoaderInstaller
    {
        private static readonly string[] RuntimeFiles = { "UE4SS.log", "imgui.ini" };

        private readonly InstallerOptions options;
        private readonly Action<string> log;
        private readonly Action<int> progress;
        private readonly Action<string> step;
        private volatile bool canCancel;

        public ModLoaderInstaller(InstallerOptions options, Action<string> log, Action<int> progress, Action<string> step = null)
        {
            this.options = options;
            this.log = log ?? (_ => { });
            this.progress = progress ?? (_ => { });
            this.step = step ?? (_ => { });
        }

        /// <summary>
        /// What the main button should do. installed is the version in the install list (null when there is none), current
        /// is this installer's. A version that cannot be read, or a newer installed one, means Reinstall.
        /// </summary>
        public static InstallAction ChooseAction(string installed, string current)
        {
            if (installed == null)
            {
                return InstallAction.Install;
            }

            Version installedVersion;
            Version currentVersion;
            if (Version.TryParse(installed, out installedVersion) && Version.TryParse(current, out currentVersion)
                && installedVersion < currentVersion)
            {
                return InstallAction.Update;
            }

            return InstallAction.Reinstall;
        }

        /// <summary>True when the last Install ended because it was cancelled.</summary>
        public bool WasCancelled { get; private set; }

        /// <summary>True while a running install can still be cancelled (nothing is written before the download is verified).</summary>
        public bool CanCancel
        {
            get { return canCancel; }
        }

        /// <summary>Looks at a folder without changing anything. Never throws.</summary>
        public GameStatus Inspect(string root)
        {
            try
            {
                return InspectCore(root);
            }
            catch (Exception ex)
            {
                return new GameStatus
                {
                    Kind = StatusKind.FolderProblem,
                    Title = "Could not read the game folder",
                    Detail = ex.Message + " Close other programs that may use it and try again.",
                };
            }
        }

        private GameStatus InspectCore(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return new GameStatus
                {
                    Kind = StatusKind.FolderProblem,
                    Title = "Choose the Alchemy Factory folder",
                    Detail = "Type its path above or click Browse.",
                };
            }

            GameFolder game;
            try
            {
                game = GameFolder.Open(root, false, false);
            }
            catch (InstallerException ex)
            {
                return new GameStatus { Kind = StatusKind.FolderProblem, Title = "This folder cannot be used", Detail = ex.Message };
            }

            InstallManifest manifest = null;
            if (game.ManifestFileExists())
            {
                manifest = game.ReadManifest();
                if (manifest == null)
                {
                    return Problem(
                        StatusKind.Foreign,
                        game,
                        "The install list cannot be read",
                        "The installer will not touch this game. Delete dwmapi.dll and the ue4ss folder in " + game.Win64
                        + " by hand to start clean.");
                }
            }
            else
            {
                string found;
                if (game.HasForeignUe4ss(out found))
                {
                    return Problem(StatusKind.Foreign, game, "Another UE4SS is in this game", ForeignDetail(found, game));
                }

                if (!GameFolder.IsAscii(game.Root))
                {
                    return Problem(
                        StatusKind.FolderProblem,
                        game,
                        "The game folder path has letters mods cannot read",
                        "Letters like n with a tilde or accents are a problem. "
                        + "Move the Steam library to a folder with plain English letters, digits and symbols only.");
                }
            }

            var status = new GameStatus
            {
                Manifest = manifest,
                Root = game.Root,
                ModsDir = game.ModsDir,
                Action = ChooseAction(manifest == null ? null : manifest.InstallerVersion, options.Version),
            };
            if (File.Exists(game.Settings))
            {
                status.ConsoleEnabled = ConsoleSetting.IsEnabled(File.ReadAllText(game.Settings));
            }

            if (options.IsGameRunning() || game.IsDllLocked())
            {
                status.Kind = StatusKind.Running;
                status.Title = "Alchemy Factory is running";
                status.Detail = "Close the game, then try again.";
            }
            else if (manifest != null)
            {
                status.Kind = StatusKind.Installed;
                status.Title = "Installed: AF Mod Loader " + manifest.InstallerVersion + " (UE4SS " + manifest.Ue4ssBuild + ")";
            }
            else
            {
                status.Kind = StatusKind.NotInstalled;
                status.Title = "Not installed";
            }

            return status;
        }

        public void Install(string root, bool enableConsole, CancellationToken cancel = default(CancellationToken))
        {
            bool changedGame = false;
            try
            {
                InstallCore(root, enableConsole, cancel, () => changedGame = true);
            }
            catch (InstallerException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw new InstallerException("Cancelled. The game folder was not changed.");
            }
            catch (Exception ex)
            {
                string advice = changedGame
                    ? " Nothing is lost: run Install again to finish, or Remove to take back what was installed."
                    : " Nothing was changed in the game folder.";
                throw new InstallerException("The install stopped: " + ex.Message + advice, ex);
            }
            finally
            {
                canCancel = false;
            }
        }

        public void Remove(string root)
        {
            try
            {
                RemoveCore(root);
            }
            catch (InstallerException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InstallerException(
                    "The removal stopped: " + ex.Message + " Close the game and other programs that use the game folder, "
                    + "then run Remove again.",
                    ex);
            }
        }

        private static GameStatus Problem(StatusKind kind, GameFolder game, string title, string detail)
        {
            return new GameStatus
            {
                Kind = kind,
                Title = title,
                Detail = detail,
                Root = game.Root,
                ModsDir = game.ModsDir,
            };
        }

        private static string ForeignDetail(string found, GameFolder game)
        {
            return "Found " + found.Substring(game.Win64.Length).TrimStart('\\') + " in " + game.Win64 + ", and this installer never changes it. "
                + "To use AF Mod Loader, delete dwmapi.dll and the ue4ss folder there by hand, then run Install again.";
        }

        private static string ForeignMessage(string found, GameFolder game)
        {
            return "Another UE4SS is already in this game. " + ForeignDetail(found, game);
        }

        private void InstallCore(string root, bool enableConsole, CancellationToken cancel, Action markChanged)
        {
            // Nothing is written until the download is verified, so the whole run up to there can be cancelled.
            canCancel = true;
            GameFolder game = GameFolder.Open(root, true, true);
            Say("Game folder: " + game.Root);
            EnsureClosed(game);
            InstallManifest previous = ReadOurManifestOrNone(game);
            game.EnsureNoLinks();
            cancel.ThrowIfCancellationRequested();

            Ue4ssPin pin = options.Pin;
            string temp = Path.GetTempFileName();
            try
            {
                Say("Temporary download file: " + temp + " (deleted when the install ends)");
                options.Downloader.Download(
                    pin.Url,
                    temp,
                    pin.Size,
                    (done, total) =>
                    {
                        progress(total > 0 ? (int)(done * 60 / total) : 0);
                        step("Downloading UE4SS (" + Ue4ssPin.FormatMegabytes(done) + " of " + Ue4ssPin.FormatMegabytes(pin.Size) + " MB)");
                    },
                    log,
                    cancel);
                progress(60);
                step("Checking the download");
                VerifyDownload(temp, pin);
                canCancel = false;

                // The check above must come before the first write to the game folder.
                EnsureClosed(game);
                using (ZipArchive zip = ZipFile.OpenRead(temp))
                {
                    List<PlannedFile> plan = BuildPlan(zip, game);
                    Apply(game, plan, previous, enableConsole, markChanged);
                }
            }
            finally
            {
                try
                {
                    File.Delete(temp);
                    Say("Deleted the temporary file.");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Say("Could not delete the temporary file " + temp + ": " + ex.Message);
                }
            }

            progress(100);
            Say("Done. AF Mod Loader is installed. Put mods in " + game.ModsDir);
        }

        private void Say(string message)
        {
            log(message);
        }

        private InstallManifest ReadOurManifestOrNone(GameFolder game)
        {
            if (game.ManifestFileExists())
            {
                InstallManifest manifest = game.ReadManifest();
                if (manifest == null)
                {
                    throw new InstallerException(
                        "The install list (" + InstallManifest.PathIn(game.Win64) + ") cannot be read, so the installer "
                        + "will not touch this game. Delete dwmapi.dll and the ue4ss folder in " + game.Win64
                        + " by hand, then run Install again.");
                }

                Say("Found an earlier install: AF Mod Loader " + manifest.InstallerVersion + ", UE4SS " + manifest.Ue4ssBuild + ".");
                return manifest;
            }

            string found;
            if (game.HasForeignUe4ss(out found))
            {
                throw new InstallerException(ForeignMessage(found, game));
            }

            return null;
        }

        private void EnsureClosed(GameFolder game)
        {
            if (options.IsGameRunning())
            {
                throw new InstallerException("Alchemy Factory is running. Close the game, then try again.");
            }

            if (game.IsDllLocked())
            {
                throw new InstallerException(
                    "dwmapi.dll is in use, so the game (or another program) still has it open. Close the game, "
                    + "check Task Manager that it is gone, then try again.");
            }
        }

        private void VerifyDownload(string file, Ue4ssPin pin)
        {
            long size = new FileInfo(file).Length;
            Say("Downloaded " + size.ToString("N0") + " bytes (expected " + pin.Size.ToString("N0") + ").");
            if (size != pin.Size)
            {
                throw new InstallerException(
                    "The download has the wrong size (" + size.ToString("N0") + " bytes instead of " + pin.Size.ToString("N0")
                    + "). Nothing was installed. Try again later.");
            }

            string hash;
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(file))
            {
                hash = string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
            }

            if (!hash.Equals(pin.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Say("SHA-256 does NOT match. Got " + hash + ", expected " + pin.Sha256 + ".");
                throw new InstallerException(
                    "The downloaded file does not match the expected SHA-256, so it was not installed. The game folder was "
                    + "not touched. Try again later; if it keeps happening, do not install.");
            }

            Say("SHA-256 matches: " + hash);
        }

        private List<PlannedFile> BuildPlan(ZipArchive zip, GameFolder game)
        {
            var plan = new List<PlannedFile>();
            bool dwmapi = false;
            bool dll = false;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = entry.FullName;
                if (name.EndsWith("/", StringComparison.Ordinal) || !PathRules.IsAllowedFromZip(name))
                {
                    continue;
                }

                PathRules.Resolve(game.Win64, name);
                if (name.Equals(PathRules.Settings, StringComparison.OrdinalIgnoreCase) && File.Exists(game.Settings))
                {
                    Say("Keeping your existing " + PathRules.Settings + " (your edits stay).");
                    continue;
                }

                dwmapi |= name.Equals(PathRules.Dwmapi, StringComparison.OrdinalIgnoreCase);
                dll |= name.Equals(PathRules.Ue4ssDll, StringComparison.OrdinalIgnoreCase);
                plan.Add(new PlannedFile(name, entry, null));
            }

            if (!dwmapi || !dll)
            {
                throw new InstallerException("The download does not contain the expected UE4SS files. Nothing was installed.");
            }

            foreach (LoaderFile file in options.Loader())
            {
                string name = PathRules.LoaderPrefix + file.Path;
                PathRules.Resolve(game.Win64, name);
                plan.Add(new PlannedFile(name, null, file.Content));
            }

            return plan;
        }

        private void Apply(GameFolder game, List<PlannedFile> plan, InstallManifest previous, bool enableConsole, Action markChanged)
        {
            // 0. Checks first, so a problem is found before the first change.
            var listed = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (previous != null)
            {
                foreach (string entry in previous.Files)
                {
                    if (IsManaged(game, entry) && seen.Add(entry))
                    {
                        listed.Add(entry);
                        CheckTarget(game, entry);
                    }
                }
            }

            var unknown = new List<string>();
            foreach (PlannedFile file in plan)
            {
                CheckTarget(game, file.Path);
                if (!seen.Contains(file.Path) && File.Exists(PathRules.Resolve(game.Win64, file.Path)))
                {
                    unknown.Add(file.Path);
                }
            }

            if (unknown.Count > 0)
            {
                throw new InstallerException(
                    "These files are already in the game and were not installed by this installer: "
                    + string.Join(", ", unknown.Take(8)) + (unknown.Count > 8 ? ", ..." : string.Empty)
                    + ". Another UE4SS or leftovers from an earlier try? Nothing was changed. Delete dwmapi.dll and the ue4ss "
                    + "folder in " + game.Win64 + " by hand, then run Install again.");
            }

            // 1. The manifest first, listing everything this install may create plus everything the last one made, so an
            //    interruption at any point still leaves Remove and the next Install able to find every file.
            foreach (PlannedFile file in plan)
            {
                if (seen.Add(file.Path))
                {
                    listed.Add(file.Path);
                }
            }

            markChanged();
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            new InstallManifest(options.Version, options.Pin.Build, now, listed).Save(game.Win64);
            Say("Wrote " + InstallManifest.PathIn(game.Win64) + " (" + listed.Count + " files listed).");

            // 2. Old loader files go, so files dropped by a newer loader do not stay behind.
            foreach (string entry in listed.Where(e => e.StartsWith(PathRules.LoaderPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                DeleteFile(game, entry);
            }

            // 3. The files. Each is created new: an existing file is deleted first, never written through, so a hard link
            //    cannot lead a write to a file outside the game folder.
            for (int i = 0; i < plan.Count; i++)
            {
                PlannedFile file = plan[i];
                if (options.BeforeWriteFile != null)
                {
                    options.BeforeWriteFile(i, file.Path);
                }

                step("Writing files (" + (i + 1) + " of " + plan.Count + ")");

                string target = PathRules.Resolve(game.Win64, file.Path);
                CheckTarget(game, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                DeleteExisting(target);
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (file.Entry != null)
                    {
                        using (Stream input = file.Entry.Open())
                        {
                            input.CopyTo(output);
                        }
                    }
                    else
                    {
                        output.Write(file.Content, 0, file.Content.Length);
                    }
                }

                Say("Wrote " + file.Path);
                progress(60 + (i + 1) * 38 / plan.Count);
            }

            // 4. The console option, through a temporary file next to the settings.
            if (File.Exists(game.Settings))
            {
                string ini = File.ReadAllText(game.Settings);
                if (ConsoleSetting.IsEnabled(ini) != enableConsole)
                {
                    string temp = game.Settings + ".afml-tmp";
                    DeleteExisting(temp);
                    File.WriteAllText(temp, ConsoleSetting.Apply(ini, enableConsole), new UTF8Encoding(false));
                    DeleteExisting(game.Settings);
                    File.Move(temp, game.Settings);
                    Say("Console windows " + (enableConsole ? "turned on" : "turned off") + " in " + PathRules.Settings);
                }
            }

            // 5. Where mods go. These are never removed.
            Directory.CreateDirectory(Path.Combine(game.ModsDir, ".data"));
            Say("Mods folder ready: " + game.ModsDir);

            // 6. The final manifest: only files that exist.
            List<string> final = listed.Where(e => File.Exists(PathRules.Resolve(game.Win64, e))).ToList();
            new InstallManifest(options.Version, options.Pin.Build, now, final).Save(game.Win64);
            Say("Updated the install list (" + final.Count + " files).");
        }

        private void RemoveCore(string root)
        {
            GameFolder game = GameFolder.Open(root, false, true);
            Say("Game folder: " + game.Root);
            EnsureClosed(game);

            if (!game.ManifestFileExists())
            {
                string found;
                if (game.HasForeignUe4ss(out found))
                {
                    throw new InstallerException(
                        "UE4SS is in this game (found " + found + ") but this installer did not put it there, so it will not "
                        + "remove it. To remove it by hand, delete dwmapi.dll and the ue4ss folder in " + game.Win64 + ".");
                }

                throw new InstallerException("Nothing to remove: AF Mod Loader is not installed in this game.");
            }

            InstallManifest manifest = game.ReadManifest();
            if (manifest == null)
            {
                throw new InstallerException(
                    "The install list (" + InstallManifest.PathIn(game.Win64) + ") cannot be read, so nothing was deleted. "
                    + "To remove everything by hand, delete dwmapi.dll and the ue4ss folder in " + game.Win64 + ".");
            }

            // Check every entry before the first delete: a link anywhere refuses the whole removal.
            game.EnsureNoLinks();
            var managed = new List<string>();
            int skipped = 0;
            foreach (string entry in manifest.Files)
            {
                if (IsManaged(game, entry))
                {
                    CheckTarget(game, entry);
                    managed.Add(entry);
                }
                else
                {
                    skipped++;
                    Say("Skipped (not a file this installer manages, left in place): " + entry);
                }
            }

            Say("Removing " + managed.Count + " listed files.");
            string ue4ss = Path.Combine(game.Win64, "ue4ss");
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < managed.Count; i++)
            {
                step("Removing files (" + (i + 1) + " of " + managed.Count + ")");
                DeleteFile(game, managed[i]);
                for (string dir = Path.GetDirectoryName(PathRules.Resolve(game.Win64, managed[i]));
                    dir != null && dir.Length > ue4ss.Length;
                    dir = Path.GetDirectoryName(dir))
                {
                    folders.Add(dir);
                }

                progress((i + 1) * 80 / managed.Count);
            }

            // Files UE4SS makes by itself while the game runs.
            foreach (string name in RuntimeFiles)
            {
                DeletePlain(Path.Combine(ue4ss, name), "ue4ss/" + name);
            }

            if (Directory.Exists(ue4ss))
            {
                foreach (string dump in Directory.GetFiles(ue4ss, "crash_*.dmp"))
                {
                    if (string.Equals(Path.GetExtension(dump), ".dmp", StringComparison.OrdinalIgnoreCase))
                    {
                        DeletePlain(dump, "ue4ss/" + Path.GetFileName(dump));
                    }
                }
            }

            // Only folders that held listed files are removed, deepest first, and only when empty. Failing here is a
            // warning: the files are already gone.
            foreach (string dir in folders.OrderByDescending(d => d.Length))
            {
                TryDeleteEmptyFolder(dir, game.Win64);
            }

            // The install list goes last, so a failure above can still be followed by another Remove.
            DeletePlain(InstallManifest.PathIn(game.Win64), "ue4ss/" + Path.GetFileName(InstallManifest.PathIn(game.Win64)));
            TryDeleteEmptyFolder(ue4ss, game.Win64);

            if (Directory.Exists(ue4ss))
            {
                string[] left = Directory.GetFileSystemEntries(ue4ss).Select(Path.GetFileName).ToArray();
                Say("Left in " + ue4ss + " (not installed by this installer): " + string.Join(", ", left)
                    + ". Delete them by hand if you want the folder gone.");
            }

            progress(100);
            Say("Done. AF Mod Loader is removed" + (skipped > 0 ? " (" + skipped + " odd entries skipped)" : string.Empty)
                + ". Your mods folder was kept: " + game.ModsDir);
        }

        private static bool IsManaged(GameFolder game, string entry)
        {
            if (!PathRules.IsAllowed(entry))
            {
                return false;
            }

            try
            {
                PathRules.Resolve(game.Win64, entry);
                return true;
            }
            catch (InstallerException)
            {
                return false;
            }
        }

        /// <summary>Refuses a target that is under a link, is itself a link, or is a folder.</summary>
        private static void CheckTarget(GameFolder game, string entry)
        {
            string path = PathRules.Resolve(game.Win64, entry);
            GameFolder.EnsureNoLinksAbove(game.Win64, path);
            if (Directory.Exists(path))
            {
                throw new InstallerException(path + " is a folder where a file is expected. Nothing was changed.");
            }

            if (File.Exists(path) && GameFolder.IsReparsePoint(path))
            {
                throw new InstallerException(
                    path + " is a link, and the installer never follows links. Delete the link, then try again. Nothing was changed.");
            }
        }

        /// <summary>Deletes a file if it is there (this name only: other names of a hard link are not touched).</summary>
        private static void DeleteExisting(string path)
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }

        private void DeleteFile(GameFolder game, string entry)
        {
            string path = PathRules.Resolve(game.Win64, entry);
            GameFolder.EnsureNoLinksAbove(game.Win64, path);
            DeletePlain(path, entry);
        }

        private void DeletePlain(string path, string shown)
        {
            if (File.Exists(path))
            {
                DeleteExisting(path);
                Say("Deleted " + shown);
            }
        }

        private void TryDeleteEmptyFolder(string dir, string win64)
        {
            try
            {
                if (Directory.Exists(dir) && !GameFolder.IsReparsePoint(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    Say("Deleted empty folder " + dir.Substring(win64.Length).TrimStart('\\').Replace('\\', '/'));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Say("Warning: could not delete the folder " + dir + " (" + ex.Message + "). Delete it by hand if you want it gone.");
            }
        }

        private sealed class PlannedFile
        {
            public PlannedFile(string path, ZipArchiveEntry entry, byte[] content)
            {
                Path = path;
                Entry = entry;
                Content = content;
            }

            public string Path { get; }

            public ZipArchiveEntry Entry { get; }

            public byte[] Content { get; }
        }
    }
}
