using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VCUT;

public class RoundedTableLayoutPanel : TableLayoutPanel
{
    public RoundedTableLayoutPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = VcutTheme.Surface;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColorBottom { get; set; } = Color.FromArgb(17, 13, 29);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = VcutTheme.Border;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 16;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowTopAccentBar { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float PhaseOffset { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(VcutTheme.ParentBackground(this, VcutTheme.Canvas));

        var bounds = new RectangleF(1.0f, 1.0f, Math.Max(2, Width - 2), Math.Max(2, Height - 2));
        using GraphicsPath path = VcutTheme.RoundedRectangle(bounds, CornerRadius);

        using (var fillBrush = new LinearGradientBrush(
            new RectangleF(0, 0, Math.Max(2, Width), Math.Max(2, Height)),
            FillColor,
            FillColorBottom,
            LinearGradientMode.Vertical))
        {
            e.Graphics.FillPath(fillBrush, path);
        }

        if (ShowTopAccentBar && Width > 20 && Height > 20)
        {
            e.Graphics.SetClip(path);
            var topBarRect = new RectangleF(bounds.Left, bounds.Top, bounds.Width, 3.0f);
            using (var accentBrush = new LinearGradientBrush(topBarRect, VcutTheme.Violet, VcutTheme.Amber, LinearGradientMode.Horizontal))
            {
                float mid = 0.35f + 0.3f * (float)Math.Sin(PhaseOffset);
                var blend = new ColorBlend(4)
                {
                    Colors = new[] { VcutTheme.Violet, VcutTheme.Accent, VcutTheme.Amber, VcutTheme.Cyan },
                    Positions = new[] { 0.0f, Math.Clamp(mid, 0.15f, 0.65f), 0.78f, 1.0f }
                };
                accentBrush.InterpolationColors = blend;
                e.Graphics.FillRectangle(accentBrush, topBarRect);
            }

            // Subtle ambient top glow
            var ambientRect = new RectangleF(bounds.Left, bounds.Top, bounds.Width, Math.Min(42f, bounds.Height * 0.35f));
            using (var ambientBrush = new LinearGradientBrush(
                ambientRect,
                Color.FromArgb(22, 255, 77, 121),
                Color.FromArgb(0, 22, 17, 36),
                LinearGradientMode.Vertical))
            {
                e.Graphics.FillRectangle(ambientBrush, ambientRect);
            }

            e.Graphics.ResetClip();
        }

        using var pen = new Pen(BorderColor, 1.2f);
        e.Graphics.DrawPath(pen, path);
    }
}
