using System;
using System.Drawing;
using System.Windows.Forms;

namespace AFModLoader.Installer
{
    /// <summary>Which edges of a <see cref="BorderPanel"/> get a line.</summary>
    [Flags]
    internal enum Sides
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        All = 15,
    }

    /// <summary>A panel with a one pixel line on some or all of its edges.</summary>
    internal class BorderPanel : Panel
    {
        private Color borderColor = SystemColors.ControlDark;

        public BorderPanel()
        {
            ResizeRedraw = true;
            DoubleBuffered = true;
        }

        public Sides Sides { get; set; }

        public Color BorderColor
        {
            get
            {
                return borderColor;
            }

            set
            {
                borderColor = value;
                Invalidate();
            }
        }

        protected virtual int LineWidth
        {
            get { return Math.Max(1, DeviceDpi / 96); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int w = LineWidth;
            using (var brush = new SolidBrush(borderColor))
            {
                if (Sides == Sides.All)
                {
                    e.Graphics.FillRectangle(brush, 0, 0, Width, w);
                    e.Graphics.FillRectangle(brush, 0, Height - w, Width, w);
                    e.Graphics.FillRectangle(brush, 0, 0, w, Height);
                    e.Graphics.FillRectangle(brush, Width - w, 0, w, Height);
                    return;
                }

                if ((Sides & Sides.Top) != 0)
                {
                    e.Graphics.FillRectangle(brush, 0, 0, Width, w);
                }

                if ((Sides & Sides.Bottom) != 0)
                {
                    e.Graphics.FillRectangle(brush, 0, Height - w, Width, w);
                }
            }
        }
    }

    /// <summary>
    /// A text box inside a panel that draws the border: the accent colour while the box has the focus, same thickness.
    /// The box itself never gets disabled (that would grey its text): it is made read only instead.
    /// </summary>
    internal sealed class BorderedTextBox : BorderPanel
    {
        private Color idleColor = SystemColors.ControlDark;
        private Color focusColor = SystemColors.Highlight;

        public BorderedTextBox()
        {
            Sides = Sides.All;
            Inner.BorderStyle = BorderStyle.None;
            Inner.Dock = DockStyle.Fill;
            Inner.Enter += (s, e) => ShowFocus();
            Inner.Leave += (s, e) => ShowFocus();
            Controls.Add(Inner);
        }

        public TextBox Inner { get; } = new TextBox();

        /// <summary>The focus ring is twice as thick as the idle border.</summary>
        protected override int LineWidth
        {
            get { return base.LineWidth * (Inner.Focused ? 2 : 1); }
        }

        public void SetColors(Color back, Color text, Color idle, Color focus)
        {
            BackColor = back;
            Inner.BackColor = back;
            Inner.ForeColor = text;
            idleColor = idle;
            focusColor = focus;
            ShowFocus();
        }

        /// <summary>Sets the padding around the text and the height that fits the font.</summary>
        public void FitToFont(int sidePadding, int verticalPadding)
        {
            Padding = new Padding(sidePadding, verticalPadding, sidePadding, verticalPadding);
            Height = Inner.PreferredHeight + 2 * verticalPadding + 2 * LineWidth;
        }

        private void ShowFocus()
        {
            BorderColor = Inner.Focused ? focusColor : idleColor;
        }
    }

    /// <summary>
    /// A button that draws itself from the theme, so a disabled one keeps readable text. With high contrast it is a normal
    /// system button.
    /// </summary>
    internal sealed class ThemedButton : Button
    {
        private Theme theme;
        private bool hover;
        private bool pressed;

        public ThemedButton()
        {
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        /// <summary>The main button of the window: filled with the accent colour.</summary>
        public bool Primary { get; set; }

        public void ApplyTheme(Theme value)
        {
            theme = value;
            FlatStyle = value.HighContrast ? FlatStyle.System : FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint, !value.HighContrast);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            hover = false;
            pressed = false;
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (theme == null || theme.HighContrast)
            {
                base.OnPaint(e);
                return;
            }

            Color fill;
            Color line;
            Color text;
            if (!Enabled)
            {
                fill = BackColor;
                line = theme.Border;
                text = theme.Disabled;
            }
            else if (Primary)
            {
                fill = pressed ? theme.AccentDown : hover ? theme.AccentHover : theme.Accent;
                line = fill;
                text = theme.AccentText;
            }
            else
            {
                fill = pressed ? theme.OutlineDown : hover ? theme.OutlineHover : BackColor;
                line = theme.ControlBorder;
                text = theme.Text;
            }

            Graphics g = e.Graphics;
            int w = Math.Max(1, DeviceDpi / 96);
            Rectangle body = ClientRectangle;
            if (Focused && Enabled)
            {
                // The focus ring: 2 px at the edge, then a 1 px gap in the surface colour, then the button.
                using (var brush = new SolidBrush(Primary ? theme.Text : theme.Accent))
                {
                    g.FillRectangle(brush, body);
                }

                using (var brush = new SolidBrush(BackColor))
                {
                    g.FillRectangle(brush, Rectangle.Inflate(body, -2 * w, -2 * w));
                }

                body = Rectangle.Inflate(body, -3 * w, -3 * w);
            }

            using (var brush = new SolidBrush(line))
            {
                g.FillRectangle(brush, body);
            }

            using (var brush = new SolidBrush(fill))
            {
                g.FillRectangle(brush, Rectangle.Inflate(body, -w, -w));
            }

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis;
            if (!ShowKeyboardCues)
            {
                flags |= TextFormatFlags.HidePrefix;
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, flags);
        }
    }

    /// <summary>A check box drawn from the theme (the system one is hard to see on dark and greys its text out).</summary>
    internal sealed class ThemedCheckBox : CheckBox
    {
        private Theme theme;

        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void ApplyTheme(Theme value)
        {
            theme = value;
            FlatStyle = value.HighContrast ? FlatStyle.System : FlatStyle.Standard;
            SetStyle(ControlStyles.UserPaint, !value.HighContrast);
            Invalidate();
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (theme == null || theme.HighContrast)
            {
                base.OnPaint(e);
                return;
            }

            // AllPaintingInWmPaint skips the background pass, so clear it here or old pixels stay visible.
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int w = Math.Max(1, DeviceDpi / 96);
            int size = LogicalToDeviceUnits(16);
            int room = 3 * w;
            var box = new Rectangle(room, (Height - size) / 2, size, size);
            if (Focused && Enabled)
            {
                // The focus ring around the box: 1 px gap (the surface), then 2 px in the accent colour.
                using (var brush = new SolidBrush(theme.Accent))
                {
                    g.FillRectangle(brush, Rectangle.Inflate(box, room, room));
                }

                using (var brush = new SolidBrush(BackColor))
                {
                    g.FillRectangle(brush, Rectangle.Inflate(box, w, w));
                }
            }

            Color line = !Enabled ? theme.Border : theme.ControlBorder;
            Color fill = !Enabled ? theme.Disabled : theme.Accent;
            using (var brush = new SolidBrush(line))
            {
                g.FillRectangle(brush, box);
            }

            Rectangle inner = Rectangle.Inflate(box, -w, -w);
            using (var brush = new SolidBrush(Checked ? fill : BackColor))
            {
                g.FillRectangle(brush, inner);
            }

            if (Checked)
            {
                // The tick: two lines.
                using (var pen = new Pen(Enabled ? theme.AccentText : BackColor, Math.Max(2f, size / 8f)))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.DrawLines(
                        pen,
                        new[]
                        {
                            new PointF(box.X + size * 0.24f, box.Y + size * 0.52f),
                            new PointF(box.X + size * 0.43f, box.Y + size * 0.71f),
                            new PointF(box.X + size * 0.76f, box.Y + size * 0.30f),
                        });
                }
            }

            int gap = LogicalToDeviceUnits(8);
            var textArea = new Rectangle(2 * room + size + gap, 0, Width - 2 * room - size - gap, Height);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (!ShowKeyboardCues)
            {
                flags |= TextFormatFlags.HidePrefix;
            }

            TextRenderer.DrawText(g, Text, Font, textArea, Enabled ? theme.Text : theme.Disabled, flags);
        }
    }

    /// <summary>
    /// A link in the accent colour: underlined on hover, and Space works as well as Enter. A link that cannot be used
    /// now is not disabled (Windows draws that text embossed): it is shown in the disabled colour and ignores clicks.
    /// </summary>
    internal sealed class ThemedLink : LinkLabel
    {
        private Theme theme;
        private bool usable = true;

        public ThemedLink()
        {
            LinkBehavior = LinkBehavior.HoverUnderline;
            AutoSize = true;
            Font = Fonts.Semibold;
        }

        public void ApplyTheme(Theme value)
        {
            theme = value;
            Recolor();
        }

        public void SetUsable(bool value)
        {
            usable = value;
            Recolor();
        }

        protected override void OnLinkClicked(LinkLabelLinkClickedEventArgs e)
        {
            if (usable)
            {
                base.OnLinkClicked(e);
            }
        }

        private void Recolor()
        {
            if (theme == null)
            {
                return;
            }

            LinkColor = usable ? theme.Accent : theme.Disabled;
            VisitedLinkColor = LinkColor;
            ActiveLinkColor = usable ? theme.AccentHover : theme.Disabled;
            LinkBehavior = usable ? LinkBehavior.HoverUnderline : LinkBehavior.NeverUnderline;
            Cursor = usable ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && usable && Links.Count > 0)
            {
                OnLinkClicked(new LinkLabelLinkClickedEventArgs(Links[0]));
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }
    }

    /// <summary>A progress bar: a track and a fill, two plain panels.</summary>
    internal sealed class ThemedProgress : Panel
    {
        private readonly Panel fill = new Panel();
        private int value;

        public ThemedProgress()
        {
            fill.Dock = DockStyle.Left;
            Controls.Add(fill);
        }

        public int Value
        {
            get
            {
                return value;
            }

            set
            {
                this.value = Math.Max(0, Math.Min(100, value));
                UpdateFill();
            }
        }

        public void SetColors(Color track, Color bar)
        {
            BackColor = track;
            fill.BackColor = bar;
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateFill();
        }

        private void UpdateFill()
        {
            fill.Width = ClientSize.Width * value / 100;
        }
    }
}
