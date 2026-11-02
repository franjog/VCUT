using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VCUT.Core;

namespace VCUT;

public class VideoTimelineControl : Control
{
    private double _position;
    private double _selectionStart;
    private double _selectionEnd = 1.0;
    private double _totalDurationSeconds;
    private bool _dragging;
    private float _hoverX = -1;
    private bool _isHovered;
    private float _pulsePhase;
    private Bitmap _trackCacheBitmap;
    private bool _trackCacheDirty = true;
    private readonly Font _pillFont = new("Cascadia Mono", 7.5f, FontStyle.Bold);
    private readonly Font _badgeFont = new("Cascadia Mono", 6.8f, FontStyle.Bold);
    private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 33 };

    public event Action<double> SeekRequested;
    public event Action<double> ScrubEnded;
    public event Action<double, Point> HoverPositionChanged;
    public event Action HoverEnded;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsDragging => _dragging;

    public VideoTimelineControl()
    {
        DoubleBuffered = true;
        BackColor = VcutTheme.Surface;
        Height = 56;
        MinimumSize = new Size(120, 48);
        TabStop = true;
        AccessibleName = "Línea de tiempo del vídeo";
        Cursor = Cursors.Hand;
        SetStyle(
            ControlStyles.Selectable |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint,
            true);
        _animTimer.Tick += (_, _) =>
        {
            _pulsePhase = (_pulsePhase + 0.08f) % (float)(Math.PI * 2.0);
            Invalidate();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
            _trackCacheBitmap?.Dispose();
            _pillFont.Dispose();
            _badgeFont.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs e)
    {
        _trackCacheDirty = true;
        base.OnResize(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _trackCacheDirty = true;
        base.OnEnabledChanged(e);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Position
    {
        get => _position;
        set
        {
            _position = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double TotalDurationSeconds
    {
        get => _totalDurationSeconds;
        set
        {
            _totalDurationSeconds = Math.Max(0, value);
            Invalidate();
        }
    }

    public void SetSelection(double startFraction, double endFraction)
    {
        double nextStart = Math.Clamp(startFraction, 0, 1);
        double nextEnd = Math.Clamp(endFraction, nextStart, 1);
        if (Math.Abs(_selectionStart - nextStart) > 0.00001 || Math.Abs(_selectionEnd - nextEnd) > 0.00001)
        {
            _selectionStart = nextStart;
            _selectionEnd = nextEnd;
            _trackCacheDirty = true;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _isHovered = true;
        _animTimer.Start();
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _isHovered = false;
        _hoverX = -1;
        _animTimer.Stop();
        HoverEnded?.Invoke();
        Invalidate();
        base.OnMouseLeave(e);
    }

    private void EnsureTrackCache()
    {
        if (Width <= 4 || Height <= 4)
        {
            return;
        }

        if (!_trackCacheDirty && _trackCacheBitmap is not null && _trackCacheBitmap.Width == Width && _trackCacheBitmap.Height == Height)
        {
            return;
        }

        _trackCacheBitmap?.Dispose();
        _trackCacheBitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using Graphics g = Graphics.FromImage(_trackCacheBitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(VcutTheme.ParentBackground(this, VcutTheme.Surface));

        float left = 18.0f;
        float right = Math.Max(left + 10, Width - 18.0f);
        float widthAvailable = right - left;
        float trackHeight = 26.0f;
        float trackY = Height / 2.0f - trackHeight / 2.0f + 4.0f;
        var track = new RectangleF(left, trackY, widthAvailable, trackHeight);

        using (GraphicsPath basePath = VcutTheme.RoundedRectangle(track, 8.0f))
        using (var baseBrush = new LinearGradientBrush(
            track,
            Enabled ? Color.FromArgb(18, 13, 30) : Color.FromArgb(15, 11, 24),
            Enabled ? Color.FromArgb(29, 22, 48) : Color.FromArgb(19, 15, 30),
            LinearGradientMode.Vertical))
        using (var borderPen = new Pen(Color.FromArgb(62, 46, 98), 1.1f))
        {
            g.FillPath(baseBrush, basePath);

            g.SetClip(basePath);
            int tickCount = Math.Max(20, (int)(widthAvailable / 8.0f));
            float step = widthAvailable / tickCount;
            using var tickPenMuted = new Pen(Color.FromArgb(46, 134, 120, 174), 1.4f);
            for (int i = 0; i < tickCount; i++)
            {
                float bx = left + i * step + step * 0.5f;
                float norm = (float)i / tickCount;
                if (norm < _selectionStart || norm > _selectionEnd)
                {
                    bool major = (i % 5) == 0;
                    float tickLen = major ? 6.5f : 3.5f;
                    g.DrawLine(tickPenMuted, bx, trackY + 2.0f, bx, trackY + 2.0f + tickLen);
                    g.DrawLine(tickPenMuted, bx, trackY + trackHeight - 2.0f - tickLen, bx, trackY + trackHeight - 2.0f);
                }
            }

            g.ResetClip();
            g.DrawPath(borderPen, basePath);
        }

        float selectionLeft = left + (float)(_selectionStart * widthAvailable);
        float selectionRight = left + (float)(_selectionEnd * widthAvailable);
        float selectionWidth = Math.Max(4.0f, selectionRight - selectionLeft);
        var selection = new RectangleF(selectionLeft, trackY, selectionWidth, trackHeight);

        using (GraphicsPath selectionPath = VcutTheme.RoundedRectangle(selection, 7.0f))
        using (var selBrush = new LinearGradientBrush(
            selection,
            Color.FromArgb(115, VcutTheme.Violet),
            Color.FromArgb(135, VcutTheme.Amber),
            LinearGradientMode.Horizontal))
        {
            var cb = new ColorBlend(3)
            {
                Colors = new[]
                {
                    Color.FromArgb(120, VcutTheme.Violet),
                    Color.FromArgb(135, VcutTheme.Accent),
                    Color.FromArgb(125, VcutTheme.Amber)
                },
                Positions = new[] { 0.0f, 0.5f, 1.0f }
            };
            selBrush.InterpolationColors = cb;
            g.FillPath(selBrush, selectionPath);

            g.SetClip(selectionPath);
            int tickCount = Math.Max(20, (int)(widthAvailable / 8.0f));
            float step = widthAvailable / tickCount;
            using var tickPenActive = new Pen(Color.FromArgb(170, 255, 240, 250), 1.5f);
            for (int i = 0; i < tickCount; i++)
            {
                float bx = left + i * step + step * 0.5f;
                if (bx < selectionLeft || bx > selectionRight)
                {
                    continue;
                }

                bool major = (i % 5) == 0;
                float tickLen = major ? 7.0f : 4.0f;
                g.DrawLine(tickPenActive, bx, trackY + 2.0f, bx, trackY + 2.0f + tickLen);
                g.DrawLine(tickPenActive, bx, trackY + trackHeight - 2.0f - tickLen, bx, trackY + trackHeight - 2.0f);
            }

            string videoTag = "🎬 VÍDEO";
            Size tagSize = TextRenderer.MeasureText(videoTag, _badgeFont);
            var tagRect = new RectangleF(left + 7.0f, trackY + (trackHeight - 15.0f) / 2.0f, tagSize.Width + 10.0f, 15.0f);
            using (GraphicsPath clipPath = VcutTheme.RoundedRectangle(track, 8.0f))
            using (GraphicsPath tagPath = VcutTheme.RoundedRectangle(tagRect, 4.0f))
            using (var tagBrush = new SolidBrush(Color.FromArgb(190, 16, 12, 30)))
            using (var tagBorder = new Pen(Color.FromArgb(110, VcutTheme.AccentHover), 1.0f))
            {
                g.ResetClip();
                g.SetClip(clipPath);
                g.FillPath(tagBrush, tagPath);
                g.DrawPath(tagBorder, tagPath);
                TextRenderer.DrawText(
                    g,
                    videoTag,
                    _badgeFont,
                    Rectangle.Round(tagRect),
                    VcutTheme.AccentHover,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            g.ResetClip();

            using var selBorder = new Pen(VcutTheme.AccentHover, 1.5f);
            g.DrawPath(selBorder, selectionPath);
        }

        DrawBracketHandle(g, selectionLeft, trackY, trackHeight, isStart: true);
        DrawBracketHandle(g, selectionRight, trackY, trackHeight, isStart: false);
        _trackCacheDirty = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        EnsureTrackCache();
        if (_trackCacheBitmap is not null)
        {
            e.Graphics.DrawImageUnscaled(_trackCacheBitmap, 0, 0);
        }
        else
        {
            e.Graphics.Clear(VcutTheme.ParentBackground(this, VcutTheme.Surface));
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        float left = 18.0f;
        float right = Math.Max(left + 10, Width - 18.0f);
        float widthAvailable = right - left;
        float trackHeight = 26.0f;
        float trackY = Height / 2.0f - trackHeight / 2.0f + 4.0f;

        // Hover Guide Line & Timecode Pill
        if (_isHovered && _hoverX >= left && _hoverX <= right && Enabled)
        {
            using (var hoverPen = new Pen(Color.FromArgb(170, VcutTheme.CyanBright), 1.2f) { DashStyle = DashStyle.Dash })
            {
                e.Graphics.DrawLine(hoverPen, _hoverX, trackY - 6, _hoverX, trackY + trackHeight + 6);
            }

            if (_totalDurationSeconds > 0)
            {
                double hoverFrac = Math.Clamp((_hoverX - left) / widthAvailable, 0, 1);
                string hoverTime = Timecode.Format(TimeSpan.FromSeconds(hoverFrac * _totalDurationSeconds));
                var pillWidth = 82f;
                var pillHeight = 18f;
                float pillX = Math.Clamp(_hoverX - pillWidth / 2f, 2f, Width - pillWidth - 2f);
                var pillRect = new RectangleF(pillX, 1f, pillWidth, pillHeight);
                using GraphicsPath pillPath = VcutTheme.RoundedRectangle(pillRect, 6f);
                using var pillBrush = new SolidBrush(Color.FromArgb(235, 36, 26, 62));
                using var pillPen = new Pen(VcutTheme.Cyan, 1f);
                e.Graphics.FillPath(pillBrush, pillPath);
                e.Graphics.DrawPath(pillPen, pillPath);
                TextRenderer.DrawText(
                    e.Graphics,
                    hoverTime,
                    _pillFont,
                    Rectangle.Round(pillRect),
                    VcutTheme.CyanBright,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        // Playhead Needle + Pulsing Halo
        float playheadX = left + (float)(_position * widthAvailable);
        float centerY = trackY + trackHeight / 2.0f;
        float pulseRadius = 11.0f + 2.5f * (float)Math.Sin(_pulsePhase);

        using (var haloBrush = new SolidBrush(Color.FromArgb(75, VcutTheme.Cyan)))
        {
            e.Graphics.FillEllipse(haloBrush, playheadX - pulseRadius, centerY - pulseRadius, pulseRadius * 2, pulseRadius * 2);
        }

        using (var needlePen = new Pen(Color.White, 2.2f))
        {
            e.Graphics.DrawLine(needlePen, playheadX, trackY - 5, playheadX, trackY + trackHeight + 5);
        }

        // Diamond cap on playhead
        PointF[] diamond =
        {
            new(playheadX, trackY - 9),
            new(playheadX + 6, trackY - 3),
            new(playheadX, trackY + 3),
            new(playheadX - 6, trackY - 3)
        };
        using (var capBrush = new SolidBrush(VcutTheme.CyanBright))
        using (var capPen = new Pen(Color.White, 1.2f))
        {
            e.Graphics.FillPolygon(capBrush, diamond);
            e.Graphics.DrawPolygon(capPen, diamond);
        }
    }

    private static void DrawBracketHandle(Graphics graphics, float x, float top, float height, bool isStart)
    {
        float gripWidth = 6.0f;
        float gx = isStart ? x - 2.0f : x - gripWidth + 2.0f;
        var rect = new RectangleF(gx, top - 2.0f, gripWidth, height + 4.0f);
        using GraphicsPath path = VcutTheme.RoundedRectangle(rect, 3.0f);
        using var brush = new SolidBrush(isStart ? VcutTheme.Accent : VcutTheme.Amber);
        using var pen = new Pen(Color.White, 1.0f);
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        Focus();
        _dragging = true;
        Capture = true;
        SeekAt(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hoverX = e.X;
        if (Enabled)
        {
            float left = 18.0f;
            float right = Math.Max(left + 10, Width - 18.0f);
            float clampedX = Math.Clamp(e.X, left, right);
            double hoverFraction = Math.Clamp((clampedX - left) / (double)(right - left), 0.0, 1.0);
            Point screenAnchor = PointToScreen(new Point((int)Math.Round(clampedX), 0));
            HoverPositionChanged?.Invoke(hoverFraction, screenAnchor);
        }

        if (_dragging && (e.Button & MouseButtons.Left) != 0 && Enabled)
        {
            SeekAt(e.X);
        }
        else
        {
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            if (Enabled)
            {
                SeekAt(e.X);
                ScrubEnded?.Invoke(Position);
            }
        }

        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
        {
            Position += e.KeyCode == Keys.Right ? 0.005 : -0.005;
            SeekRequested?.Invoke(Position);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void SeekAt(int x)
    {
        float left = 18.0f;
        float right = Math.Max(left + 10, Width - 18.0f);
        Position = Math.Clamp((x - left) / (double)(right - left), 0, 1);
        Update();
        SeekRequested?.Invoke(Position);
    }
}
