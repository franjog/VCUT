using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VCUT;

public enum BadgeStyle
{
    Mint,
    Cyan,
    Purple,
    Warning,
    Neutral,
    Coral,
    Amber
}

public class BadgeLabel : Label
{
    private BadgeStyle _style = BadgeStyle.Mint;

    public BadgeLabel()
    {
        AutoSize = false;
        TextAlign = ContentAlignment.MiddleCenter;
        Font = new Font("Segoe UI Variable Text", 8.2f, FontStyle.Bold);
        ForeColor = VcutTheme.Accent;
        BackColor = Color.Transparent;
        Margin = new Padding(4, 3, 4, 3);
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BadgeStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(VcutTheme.ParentBackground(this, VcutTheme.Surface));
        var bounds = new RectangleF(1.0f, 1.0f, Math.Max(2, Width - 2), Math.Max(2, Height - 2));

        Color fillStart;
        Color fillEnd;
        Color border;
        Color textCol;

        switch (_style)
        {
            case BadgeStyle.Cyan:
                fillStart = Color.FromArgb(45, 0, 195, 255);
                fillEnd = Color.FromArgb(25, 95, 70, 220);
                border = Color.FromArgb(135, 0, 212, 255);
                textCol = VcutTheme.CyanBright;
                break;
            case BadgeStyle.Purple:
                fillStart = Color.FromArgb(52, 165, 85, 247);
                fillEnd = Color.FromArgb(30, 255, 77, 121);
                border = Color.FromArgb(140, 192, 112, 255);
                textCol = Color.FromArgb(235, 205, 255);
                break;
            case BadgeStyle.Coral:
                fillStart = Color.FromArgb(55, 255, 77, 121);
                fillEnd = Color.FromArgb(35, 255, 158, 68);
                border = Color.FromArgb(150, 255, 108, 145);
                textCol = Color.FromArgb(255, 210, 224);
                break;
            case BadgeStyle.Amber:
                fillStart = Color.FromArgb(50, 255, 158, 68);
                fillEnd = Color.FromArgb(30, 255, 77, 121);
                border = Color.FromArgb(145, 255, 175, 95);
                textCol = Color.FromArgb(255, 225, 180);
                break;
            case BadgeStyle.Warning:
                fillStart = Color.FromArgb(48, 255, 190, 85);
                fillEnd = Color.FromArgb(25, 180, 90, 30);
                border = Color.FromArgb(140, 255, 190, 85);
                textCol = VcutTheme.Warning;
                break;
            case BadgeStyle.Neutral:
                fillStart = Color.FromArgb(38, 30, 58);
                fillEnd = Color.FromArgb(28, 22, 44);
                border = Color.FromArgb(85, 68, 126);
                textCol = VcutTheme.TextSecondary;
                break;
            default: // Mint / Emerald
                fillStart = Color.FromArgb(48, 24, 232, 165);
                fillEnd = Color.FromArgb(25, 0, 190, 215);
                border = Color.FromArgb(140, 24, 232, 165);
                textCol = VcutTheme.Success;
                break;
        }

        float radius = Math.Min(11.0f, bounds.Height / 2.0f);
        using (GraphicsPath path = VcutTheme.RoundedRectangle(bounds, radius))
        using (var fillBrush = new LinearGradientBrush(bounds, fillStart, fillEnd, LinearGradientMode.Horizontal))
        using (var borderPen = new Pen(border, 1.1f))
        {
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            ClientRectangle,
            textCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
