using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VCUT;

public sealed class TimelineThumbnailPopup : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;

    private byte[] _currentJpeg;
    private Bitmap _currentBitmap;
    private string _timecodeText = "00:00:00.000";
    private bool _isExact;

    public TimelineThumbnailPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(196, 140);
        BackColor = VcutTheme.Surface;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        UpdateRoundedRegion();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTransparent;
            return cp;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRoundedRegion();
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using GraphicsPath path = VcutTheme.RoundedRectangle(new RectangleF(0, 0, Width, Height), 12f);
        Region old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    public void ShowAt(IWin32Window owner, Point screenAnchor, string timecodeText, byte[] jpegBytes, bool isExact)
    {
        string nextTimecode = timecodeText ?? "00:00:00.000";
        bool changed = _timecodeText != nextTimecode || _isExact != isExact;
        _timecodeText = nextTimecode;
        _isExact = isExact;

        if (jpegBytes is { Length: > 0 } && !ReferenceEquals(_currentJpeg, jpegBytes))
        {
            _currentJpeg = jpegBytes;
            changed = true;
            try
            {
                int boxW = Math.Max(32, Width - 12);
                int boxH = Math.Max(24, Height - 35);
                using var ms = new MemoryStream(jpegBytes, writable: false);
                using var source = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
                float scale = Math.Min((float)boxW / source.Width, (float)boxH / source.Height);
                int drawW = Math.Max(1, (int)Math.Round(source.Width * scale));
                int drawH = Math.Max(1, (int)Math.Round(source.Height * scale));
                var thumb = new Bitmap(drawW, drawH, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(thumb))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(source, new Rectangle(0, 0, drawW, drawH));
                }

                Bitmap old = _currentBitmap;
                _currentBitmap = thumb;
                old?.Dispose();
            }
            catch
            {
            }
        }

        if (_currentBitmap is null)
        {
            return;
        }

        Rectangle workingArea = Screen.FromPoint(screenAnchor).WorkingArea;
        int targetX = Math.Clamp(screenAnchor.X - Width / 2, workingArea.Left + 6, workingArea.Right - Width - 6);
        int targetY = Math.Max(workingArea.Top + 6, screenAnchor.Y - Height - 8);
        var nextLocation = new Point(targetX, targetY);
        if (Location != nextLocation)
        {
            Location = nextLocation;
        }

        if (!Visible)
        {
            Show(owner);
            changed = true;
        }

        if (changed)
        {
            Invalidate();
        }
    }

    public void HidePopup()
    {
        if (Visible)
        {
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;

        var outer = new RectangleF(0, 0, Width, Height);
        using (var bgBrush = new LinearGradientBrush(outer, VcutTheme.SurfaceRaised, VcutTheme.Canvas, LinearGradientMode.Vertical))
        {
            e.Graphics.FillRectangle(bgBrush, outer);
        }

        // Top 3px Sunset-Aurora bar
        var topBar = new RectangleF(0, 0, Width, 3f);
        using (var topBrush = new LinearGradientBrush(topBar, VcutTheme.Violet, VcutTheme.Amber, LinearGradientMode.Horizontal))
        {
            var cb = new ColorBlend(3)
            {
                Colors = new[] { VcutTheme.Violet, VcutTheme.Accent, VcutTheme.Amber },
                Positions = new[] { 0.0f, 0.5f, 1.0f }
            };
            topBrush.InterpolationColors = cb;
            e.Graphics.FillRectangle(topBrush, topBar);
        }

        var frameRect = new RectangleF(6, 7, Width - 12, Height - 35);
        using (GraphicsPath framePath = VcutTheme.RoundedRectangle(frameRect, 7f))
        {
            using (var frameBg = new SolidBrush(Color.FromArgb(10, 7, 18)))
            {
                e.Graphics.FillPath(frameBg, framePath);
            }

            e.Graphics.SetClip(framePath);
            if (_currentBitmap is not null)
            {
                float drawX = frameRect.X + (frameRect.Width - _currentBitmap.Width) / 2f;
                float drawY = frameRect.Y + (frameRect.Height - _currentBitmap.Height) / 2f;
                e.Graphics.DrawImageUnscaled(_currentBitmap, (int)Math.Round(drawX), (int)Math.Round(drawY));
            }

            e.Graphics.ResetClip();

            using var frameBorder = new Pen( _isExact ? VcutTheme.AccentHover : VcutTheme.BorderStrong, 1.1f);
            e.Graphics.DrawPath(frameBorder, framePath);
        }

        // Timecode footer pill
        var footerRect = new Rectangle(6, Height - 26, Width - 12, 22);
        using var timeFont = new Font("Cascadia Mono", 9.0f, FontStyle.Bold);
        TextRenderer.DrawText(
            e.Graphics,
            "⏱ " + _timecodeText,
            timeFont,
            footerRect,
            VcutTheme.CyanBright,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        // Outer glowing border
        var borderBounds = new RectangleF(1.0f, 1.0f, Width - 2.0f, Height - 2.0f);
        using GraphicsPath borderPath = VcutTheme.RoundedRectangle(borderBounds, 11f);
        using var borderPen = new Pen(VcutTheme.Accent, 1.5f);
        e.Graphics.DrawPath(borderPen, borderPath);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _currentBitmap?.Dispose();
            _currentBitmap = null;
        }

        base.Dispose(disposing);
    }
}

