using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VCUT;

internal static class VcutTheme
{
    // Deep Cosmic Obsidian & Amethyst Studio Palette
    internal static readonly Color Canvas = Color.FromArgb(12, 9, 20);
    internal static readonly Color Surface = Color.FromArgb(22, 17, 36);
    internal static readonly Color SurfaceRaised = Color.FromArgb(33, 25, 54);
    internal static readonly Color SurfaceHover = Color.FromArgb(47, 36, 76);
    internal static readonly Color Border = Color.FromArgb(64, 48, 102);
    internal static readonly Color BorderStrong = Color.FromArgb(98, 74, 152);
    internal static readonly Color BorderGlow = Color.FromArgb(150, 255, 77, 121);

    // High-contrast typography
    internal static readonly Color TextPrimary = Color.FromArgb(250, 247, 255);
    internal static readonly Color TextSecondary = Color.FromArgb(182, 170, 212);
    internal static readonly Color TextQuiet = Color.FromArgb(128, 114, 162);

    // Electric Sunset & Aurora Neon Accents
    internal static readonly Color Accent = Color.FromArgb(255, 77, 121);        // Hot Coral Rose
    internal static readonly Color AccentHover = Color.FromArgb(255, 108, 145);
    internal static readonly Color AccentPressed = Color.FromArgb(224, 52, 98);
    internal static readonly Color Amber = Color.FromArgb(255, 158, 68);         // Sunset Amber
    internal static readonly Color Violet = Color.FromArgb(155, 93, 229);        // Electric Violet
    internal static readonly Color Cyan = Color.FromArgb(0, 212, 255);           // Cyber Sky
    internal static readonly Color CyanBright = Color.FromArgb(92, 235, 255);
    internal static readonly Color Purple = Color.FromArgb(192, 112, 255);
    internal static readonly Color Warning = Color.FromArgb(255, 190, 85);
    internal static readonly Color Danger = Color.FromArgb(255, 82, 108);
    internal static readonly Color Success = Color.FromArgb(24, 232, 165);       // Neon Mint-Emerald

    internal static Color Lerp(Color from, Color to, float amount)
    {
        float t = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(from.A + (to.A - from.A) * t),
            (int)(from.R + (to.R - from.R) * t),
            (int)(from.G + (to.G - from.G) * t),
            (int)(from.B + (to.B - from.B) * t));
    }

    internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Max(1.0f, radius * 2.0f);
        if (bounds.Width <= diameter || bounds.Height <= diameter)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static Color ParentBackground(Control control, Color fallback)
    {
        Control current = control.Parent;
        while (current is not null)
        {
            Color candidate = current.BackColor;
            if (candidate.A == 255)
            {
                return candidate;
            }

            current = current.Parent;
        }

        return fallback;
    }
}
