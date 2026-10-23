using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AFModLoader.Installer.Core;

namespace AFModLoader.Installer
{
    /// <summary>The one window. All the work is in Core; this only shows it and keeps the UI responsive.</summary>
    public sealed class MainForm : Form
    {
        private const int ContentWidth = 540;

        private readonly Ue4ssPin pin = Ue4ssPin.Current;
        private readonly TextBox folderBox = new TextBox();
        private readonly Button browseButton = new Button();
        private readonly Label statusLabel = new Label();
        private readonly CheckBox consoleBox = new CheckBox();
        private readonly Label consoleHint = new Label();
        private readonly Button installButton = new Button();
        private readonly Button removeButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly Button detailsButton = new Button();
        private readonly Button copyButton = new Button();
        private readonly TextBox detailsBox = new TextBox();
        private readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 400 };

        private GameStatus status;
        private bool busy;
        private int refreshCounter;
        private bool closeWhenDone;
        private ModLoaderInstaller current;
        private CancellationTokenSource cancelSource;

        public MainForm(string initialFolder)
        {
            Text = "AF Mod Loader installer";
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            BuildLayout();

            string folder = initialFolder ?? SteamLocator.FindGame() ?? string.Empty;
            folderBox.Text = folder;
            Log("AF Mod Loader installer " + typeof(MainForm).Assembly.GetName().Version.ToString(3));
            Log(folder.Length > 0 ? "Game folder: " + folder : "Alchemy Factory was not found in the Steam libraries.");
            debounce.Tick += (s, e) =>
            {
                debounce.Stop();
                if (!busy)
                {
                    RefreshStatus(true, null);
                }
            };
            Shown += (s, e) => RefreshStatus(true, null);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                bool shutdown = e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing;
                if (current != null && current.CanCancel && cancelSource != null)
                {
                    // Still downloading: nothing in the game folder has changed, so closing means cancelling.
                    cancelSource.Cancel();
                    if (!shutdown)
                    {
                        e.Cancel = true;
                        closeWhenDone = true;
                    }
                }
                else if (!shutdown)
                {
                    e.Cancel = true;
                    MessageBox.Show(this, "Files in the game folder are being changed. Please wait until the installer finishes.", Text);
                }
            }

            base.OnFormClosing(e);
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Text = text,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0, 6, 0, 0),
            };
        }

        private void BuildLayout()
        {
            var table = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            table.Controls.Add(MakeLabel("Alchemy Factory folder"));

            var folderRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0) };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderBox.Dock = DockStyle.Fill;
            folderBox.Width = ContentWidth - 40;
            folderBox.TextChanged += (s, e) => OnFolderChanged();
            browseButton.Text = "...";
            browseButton.AutoSize = true;
            browseButton.Click += (s, e) => Browse();
            folderRow.Controls.Add(folderBox);
            folderRow.Controls.Add(browseButton);
            table.Controls.Add(folderRow);

            statusLabel.AutoSize = true;
            statusLabel.MaximumSize = new Size(ContentWidth, 0);
            statusLabel.Margin = new Padding(0, 10, 0, 0);
            statusLabel.Font = new Font(Font, FontStyle.Bold);
            table.Controls.Add(statusLabel);

            table.Controls.Add(MakeLabel(
                "Install downloads UE4SS " + pin.Build + " from github.com/UE4SS-RE/RE-UE4SS (SHA-256 " + pin.ShortHash + ").\r\n"
                + "URL: " + pin.Url + "\r\n"
                + "SHA-256: " + pin.Sha256 + "\r\n"
                + "The file is checked before anything in the game folder is changed. Nothing else is downloaded or sent."));

            consoleBox.Text = "Enable UE4SS console (for mod developers)";
            consoleBox.AutoSize = true;
            consoleBox.Margin = new Padding(0, 10, 0, 0);
            consoleBox.CheckedChanged += (s, e) => UpdateButtons();
            table.Controls.Add(consoleBox);

            consoleHint.AutoSize = true;
            consoleHint.MaximumSize = new Size(ContentWidth, 0);
            consoleHint.Margin = new Padding(18, 0, 0, 0);
            table.Controls.Add(consoleHint);

            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            installButton.AutoSize = true;
            installButton.Text = "Install";
            installButton.Click += (s, e) => RunInstall();
            removeButton.AutoSize = true;
            removeButton.Text = "Remove";
            removeButton.Click += (s, e) => RunRemove();
            buttons.Controls.Add(installButton);
            cancelButton.AutoSize = true;
            cancelButton.Text = "Cancel";
            cancelButton.Visible = false;
            cancelButton.Click += (s, e) =>
            {
                cancelButton.Enabled = false;
                if (cancelSource != null)
                {
                    cancelSource.Cancel();
                }
            };
            buttons.Controls.Add(removeButton);
            buttons.Controls.Add(cancelButton);
            table.Controls.Add(buttons);

            progressBar.Width = ContentWidth;
            progressBar.Margin = new Padding(0, 10, 0, 0);
            table.Controls.Add(progressBar);

            var detailButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            detailsButton.AutoSize = true;
            detailsButton.Text = "Show details";
            detailsButton.Click += (s, e) => SetDetailsVisible(!detailsBox.Visible);
            copyButton.AutoSize = true;
            copyButton.Text = "Copy";
            copyButton.Click += (s, e) => CopyDetails();
            detailButtons.Controls.Add(detailsButton);
            detailButtons.Controls.Add(copyButton);
            table.Controls.Add(detailButtons);

            detailsBox.Multiline = true;
            detailsBox.ReadOnly = true;
            detailsBox.ScrollBars = ScrollBars.Both;
            detailsBox.WordWrap = false;
            detailsBox.Font = new Font("Consolas", Font.Size);
            detailsBox.Size = new Size(ContentWidth, 170);
            detailsBox.Visible = false;
            table.Controls.Add(detailsBox);

            Controls.Add(table);
        }

        private void SetDetailsVisible(bool visible)
        {
            detailsBox.Visible = visible;
            detailsButton.Text = visible ? "Hide details" : "Show details";
        }

        private void CopyDetails()
        {
            try
            {
                Clipboard.SetText(detailsBox.Text);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(this, "Could not copy: another program is using the clipboard. Try again.", Text);
            }
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose the Alchemy Factory folder (the one Steam installed the game in).";
                if (Directory.Exists(folderBox.Text))
                {
                    dialog.SelectedPath = folderBox.Text;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    folderBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void OnFolderChanged()
        {
            status = null;
            UpdateButtons();
            debounce.Stop();
            debounce.Start();
        }

        private ModLoaderInstaller NewInstaller()
        {
            ModLoaderInstaller installer = null;
            installer = new ModLoaderInstaller(
                new InstallerOptions(),
                line => Post(() => Log(line)),
                percent => Post(() =>
                {
                    progressBar.Value = Math.Max(0, Math.Min(100, percent));
                    cancelButton.Enabled = installer.CanCancel && cancelSource != null && !cancelSource.IsCancellationRequested;
                }));
            return installer;
        }

        /// <summary>Runs an action on the UI thread, unless the window is already gone.</summary>
        private void Post(Action action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(action);
                }
            }
            catch (InvalidOperationException)
            {
                // The window closed while the worker was still reporting.
            }
        }

        private void Log(string line)
        {
            detailsBox.AppendText(line + "\r\n");
        }

        /// <summary>
        /// Looks at the folder in the background and updates the status line. A result that is not the newest request
        /// is dropped. failure is the message of the operation this refresh follows, if it failed.
        /// </summary>
        private async void RefreshStatus(bool syncConsole, string failure)
        {
            int ticket = ++refreshCounter;
            string folder = folderBox.Text;
            GameStatus result;
            try
            {
                result = await Task.Run(() => new ModLoaderInstaller(new InstallerOptions(), null, null).Inspect(folder));
            }
            catch (Exception ex)
            {
                result = new GameStatus { Kind = StatusKind.FolderProblem, Message = "Could not read the game folder: " + ex.Message };
            }

            if (ticket != refreshCounter)
            {
                return;
            }

            status = result;
            bool fine = result.Kind == StatusKind.Installed || result.Kind == StatusKind.NotInstalled;
            statusLabel.Text = failure ?? result.Message;
            statusLabel.ForeColor = fine && failure == null ? SystemColors.ControlText : Color.Firebrick;
            if (syncConsole && result.Manifest != null)
            {
                consoleBox.Checked = result.ConsoleEnabled;
            }

            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool known = status != null && !busy;
            bool installed = status != null && status.Manifest != null;
            installButton.Text = installed ? "Update" : "Install";
            installButton.Enabled = known && (status.Kind == StatusKind.Installed || status.Kind == StatusKind.NotInstalled);
            removeButton.Enabled = known && status.Kind == StatusKind.Installed;
            folderBox.Enabled = !busy;
            browseButton.Enabled = !busy;
            consoleBox.Enabled = !busy;
            consoleHint.Text = installed && !busy && consoleBox.Checked != status.ConsoleEnabled
                ? "Click Update to apply this."
                : string.Empty;
        }

        private async void RunInstall()
        {
            bool console = consoleBox.Checked;
            string folder = folderBox.Text;
            await Run(
                (installer, cancel) => installer.Install(folder, console, cancel),
                installButton.Text == "Update" ? "Updating..." : "Installing...");
        }

        private async void RunRemove()
        {
            DialogResult answer = MessageBox.Show(
                this,
                "Remove AF Mod Loader and UE4SS from this game?\r\n\r\nYour mods folder is kept.",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            string folder = folderBox.Text;
            await Run((installer, cancel) => installer.Remove(folder), "Removing...");
        }

        private async Task Run(Action<ModLoaderInstaller, CancellationToken> work, string message)
        {
            busy = true;
            refreshCounter++;
            cancelSource = new CancellationTokenSource();
            cancelButton.Visible = true;
            cancelButton.Enabled = true;
            progressBar.Value = 0;
            statusLabel.ForeColor = SystemColors.ControlText;
            statusLabel.Text = message;
            UpdateButtons();
            string failure = null;
            try
            {
                ModLoaderInstaller installer = NewInstaller();
                current = installer;
                CancellationToken token = cancelSource.Token;
                await Task.Run(() => work(installer, token));
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                Log("Error: " + ex.Message);
                if (!(ex is InstallerException) || ex.InnerException != null)
                {
                    Log(ex.ToString());
                }
            }

            busy = false;
            current = null;
            cancelSource.Dispose();
            cancelSource = null;
            cancelButton.Visible = false;
            if (closeWhenDone)
            {
                Close();
                return;
            }

            if (failure != null)
            {
                SetDetailsVisible(true);
            }

            RefreshStatus(true, failure);
        }
    }
}
