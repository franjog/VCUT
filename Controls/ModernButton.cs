using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VCUT;

public enum ModernButtonStyle
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

public class ModernButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private float _hoverAmount;
    private float _pressAmount;
    private float _shimmerPhase;
    private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 16 };
    private ModernButtonStyle _buttonStyle = ModernButtonStyle.Secondary;

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI Variable Text", 9.5f, FontStyle.Bold);
        Height = 42;
        TabStop = true;
        _animTimer.Tick += OnAnimationTick;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernButtonStyle ButtonStyle
    {
        get => _buttonStyle;
        set
        {
            _buttonStyle = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 11;

    private void OnAnimationTick(object sender, EventArgs e)
    {
        float targetHover = _hovered && Enabled ? 1.0f : 0.0f;
        float targetPress = _pressed && Enabled ? 1.0f : 0.0f;

        _hoverAmount += (targetHover - _hoverAmount) * 0.28f;
        _pressAmount += (targetPress - _pressAmount) * 0.35f;
        if (_hovered && Enabled)
        {
            _shimmerPhase = (_shimmerPhase + 0.04f) % 1.0f;
        }

        if (Math.Abs(targetHover - _hoverAmount) < 0.01f && Math.Abs(targetPress - _pressAmount) < 0.01f && !_hovered)
        {
            _hoverAmount = targetHover;
            _pressAmount = targetPress;
            _animTimer.Stop();
        }

        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        _animTimer.Start();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        _animTimer.Start();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            _animTimer.Start();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        _animTimer.Start();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        if (!Enabled)
        {
            _hovered = false;
            _pressed = false;
            _hoverAmount = 0f;
            _pressAmount = 0f;
            _animTimer.Stop();
        }

        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        pevent.Graphics.Clear(VcutTheme.ParentBackground(this, VcutTheme.Surface));

        float yOffset = -1.2f * _hoverAmount + 1.8f * _pressAmount;
        var bounds = new RectangleF(1.5f, 1.5f + yOffset, Math.Max(2, Width - 3), Math.Max(2, Height - 4));

        if (Enabled && _hoverAmount > 0.02f && ButtonStyle == ModernButtonStyle.Primary)
        {
            var glowBounds = new RectangleF(0.5f, 0.5f, Math.Max(2, Width - 1), Math.Max(2, Height - 1));
            using GraphicsPath glowPath = VcutTheme.RoundedRectangle(glowBounds, CornerRadius + 2);
            using var glowPen = new Pen(Color.FromArgb((int)(95 * _hoverAmount), VcutTheme.AccentHover), 2.5f);
            pevent.Graphics.DrawPath(glowPen, glowPath);
        }

        using (GraphicsPath path = VcutTheme.RoundedRectangle(bounds, CornerRadius))
        {
            if (!Enabled)
            {
                using var disabledBrush = new SolidBrush(Color.FromArgb(24, 19, 38));
                using var disabledPen = new Pen(Color.FromArgb(46, 36, 72), 1f);
                pevent.Graphics.FillPath(disabledBrush, path);
                pevent.Graphics.DrawPath(disabledPen, path);
            }
            else if (ButtonStyle == ModernButtonStyle.Primary)
            {
                Color c1 = VcutTheme.Lerp(VcutTheme.Violet, VcutTheme.Accent, 0.25f + 0.25f * _hoverAmount);
                Color c2 = VcutTheme.Lerp(VcutTheme.Accent, VcutTheme.AccentHover, _hoverAmount);
                Color c3 = VcutTheme.Lerp(VcutTheme.Amber, Color.FromArgb(255, 195, 95), _hoverAmount);

                if (_pressAmount > 0.1f)
                {
                    c1 = VcutTheme.Lerp(c1, Color.Black, 0.2f * _pressAmount);
                    c2 = VcutTheme.Lerp(c2, Color.Black, 0.2f * _pressAmount);
                    c3 = VcutTheme.Lerp(c3, Color.Black, 0.2f * _pressAmount);
                }

                using (var grad = new LinearGradientBrush(bounds, c1, c3, 18f + 25f * _hoverAmount))
                {
                    var blend = new ColorBlend(3)
                    {
                        Colors = new[] { c1, c2, c3 },
                        Positions = new[] { 0.0f, 0.52f, 1.0f }
                    };
                    grad.InterpolationColors = blend;
                    pevent.Graphics.FillPath(grad, path);
                }

                // Subtle top glass sheen
                pevent.Graphics.SetClip(path);
                var sheenRect = new RectangleF(bounds.Left, bounds.Top, bounds.Width, bounds.Height * 0.48f);
                using (var sheen = new LinearGradientBrush(
                    sheenRect,
                    Color.FromArgb((int)(42 + 28 * _hoverAmount), 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical))
                {
                    pevent.Graphics.FillRectangle(sheen, sheenRect);
                }

                pevent.Graphics.ResetClip();

                Color borderCol = VcutTheme.Lerp(Color.FromArgb(140, 255, 160, 190), Color.White, _hoverAmount);
                using var borderPen = new Pen(borderCol, 1.2f);
                pevent.Graphics.DrawPath(borderPen, path);
            }
            else if (ButtonStyle == ModernButtonStyle.Danger)
            {
                Color fill = VcutTheme.Lerp(Color.FromArgb(62, 22, 42), Color.FromArgb(135, 34, 64), _hoverAmount);
                Color border = VcutTheme.Lerp(Color.FromArgb(150, 255, 82, 108), VcutTheme.Danger, _hoverAmount);
                using var brush = new SolidBrush(fill);
                using var pen = new Pen(border, 1.2f);
                pevent.Graphics.FillPath(brush, path);
                pevent.Graphics.DrawPath(pen, path);
            }
            else if (ButtonStyle == ModernButtonStyle.Ghost)
            {
                Color fill = VcutTheme.Lerp(Color.FromArgb(18, 255, 255, 255), Color.FromArgb(52, 155, 93, 229), _hoverAmount);
                Color border = VcutTheme.Lerp(Color.FromArgb(40, 98, 74, 152), VcutTheme.Violet, _hoverAmount);
                using var brush = new SolidBrush(fill);
                using var pen = new Pen(border, 1f);
                pevent.Graphics.FillPath(brush, path);
                pevent.Graphics.DrawPath(pen, path);
            }
            else
            {
                Color fillTop = VcutTheme.Lerp(VcutTheme.SurfaceRaised, VcutTheme.SurfaceHover, _hoverAmount);
                Color fillBot = VcutTheme.Lerp(Color.FromArgb(26, 20, 44), VcutTheme.SurfaceRaised, _hoverAmount);
                Color border = VcutTheme.Lerp(VcutTheme.Border, VcutTheme.Accent, _hoverAmount * 0.85f);
                using var brush = new LinearGradientBrush(bounds, fillTop, fillBot, LinearGradientMode.Vertical);
                using var pen = new Pen(border, 1.15f);
                pevent.Graphics.FillPath(brush, path);
                pevent.Graphics.DrawPath(pen, path);
            }
        }

        var textRect = new Rectangle(0, (int)Math.Round(yOffset), Width, Height);
        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            textRect,
            ResolveTextColor(),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            Rectangle focusBounds = Rectangle.Inflate(ClientRectangle, -4, -4);
            ControlPaint.DrawFocusRectangle(pevent.Graphics, focusBounds, ResolveTextColor(), Color.Transparent);
        }
    }

    private Color ResolveTextColor()
    {
        if (!Enabled)
        {
            return Color.FromArgb(98, 88, 128);
        }

        if (ButtonStyle == ModernButtonStyle.Primary)
        {
            return Color.White;
        }

        if (ButtonStyle == ModernButtonStyle.Danger)
        {
            return Color.FromArgb(255, 210, 220);
        }

        return VcutTheme.Lerp(VcutTheme.TextPrimary, Color.White, _hoverAmount);
    }
}
