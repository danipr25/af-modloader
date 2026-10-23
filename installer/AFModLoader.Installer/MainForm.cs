using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AFModLoader.Installer.Core;

namespace AFModLoader.Installer
{
    /// <summary>The one window. All the work is in Core; this only shows it and keeps the UI responsive.</summary>
    public sealed class MainForm : Form
    {
        /// <summary>What a finished run should leave in the status area once the folder is looked at again.</summary>
        private enum Outcome
        {
            None,
            Installed,
            Removed,
            Cancelled,
        }

        private enum Severity
        {
            Info,
            Warning,
            Error,
        }

        // Sizes in the code are logical pixels at 96 DPI. Px scales them to the screen (the fonts already follow it).
        private const int ContentWidth = 540;
        private const int SidePadding = 20;

        private readonly Ue4ssPin pin = Ue4ssPin.Current;
        private readonly string version = typeof(MainForm).Assembly.GetName().Version.ToString(3);
        private readonly byte[] iconBytes = ReadIcon();

        // Surfaces.
        private readonly BorderPanel header = new BorderPanel();
        private readonly BorderPanel statusCard = new BorderPanel();
        private readonly BorderPanel footer = new BorderPanel();
        private readonly PictureBox logo = new PictureBox();

        // Header and folder.
        private readonly Label titleLabel = new Label();
        private readonly Label subtitleLabel = new Label();
        private readonly Label folderLabel = new Label();
        private readonly BorderedTextBox folderBox = new BorderedTextBox();
        private readonly ThemedButton browseButton = new ThemedButton();

        // Status card.
        private readonly Label statusIcon = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Label statusDetail = new Label();
        private readonly Label refreshMark = new Label();
        private readonly ThemedLink checkAgainLink = new ThemedLink();
        private readonly FlowLayoutPanel checkAgainBox = new FlowLayoutPanel();
        private readonly FlowLayoutPanel confirmRow = new FlowLayoutPanel();
        private readonly ThemedButton confirmRemoveButton = new ThemedButton();
        private readonly ThemedButton confirmCancelButton = new ThemedButton();

        // Body.
        private readonly Label downloadLabel = new Label();
        private readonly Label urlLabel = new Label();
        private readonly Label filesMark = new Label();
        private readonly ThemedLink filesLink = new ThemedLink();
        private readonly Label filesLabel = new Label();
        private readonly ThemedCheckBox consoleBox = new ThemedCheckBox();
        private readonly Label consoleHint = new Label();
        private readonly FlowLayoutPanel folderButtons = new FlowLayoutPanel();
        private readonly ThemedButton modsFolderButton = new ThemedButton();
        private readonly ThemedButton gameFolderButton = new ThemedButton();
        private readonly TableLayoutPanel progressPanel = new TableLayoutPanel();
        private readonly ThemedProgress progressBar = new ThemedProgress();
        private readonly Label stepLabel = new Label();
        private readonly ThemedButton cancelButton = new ThemedButton();
        private readonly BorderedTextBox detailsBox = new BorderedTextBox();

        // Footer.
        private readonly Label detailsMark = new Label();
        private readonly ThemedLink detailsLink = new ThemedLink();
        private readonly ThemedButton copyButton = new ThemedButton();
        private readonly ThemedButton removeButton = new ThemedButton();
        private readonly ThemedButton installButton = new ThemedButton();

        private readonly List<Label> secondaryLabels = new List<Label>();
        private readonly List<Label> linkMarks = new List<Label>();
        private readonly List<ThemedLink> links = new List<ThemedLink>();
        private readonly List<ThemedButton> buttons = new List<ThemedButton>();
        private readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 400 };

        private Theme theme;
        private GameStatus status;
        private bool stale;
        private string notice;
        private string noticeDetail;
        private bool focusSet;
        private bool noticeIsError;
        private bool confirming;
        private bool busy;
        private int refreshCounter;
        private Task inspects = Task.FromResult(0);
        private string lastStep;
        private bool closeWhenDone;
        private ModLoaderInstaller current;
        private CancellationTokenSource cancelSource;

        public MainForm(string initialFolder)
        {
            Text = "AF Mod Loader installer " + version;
            AutoScaleMode = AutoScaleMode.None;
            Font = Fonts.Body;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (iconBytes != null)
            {
                using (var stream = new MemoryStream(iconBytes))
                {
                    Icon = new Icon(stream);
                }
            }

            BuildLayout();
            ReadTheme();
            ActiveControl = browseButton;

            string folder = initialFolder ?? SteamLocator.FindGame() ?? string.Empty;
            folderBox.Inner.Text = folder;
            Log("AF Mod Loader installer " + version);
            Log(folder.Length > 0 ? "Game folder: " + folder : "Alchemy Factory was not found in the Steam libraries.");
            debounce.Tick += (s, e) =>
            {
                debounce.Stop();
                if (!busy)
                {
                    RefreshStatus(true, null, Outcome.None);
                }
            };
            Shown += (s, e) => RefreshStatus(true, null, Outcome.None);
        }

        private string FolderText
        {
            get { return folderBox.Inner.Text; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                debounce.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (theme != null)
            {
                Native.SetDarkTitleBar(Handle, theme.Dark);
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            // Windows changed the light/dark setting, or the high contrast one: look again.
            if (m.Msg == Native.WM_SYSCOLORCHANGE
                || (m.Msg == Native.WM_SETTINGCHANGE && (m.WParam.ToInt64() == Native.SPI_SETHIGHCONTRAST || IsColorSetChange(m.LParam))))
            {
                ReadTheme();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && confirming)
            {
                CancelRemoveQuestion();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                bool shutdown = e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing;
                if ((current == null || current.CanCancel) && cancelSource != null)
                {
                    // Not started yet, or still downloading: nothing in the game folder has changed, so closing means cancelling.
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

        private static bool IsColorSetChange(IntPtr lParam)
        {
            return lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet";
        }

        private int Px(int logical)
        {
            return (int)Math.Round(logical * DeviceDpi / 96.0);
        }

        /// <summary>The icon file embedded in the exe (several sizes), or null if it is missing.</summary>
        private static byte[] ReadIcon()
        {
            using (Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico"))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }

        // ---- Layout ----------------------------------------------------------------------------------------------

        private Label MakeLabel(Label label, string text, Font font, bool secondary)
        {
            label.AutoSize = true;
            label.Text = text;
            label.Font = font;
            label.MaximumSize = new Size(Px(ContentWidth), 0);
            label.Margin = new Padding(0);
            if (secondary)
            {
                secondaryLabels.Add(label);
            }

            return label;
        }

        private ThemedButton MakeButton(ThemedButton button, string text, bool primary)
        {
            button.Text = text;
            button.Primary = primary;
            button.AutoSize = true;
            button.Font = Fonts.Body;
            button.Padding = new Padding(Px(12), Px(4), Px(12), Px(4));
            button.MinimumSize = new Size(Px(88), Px(34));
            button.Margin = new Padding(0);
            buttons.Add(button);
            return button;
        }

        /// <summary>A small mark (an icon glyph or a chevron) in front of a link, both opening the same thing.</summary>
        private FlowLayoutPanel MakeLinkRow(Label mark, ThemedLink link, string text, EventHandler open)
        {
            link.Text = text;
            link.Margin = new Padding(0);
            link.LinkClicked += (s, e) => open(s, e);
            links.Add(link);

            // The mark is as tall as the link text and centred on it.
            mark.AutoSize = false;
            mark.TextAlign = ContentAlignment.MiddleCenter;
            mark.Size = new Size(Px(18), link.PreferredHeight);
            mark.Margin = new Padding(0, 0, Px(4), 0);
            linkMarks.Add(mark);
            mark.Click += open;
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Controls.Add(mark);
            row.Controls.Add(link);
            return row;
        }

        private static void AddRow(TableLayoutPanel table, Control control, int top)
        {
            control.Margin = new Padding(0, top, 0, 0);
            table.Controls.Add(control);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(ContentWidth + 2 * SidePadding)));
            root.Controls.Add(BuildHeader());
            root.Controls.Add(BuildBody());
            root.Controls.Add(BuildFooter());
            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            header.Sides = Sides.Bottom;
            header.AutoSize = true;
            header.Dock = DockStyle.Fill;
            header.Margin = new Padding(0);
            header.Padding = new Padding(Px(SidePadding), Px(16), Px(SidePadding), Px(16));

            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            logo.Size = new Size(Px(44), Px(44));
            logo.SizeMode = PictureBoxSizeMode.CenterImage;
            logo.Margin = new Padding(0, 0, Px(14), 0);
            if (iconBytes != null)
            {
                using (var stream = new MemoryStream(iconBytes))
                using (var icon = new Icon(stream, new Size(Px(32), Px(32))))
                {
                    logo.Image = icon.ToBitmap();
                }
            }

            var titles = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, Px(2), 0, 0),
            };
            titles.Controls.Add(MakeLabel(titleLabel, "AF Mod Loader", Fonts.Title, false));
            MakeLabel(subtitleLabel, "Loads mods for Alchemy Factory.", Fonts.Body, true);
            subtitleLabel.Margin = new Padding(Px(1), 0, 0, 0);
            titles.Controls.Add(subtitleLabel);
            row.Controls.Add(logo);
            row.Controls.Add(titles);
            header.Controls.Add(row);
            return header;
        }

        private Control BuildBody()
        {
            var body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            body.Margin = new Padding(Px(SidePadding), Px(18), Px(SidePadding), Px(18));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(ContentWidth)));

            AddRow(body, MakeLabel(folderLabel, "Game folder", Fonts.Small, true), 0);
            AddRow(body, BuildFolderRow(), Px(4));
            AddRow(body, BuildStatusCard(), Px(16));

            MakeLabel(
                downloadLabel,
                "Install downloads UE4SS " + pin.Build + " (" + pin.SizeText + ") from its official GitHub release and checks its SHA-256 ("
                + pin.ShortHash + ") before changing anything in the game folder. Nothing else is downloaded or sent.",
                Fonts.Small,
                true);
            AddRow(body, downloadLabel, Px(16));
            AddRow(body, MakeLabel(urlLabel, "URL: " + pin.Url, Fonts.Mono, true), Px(4));

            AddRow(body, MakeLinkRow(filesMark, filesLink, "What gets installed", (s, e) => SetFilesVisible(!filesLabel.Visible)), Px(10));
            MakeLabel(
                filesLabel,
                "SHA-256 of the download:\r\n" + pin.Sha256 + "\r\n\r\n"
                + "Files written, in AlchemyFactory\\Binaries\\Win64 inside the game folder:\r\n"
                + "  dwmapi.dll (the UE4SS proxy)\r\n"
                + "  ue4ss\\ (UE4SS, its settings file and its UEHelpers library)\r\n"
                + "  ue4ss\\Mods\\AFModLoader\\ (the loader)\r\n"
                + "  ue4ss\\af-modloader-install.txt (the list of these files)\r\n\r\n"
                + "Also in the game folder: mods and mods\\.data, created empty. Remove keeps them.\r\n"
                + "Outside the game folder: a temporary download file, deleted when the install ends.",
                Fonts.Mono,
                true);
            filesLabel.Visible = false;
            AddRow(body, filesLabel, Px(6));
            SetFilesVisible(false);

            consoleBox.Text = "Enable UE4SS console (for mod developers)";
            consoleBox.AutoSize = false;
            consoleBox.Font = Fonts.Body;
            consoleBox.Size = new Size(Px(ContentWidth), Px(26));
            consoleBox.CheckedChanged += (s, e) => UpdateButtons();
            AddRow(body, consoleBox, Px(14));
            MakeLabel(consoleHint, string.Empty, Fonts.Small, true);
            consoleHint.Margin = new Padding(Px(24), 0, 0, 0);
            body.Controls.Add(consoleHint);

            folderButtons.AutoSize = true;
            folderButtons.Visible = false;
            MakeButton(modsFolderButton, "Open &mods folder", false).Margin = new Padding(0, 0, Px(8), 0);
            MakeButton(gameFolderButton, "Open &game folder", false);
            modsFolderButton.Click += (s, e) => OpenFolder(
                status == null ? null : status.ModsDir,
                "The mods folder is created when you install.");
            gameFolderButton.Click += (s, e) => OpenFolder(
                status == null ? null : status.Root,
                "The game folder does not exist any more. Choose it again.");
            folderButtons.Controls.Add(modsFolderButton);
            folderButtons.Controls.Add(gameFolderButton);
            AddRow(body, folderButtons, Px(12));

            // Only while work is running.
            progressPanel.AutoSize = true;
            progressPanel.ColumnCount = 2;
            progressPanel.Dock = DockStyle.Fill;
            progressPanel.Visible = false;
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            progressBar.Dock = DockStyle.Fill;
            progressBar.Height = Px(6);
            progressBar.Margin = new Padding(0, 0, 0, Px(8));
            MakeLabel(stepLabel, string.Empty, Fonts.Small, true);
            stepLabel.MaximumSize = new Size(Px(ContentWidth - 110), 0);
            stepLabel.Anchor = AnchorStyles.Left;
            MakeButton(cancelButton, "Cancel", false);
            cancelButton.Click += (s, e) =>
            {
                cancelButton.Enabled = false;
                if (cancelSource != null)
                {
                    cancelSource.Cancel();
                }
            };
            progressPanel.Controls.Add(progressBar, 0, 0);
            progressPanel.SetColumnSpan(progressBar, 2);
            progressPanel.Controls.Add(stepLabel, 0, 1);
            progressPanel.Controls.Add(cancelButton, 1, 1);
            AddRow(body, progressPanel, Px(16));

            detailsBox.Inner.Multiline = true;
            detailsBox.Inner.ReadOnly = true;
            detailsBox.Inner.ScrollBars = ScrollBars.Both;
            detailsBox.Inner.WordWrap = false;
            detailsBox.Inner.Font = Fonts.Mono;
            detailsBox.Inner.HandleCreated += (s, e) => Native.SetControlTheme(detailsBox.Inner.Handle, theme != null && theme.Dark);
            detailsBox.Padding = new Padding(Px(8));
            detailsBox.Size = new Size(Px(ContentWidth), Px(170));
            detailsBox.Visible = false;
            AddRow(body, detailsBox, Px(16));
            return body;
        }

        private Control BuildFolderRow()
        {
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderBox.Inner.Font = Fonts.Body;
            folderBox.FitToFont(Px(10), Px(7));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, folderBox.Height));
            folderBox.Dock = DockStyle.Fill;
            folderBox.Margin = new Padding(0, 0, Px(8), 0);
            folderBox.Inner.TextChanged += (s, e) => OnFolderChanged();
            MakeButton(browseButton, "&Browse...", false);
            browseButton.Dock = DockStyle.Fill;
            browseButton.Click += (s, e) => Browse();
            row.Controls.Add(folderBox);
            row.Controls.Add(browseButton);
            return row;
        }

        private Control BuildStatusCard()
        {
            statusCard.Sides = Sides.All;
            statusCard.AutoSize = true;
            statusCard.Dock = DockStyle.Fill;
            statusCard.Padding = new Padding(Px(16), Px(14), Px(16), Px(14));

            var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(34)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            statusIcon.AutoSize = false;
            statusIcon.Size = new Size(Px(26), Px(26));
            statusIcon.TextAlign = ContentAlignment.MiddleCenter;
            statusIcon.Font = Fonts.Glyph ?? Fonts.Semibold;
            statusIcon.Margin = new Padding(0, Px(1), Px(8), 0);

            var texts = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0),
            };
            int textWidth = Px(ContentWidth - 32 - 34 - 120);
            MakeLabel(statusLabel, "Checking the game folder...", Fonts.Semibold, false);
            statusLabel.MaximumSize = new Size(textWidth, 0);
            statusLabel.Margin = new Padding(0, Px(3), 0, 0);
            MakeLabel(statusDetail, string.Empty, Fonts.Body, true);
            statusDetail.MaximumSize = new Size(textWidth, 0);
            statusDetail.Margin = new Padding(0, Px(4), 0, 0);
            statusDetail.Visible = false;
            texts.Controls.Add(statusLabel);
            texts.Controls.Add(statusDetail);

            refreshMark.Font = Fonts.GlyphSmall ?? Fonts.Semibold;
            refreshMark.Text = Fonts.GlyphSmall != null ? "\uE72C" : string.Empty;
            refreshMark.Visible = Fonts.GlyphSmall != null;
            checkAgainBox.AutoSize = true;
            checkAgainBox.WrapContents = false;
            checkAgainBox.Margin = new Padding(Px(8), Px(3), 0, 0);
            FlowLayoutPanel linkRow = MakeLinkRow(refreshMark, checkAgainLink, "Check again", (s, e) => CheckAgain());
            checkAgainBox.Controls.Add(linkRow);

            row.Controls.Add(statusIcon);
            row.Controls.Add(texts);
            row.Controls.Add(checkAgainBox);
            table.Controls.Add(row);

            // The question for Remove, in the same place as the status.
            confirmRow.AutoSize = true;
            confirmRow.Visible = false;
            confirmRow.Margin = new Padding(Px(34), Px(12), 0, 0);
            MakeButton(confirmRemoveButton, "Remove", true).Margin = new Padding(0, 0, Px(8), 0);
            MakeButton(confirmCancelButton, "Cancel", false);
            confirmRemoveButton.Click += (s, e) =>
            {
                confirming = false;
                RunRemove();
            };
            confirmCancelButton.Click += (s, e) => CancelRemoveQuestion();
            confirmRow.Controls.Add(confirmRemoveButton);
            confirmRow.Controls.Add(confirmCancelButton);
            table.Controls.Add(confirmRow);

            statusCard.Controls.Add(table);
            return statusCard;
        }

        private Control BuildFooter()
        {
            footer.Sides = Sides.Top;
            footer.AutoSize = true;
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0);
            footer.Padding = new Padding(Px(SidePadding), Px(12), Px(SidePadding), Px(12));

            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            FlowLayoutPanel detailsRow = MakeLinkRow(detailsMark, detailsLink, "Details", (s, e) => SetDetailsVisible(!detailsBox.Visible));
            detailsRow.Margin = new Padding(0, 0, Px(12), 0);
            MakeButton(copyButton, "Copy", false);
            copyButton.MinimumSize = new Size(Px(64), Px(30));
            copyButton.Visible = false;
            copyButton.Click += (s, e) => CopyDetails();
            left.Controls.Add(detailsRow);
            left.Controls.Add(copyButton);
            SetDetailsVisible(false);

            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            MakeButton(removeButton, "&Remove...", false).Margin = new Padding(0, 0, Px(8), 0);
            removeButton.Click += (s, e) => AskToRemove();
            MakeButton(installButton, "&Install", true);
            installButton.Click += (s, e) => RunInstall();
            right.Controls.Add(removeButton);
            right.Controls.Add(installButton);

            row.Controls.Add(left);
            row.Controls.Add(right);
            footer.Controls.Add(row);
            return footer;
        }

        // ---- Look ------------------------------------------------------------------------------------------------

        private void ReadTheme()
        {
            theme = Theme.Detect();
            ApplyTheme();
        }

        /// <summary>Gives every control the colours of the current theme.</summary>
        private void ApplyTheme()
        {
            BackColor = theme.Window;
            ForeColor = theme.Text;
            header.BackColor = theme.Header;
            header.BorderColor = theme.Border;
            statusCard.BackColor = theme.Card;
            statusCard.BorderColor = theme.Border;
            footer.BackColor = theme.Footer;
            footer.BorderColor = theme.Border;
            logo.BackColor = theme.LogoTile;
            titleLabel.ForeColor = theme.Text;
            statusLabel.ForeColor = theme.Text;
            folderBox.SetColors(theme.Window, theme.Text, theme.ControlBorder, theme.Accent);
            detailsBox.SetColors(theme.Card, theme.Text, theme.ControlBorder, theme.Accent);
            progressBar.SetColors(theme.Border, theme.Accent);
            consoleBox.ApplyTheme(theme);
            foreach (Label label in secondaryLabels)
            {
                label.ForeColor = theme.Secondary;
            }

            foreach (Label mark in linkMarks)
            {
                mark.ForeColor = theme.Accent;
            }

            foreach (ThemedLink link in links)
            {
                link.ApplyTheme(theme);
            }

            foreach (ThemedButton button in buttons)
            {
                button.ApplyTheme(theme);
            }

            if (IsHandleCreated)
            {
                Native.SetDarkTitleBar(Handle, theme.Dark);
            }

            if (detailsBox.Inner.IsHandleCreated)
            {
                Native.SetControlTheme(detailsBox.Inner.Handle, theme.Dark);
            }

            ShowStatus();
            Invalidate(true);
        }

        private void SetFilesVisible(bool visible)
        {
            filesLabel.Visible = visible;
            SetChevron(filesMark, visible);
        }

        private void SetDetailsVisible(bool visible)
        {
            detailsBox.Visible = visible;
            copyButton.Visible = visible;
            SetChevron(detailsMark, visible);
        }

        /// <summary>A chevron (right when closed, down when open): from the icon font when there is one, same size as the text.</summary>
        private static void SetChevron(Label mark, bool open)
        {
            if (Fonts.GlyphSmall != null)
            {
                mark.Font = Fonts.GlyphSmall;
                mark.Text = open ? "\uE70D" : "\uE76C";
            }
            else
            {
                mark.Font = Fonts.Semibold;
                mark.Text = open ? "v" : ">";
            }
        }

        private void CopyDetails()
        {
            try
            {
                Clipboard.SetText(detailsBox.Inner.Text);
            }
            catch (ExternalException)
            {
                MessageBox.Show(this, "Could not copy: another program is using the clipboard. Try again.", Text);
            }
        }

        private void Browse()
        {
            string picked = FolderPicker.Pick(
                this,
                "Choose the Alchemy Factory folder (the one Steam installed the game in).",
                Directory.Exists(FolderText) ? FolderText : null,
                Log);
            if (picked != null)
            {
                folderBox.Inner.Text = picked;
            }
        }

        /// <summary>Opens a folder in the file manager. Nothing else: no browser, no network.</summary>
        private void OpenFolder(string folder, string missing)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show(this, missing, Text);
                return;
            }

            // The path is kept as GetFullPath gives it, so a drive root such as D:\ stays valid.
            string full = Path.GetFullPath(folder);

            try
            {
                Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                MessageBox.Show(this, "Could not open " + full + ": " + ex.Message, Text);
            }
        }

        // ---- State -----------------------------------------------------------------------------------------------

        private void CheckAgain()
        {
            if (!busy && !confirming)
            {
                RefreshStatus(true, null, Outcome.None);
            }
        }

        private void OnFolderChanged()
        {
            status = null;
            notice = null;
            confirming = false;
            refreshCounter++;
            UpdateButtons();
            debounce.Stop();
            debounce.Start();
        }

        private void AskToRemove()
        {
            confirming = true;
            UpdateButtons();
            ActiveControl = confirmCancelButton;
        }

        private void CancelRemoveQuestion()
        {
            confirming = false;
            UpdateButtons();
            ActiveControl = removeButton.Enabled ? removeButton : installButton.Enabled ? (Control)installButton : browseButton;
        }

        private ModLoaderInstaller NewInstaller()
        {
            ModLoaderInstaller installer = null;
            installer = new ModLoaderInstaller(
                new InstallerOptions(),
                line => Post(() => Log(line)),
                percent => Post(() =>
                {
                    progressBar.Value = percent;
                    cancelButton.Enabled = installer.CanCancel && cancelSource != null && !cancelSource.IsCancellationRequested;
                }),
                text =>
                {
                    // The download reports every 80 KB; the window only hears about a change of text.
                    if (text != lastStep)
                    {
                        lastStep = text;
                        Post(() => stepLabel.Text = text);
                    }
                });
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
            detailsBox.Inner.AppendText(line + "\r\n");
        }

        /// <summary>
        /// Looks at the folder in the background and updates the status area. A result that is not the newest request
        /// is dropped. failure is the message of the operation this refresh follows, if it failed; outcome says what
        /// a successful one was.
        /// </summary>
        private void RefreshStatus(bool syncConsole, Tuple<string, string> failure, Outcome outcome)
        {
            // Every look is remembered, so a run can wait for all of them: checking the game folder opens dwmapi.dll
            // exclusively, which must not happen while files are being installed or removed.
            inspects = Task.WhenAll(inspects, RefreshSafe(syncConsole, failure, outcome));
        }

        /// <summary>The look at the folder must never end faulted: a later run waits for it.</summary>
        private async Task RefreshSafe(bool syncConsole, Tuple<string, string> failure, Outcome outcome)
        {
            try
            {
                await RefreshAsync(syncConsole, failure, outcome);
            }
            catch (Exception ex)
            {
                Log("Could not update the window: " + ex.Message);
            }
        }

        private async Task RefreshAsync(bool syncConsole, Tuple<string, string> failure, Outcome outcome)
        {
            int ticket = ++refreshCounter;
            string folder = FolderText;
            if (failure == null && outcome == Outcome.None)
            {
                notice = null;
                SetStatusText("Checking the game folder...", null);
            }

            GameStatus result;
            try
            {
                result = await Task.Run(() => new ModLoaderInstaller(new InstallerOptions(), null, null).Inspect(folder));
            }
            catch (Exception ex)
            {
                result = new GameStatus { Kind = StatusKind.FolderProblem, Title = "Could not read the game folder", Detail = ex.Message };
            }

            if (ticket != refreshCounter || folder != FolderText)
            {
                return;
            }

            status = result;
            stale = false;
            noticeIsError = failure != null;
            notice = null;
            noticeDetail = null;
            if (failure != null)
            {
                notice = failure.Item1;
                noticeDetail = failure.Item2;
            }
            else if (outcome == Outcome.Installed && result.Kind == StatusKind.Installed)
            {
                notice = result.Title;
                noticeDetail = "Next: put mods in the mods folder, then start the game from Steam.";
            }
            else if (outcome == Outcome.Cancelled)
            {
                notice = "Cancelled";
                noticeDetail = "The game folder was not changed.";
            }
            else if (outcome == Outcome.Removed && result.Kind == StatusKind.NotInstalled)
            {
                notice = "Removed";
                noticeDetail = "Your mods folder was kept.";
            }

            if (!focusSet && installButton.Enabled)
            {
                // Nothing in the window starts selected: the main button takes the focus once it can be used.
                focusSet = true;
                ActiveControl = installButton;
            }

            if (syncConsole && result.Manifest != null)
            {
                consoleBox.Checked = result.ConsoleEnabled;
            }

            UpdateButtons();
        }

        /// <summary>The status area: the question for Remove, the result of the last run, or what the folder looks like.</summary>
        private void ShowStatus()
        {
            Severity severity = Severity.Info;
            if (confirming)
            {
                severity = Severity.Warning;
                SetStatusText("Remove AF Mod Loader and UE4SS from this game?", "Your mods folder is kept.");
            }
            else if (noticeIsError && notice != null)
            {
                severity = Severity.Error;
            }
            else if (notice == null && !busy && status != null)
            {
                switch (status.Kind)
                {
                    case StatusKind.Running:
                    case StatusKind.Foreign:
                        severity = Severity.Warning;
                        break;
                    case StatusKind.FolderProblem:
                        severity = Severity.Error;
                        break;
                }
            }

            if (!confirming && !busy && notice != null)
            {
                SetStatusText(notice, noticeDetail);
            }
            else if (!confirming && !busy && stale)
            {
                SetStatusText("Checking the game folder...", null);
            }
            else if (!confirming && !busy && status != null)
            {
                SetStatusText(status.Title, status.Detail);
            }

            SetStatusIcon(severity);
        }

        /// <summary>The bold headline, and the rest in regular weight under it.</summary>
        private void SetStatusText(string title, string detail)
        {
            statusLabel.Text = title;
            statusDetail.Text = detail ?? string.Empty;
            statusDetail.Visible = !string.IsNullOrEmpty(detail);
        }

        /// <summary>An icon from the icon font (the system icons look wrong on dark), or a letter when Windows has none.</summary>
        private void SetStatusIcon(Severity severity)
        {
            if (theme == null)
            {
                return;
            }

            bool glyphs = Fonts.Glyph != null;
            switch (severity)
            {
                case Severity.Warning:
                    statusIcon.Text = glyphs ? "\uE7BA" : "!";
                    statusIcon.ForeColor = theme.Warning;
                    break;
                case Severity.Error:
                    statusIcon.Text = glyphs ? "\uEA39" : "x";
                    statusIcon.ForeColor = theme.Error;
                    break;
                default:
                    statusIcon.Text = glyphs ? "\uE946" : "i";
                    statusIcon.ForeColor = theme.Accent;
                    break;
            }
        }

        private void UpdateButtons()
        {
            bool known = status != null && !busy && !stale && !confirming;
            bool valid = status != null && (status.Kind == StatusKind.Installed || status.Kind == StatusKind.NotInstalled);
            bool installed = status != null && status.Manifest != null;
            InstallAction action = status == null ? InstallAction.Install : status.Action;
            string name = ActionName(action);
            installButton.Text = action == InstallAction.Reinstall ? "Re&install" : "&" + name;
            installButton.Enabled = known && valid;
            removeButton.Enabled = known && status.Kind == StatusKind.Installed;
            folderButtons.Visible = status != null && status.Root != null;
            folderButtons.Enabled = !busy && !confirming;
            confirmRow.Visible = confirming;
            checkAgainLink.SetUsable(!busy && !confirming);
            progressPanel.Visible = busy;
            folderBox.Inner.ReadOnly = busy || confirming;
            browseButton.Enabled = !busy && !confirming;
            consoleBox.Enabled = !busy && !confirming;
            consoleHint.Text = installed && known && consoleBox.Checked != status.ConsoleEnabled
                ? "Click " + name + " to apply this."
                : string.Empty;
            ShowStatus();
        }

        private string ActionName(InstallAction action)
        {
            switch (action)
            {
                case InstallAction.Update:
                    return "Update to " + version;
                case InstallAction.Reinstall:
                    return "Reinstall";
                default:
                    return "Install";
            }
        }

        // ---- Work ------------------------------------------------------------------------------------------------

        private async void RunInstall()
        {
            if (status == null || status.Root == null)
            {
                return;
            }

            string root = status.Root;
            bool console = consoleBox.Checked;
            string message = status.Action == InstallAction.Update ? "Updating..."
                : status.Action == InstallAction.Reinstall ? "Reinstalling..."
                : "Installing...";
            await Run((installer, cancel) => installer.Install(root, console, cancel), message, Outcome.Installed, true);
        }

        private async void RunRemove()
        {
            if (status == null || status.Root == null)
            {
                return;
            }

            string root = status.Root;
            await Run((installer, cancel) => installer.Remove(root), "Removing...", Outcome.Removed, false);
        }

        private async Task Run(Action<ModLoaderInstaller, CancellationToken> work, string message, Outcome outcome, bool cancellable)
        {
            busy = true;
            refreshCounter++;
            notice = null;
            noticeIsError = false;
            lastStep = null;
            cancelSource = new CancellationTokenSource();
            cancelButton.Visible = cancellable;
            cancelButton.Enabled = true;
            progressBar.Value = 0;
            stepLabel.Text = "Starting...";
            SetStatusText(message, null);
            UpdateButtons();

            // The button just clicked is now disabled; without this, focus falls to the folder box and selects the path.
            if (cancellable)
            {
                cancelButton.Focus();
            }
            else
            {
                ActiveControl = null;
            }

            Tuple<string, string> failure = null;
            ModLoaderInstaller installer = null;
            try
            {
                // A look at the folder that is still running would open dwmapi.dll exclusively under the work below.
                await inspects;
                if (!closeWhenDone)
                {
                    installer = NewInstaller();
                    current = installer;
                    CancellationToken token = cancelSource.Token;
                    await Task.Run(() => work(installer, token));
                }
            }
            catch (Exception ex)
            {
                if (installer != null && installer.WasCancelled)
                {
                    // The player asked for this: not an error, and nothing to look at in the details.
                    outcome = Outcome.Cancelled;
                    Log(ex.Message);
                }
                else
                {
                    bool expected = ex is InstallerException;
                    failure = expected
                        ? Tuple.Create("Could not finish", ex.Message)
                        : Tuple.Create("Something went wrong", ex.Message + " (Details has the full report.)");
                    Log("Error: " + ex.Message);
                    if (!expected || ex.InnerException != null)
                    {
                        Log(ex.ToString());
                    }
                }
            }

            busy = false;
            current = null;
            cancelSource.Dispose();
            cancelSource = null;
            if (closeWhenDone)
            {
                Close();
                return;
            }

            if (failure != null)
            {
                SetDetailsVisible(true);
            }

            stale = true;
            UpdateButtons();
            RefreshStatus(true, failure, outcome);
        }
    }
}
