using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VCUT.Core;

namespace VCUT;

public class AudioEqualizerControl : Control
{
    private float[] _levels = Array.Empty<float>();
    private double _position;
    private double _selectionStart;
    private double _selectionEnd = 1.0;
    private double _totalDurationSeconds;
    private double _externalHoverFraction = -1.0;
    private float _localHoverX = -1.0f;
    private bool _isLoading;
    private bool _hasAudioTrack = true;
    private Bitmap _trackCacheBitmap;
    private bool _trackCacheDirty = true;
    private readonly Font _badgeFont = new("Cascadia Mono", 6.8f, FontStyle.Bold);
    private readonly Font _pillFont = new("Cascadia Mono", 7.2f, FontStyle.Bold);

    public AudioEqualizerControl()
    {
        DoubleBuffered = true;
        BackColor = VcutTheme.Surface;
        Height = 46;
        MinimumSize = new Size(120, 38);
        TabStop = false;
        AccessibleName = "Línea de tiempo de sonido (ecualizador informativo)";
        Cursor = Cursors.Default;
        SetStyle(
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trackCacheBitmap?.Dispose();
            _badgeFont.Dispose();
            _pillFont.Dispose();
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
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_position - clamped) > 0.0001)
            {
                _position = clamped;
                Invalidate();
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double TotalDurationSeconds
    {
        get => _totalDurationSeconds;
        set
        {
            _totalDurationSeconds = Math.Max(0.0, value);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ExternalHoverFraction
    {
        get => _externalHoverFraction;
        set
        {
            if (Math.Abs(_externalHoverFraction - value) > 0.0002)
            {
                _externalHoverFraction = value;
                Invalidate();
            }
        }
    }

    public void SetSelection(double startFraction, double endFraction)
    {
        double nextStart = Math.Clamp(startFraction, 0.0, 1.0);
        double nextEnd = Math.Clamp(endFraction, nextStart, 1.0);
        if (Math.Abs(_selectionStart - nextStart) > 0.00001 || Math.Abs(_selectionEnd - nextEnd) > 0.00001)
        {
            _selectionStart = nextStart;
            _selectionEnd = nextEnd;
            _trackCacheDirty = true;
            Invalidate();
        }
    }

    public void ResetState(bool isLoading, bool hasAudioTrack)
    {
        _levels = Array.Empty<float>();
        _isLoading = isLoading;
        _hasAudioTrack = hasAudioTrack;
        _trackCacheDirty = true;
        Invalidate();
    }

    public void SetAudioLevels(float[] levels, bool isLoading = false, bool hasAudioTrack = true)
    {
        _levels = levels ?? Array.Empty<float>();
        _isLoading = isLoading;
        _hasAudioTrack = hasAudioTrack;
        _trackCacheDirty = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _localHoverX = e.X;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _localHoverX = -1.0f;
        Invalidate();
        base.OnMouseLeave(e);
    }

    private float SampleLevelAt(double fraction)
    {
        float[] levels = _levels;
        if (levels is null || levels.Length == 0)
        {
            return 0.0f;
        }

        double scaled = Math.Clamp(fraction, 0.0, 0.999999) * levels.Length;
        int idx = Math.Clamp((int)scaled, 0, levels.Length - 1);
        return Math.Clamp(levels[idx], 0.0f, 1.0f);
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
        float right = Math.Max(left + 10.0f, Width - 18.0f);
        float widthAvailable = right - left;
        float trackY = 3.0f;
        float trackHeight = Math.Max(24.0f, Height - 6.0f);
        var trackRect = new RectangleF(left, trackY, widthAvailable, trackHeight);

        using (GraphicsPath basePath = VcutTheme.RoundedRectangle(trackRect, 8.0f))
        using (var baseBrush = new LinearGradientBrush(
            trackRect,
            Color.FromArgb(12, 9, 22),
            Color.FromArgb(22, 16, 38),
            LinearGradientMode.Vertical))
        using (var borderPen = new Pen(Color.FromArgb(56, 42, 90), 1.0f))
        {
            g.FillPath(baseBrush, basePath);

            g.SetClip(basePath);

            // Subtle horizontal studio dB reference lines
            using (var gridPen = new Pen(Color.FromArgb(24, 148, 130, 196), 1.0f) { DashStyle = DashStyle.Dot })
            {
                float y25 = trackY + trackHeight * 0.28f;
                float y55 = trackY + trackHeight * 0.56f;
                float y82 = trackY + trackHeight * 0.82f;
                g.DrawLine(gridPen, left + 4, y25, right - 4, y25);
                g.DrawLine(gridPen, left + 4, y55, right - 4, y55);
                g.DrawLine(gridPen, left + 4, y82, right - 4, y82);
            }

            // Selection highlight background inside equalizer
            float selectionLeft = left + (float)(_selectionStart * widthAvailable);
            float selectionRight = left + (float)(_selectionEnd * widthAvailable);
            float selectionWidth = Math.Max(2.0f, selectionRight - selectionLeft);
            var selRect = new RectangleF(selectionLeft, trackY, selectionWidth, trackHeight);
            using (var selBgBrush = new SolidBrush(Color.FromArgb(26, VcutTheme.Cyan)))
            {
                g.FillRectangle(selBgBrush, selRect);
            }

            // Equalizer LED bars
            DrawEqualizerBars(g, left, widthAvailable, trackY, trackHeight);

            // Selection boundary lines
            using (var selEdgePen = new Pen(Color.FromArgb(125, VcutTheme.CyanBright), 1.1f))
            {
                g.DrawLine(selEdgePen, selectionLeft, trackY + 1, selectionLeft, trackY + trackHeight - 1);
                g.DrawLine(selEdgePen, selectionRight, trackY + 1, selectionRight, trackY + trackHeight - 1);
            }

            // Studio label badge in top-left corner of the audio track
            string statusTag = !_hasAudioTrack
                ? "🔇 SIN AUDIO"
                : (_isLoading && _levels.Length == 0 ? "🔊 ANALIZANDO SONIDO…" : "🔊 AUDIO · ECUALIZADOR");
            Size textSize = TextRenderer.MeasureText(statusTag, _badgeFont);
            var tagRect = new RectangleF(left + 7.0f, trackY + 3.5f, textSize.Width + 10.0f, 15.0f);
            using (GraphicsPath tagPath = VcutTheme.RoundedRectangle(tagRect, 4.0f))
            using (var tagBrush = new SolidBrush(Color.FromArgb(195, 16, 12, 30)))
            using (var tagBorder = new Pen(Color.FromArgb(90, VcutTheme.Success), 1.0f))
            {
                g.FillPath(tagBrush, tagPath);
                g.DrawPath(tagBorder, tagPath);
                TextRenderer.DrawText(
                    g,
                    statusTag,
                    _badgeFont,
                    Rectangle.Round(tagRect),
                    _hasAudioTrack ? VcutTheme.Success : VcutTheme.Warning,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            g.ResetClip();
            g.DrawPath(borderPen, basePath);
        }

        _trackCacheDirty = false;
    }

    private void DrawEqualizerBars(Graphics g, float left, float widthAvailable, float trackY, float trackHeight)
    {
        float step = 4.5f;
        float barWidth = 3.0f;
        int barCount = Math.Max(24, (int)(widthAvailable / step));
        step = widthAvailable / barCount;
        barWidth = Math.Clamp(step - 1.4f, 1.8f, 4.0f);

        float bottomY = trackY + trackHeight - 3.0f;
        float maxBarHeight = Math.Max(10.0f, trackHeight - 7.0f);
        int totalSegments = Math.Max(5, (int)(maxBarHeight / 3.2f));
        float segStep = maxBarHeight / totalSegments;
        float segHeight = Math.Max(1.6f, segStep - 1.0f);

        using var silenceBrush = new SolidBrush(Color.FromArgb(52, 110, 98, 148));
        using var lowActive = new SolidBrush(Color.FromArgb(230, VcutTheme.Success));
        using var midActive = new SolidBrush(Color.FromArgb(235, VcutTheme.CyanBright));
        using var highActive = new SolidBrush(Color.FromArgb(240, VcutTheme.Amber));
        using var peakActive = new SolidBrush(Color.FromArgb(245, VcutTheme.Accent));
        using var capActive = new SolidBrush(Color.FromArgb(250, 255, 245, 252));

        using var lowMuted = new SolidBrush(Color.FromArgb(72, VcutTheme.Success));
        using var midMuted = new SolidBrush(Color.FromArgb(75, VcutTheme.CyanBright));
        using var highMuted = new SolidBrush(Color.FromArgb(80, VcutTheme.Amber));
        using var peakMuted = new SolidBrush(Color.FromArgb(85, VcutTheme.Accent));

        SmoothingMode prevMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;

        for (int i = 0; i < barCount; i++)
        {
            double norm = (i + 0.5) / barCount;
            float bx = left + i * step + (step - barWidth) * 0.5f;
            float level = SampleLevelAt(norm);
            bool inSelection = norm >= _selectionStart && norm <= _selectionEnd;

            // Silence floor indicator
            if (level <= 0.025f)
            {
                g.FillRectangle(silenceBrush, bx, bottomY - 1.5f, barWidth, 1.5f);
                continue;
            }

            int litSegments = Math.Clamp((int)Math.Ceiling(level * totalSegments), 1, totalSegments);
            for (int s = 0; s < litSegments; s++)
            {
                float segRatio = (s + 1.0f) / totalSegments;
                float sy = bottomY - (s + 1) * segStep + (segStep - segHeight);

                Brush segBrush;
                if (segRatio <= 0.50f)
                {
                    segBrush = inSelection ? lowActive : lowMuted;
                }
                else if (segRatio <= 0.78f)
                {
                    segBrush = inSelection ? midActive : midMuted;
                }
                else if (segRatio <= 0.92f)
                {
                    segBrush = inSelection ? highActive : highMuted;
                }
                else
                {
                    segBrush = inSelection ? peakActive : peakMuted;
                }

                g.FillRectangle(segBrush, bx, sy, barWidth, segHeight);
            }

            if (inSelection && litSegments >= 2)
            {
                float capY = bottomY - litSegments * segStep - 1.2f;
                if (capY >= trackY + 2.0f)
                {
                    g.FillRectangle(capActive, bx, capY, barWidth, 1.3f);
                }
            }
        }

        g.SmoothingMode = prevMode;
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
        float right = Math.Max(left + 10.0f, Width - 18.0f);
        float widthAvailable = right - left;
        float trackY = 3.0f;
        float trackHeight = Math.Max(24.0f, Height - 6.0f);

        // Hover guide (either synchronized from video timeline or local mouse hover)
        double hoverFraction = -1.0;
        bool showLocalInfoPill = false;
        if (_localHoverX >= left && _localHoverX <= right && _totalDurationSeconds > 0)
        {
            hoverFraction = Math.Clamp((_localHoverX - left) / widthAvailable, 0.0, 1.0);
            showLocalInfoPill = true;
        }
        else if (_externalHoverFraction >= 0.0 && _externalHoverFraction <= 1.0)
        {
            hoverFraction = _externalHoverFraction;
        }

        if (hoverFraction >= 0.0 && _totalDurationSeconds > 0)
        {
            float hx = left + (float)(hoverFraction * widthAvailable);
            using (var hoverPen = new Pen(Color.FromArgb(145, VcutTheme.CyanBright), 1.1f) { DashStyle = DashStyle.Dash })
            {
                e.Graphics.DrawLine(hoverPen, hx, trackY + 1, hx, trackY + trackHeight - 1);
            }

            if (showLocalInfoPill && _levels.Length > 0)
            {
                float level = SampleLevelAt(hoverFraction);
                bool isSilent = level <= 0.025f;
                int pct = (int)Math.Round(level * 100.0f);
                string timeText = Timecode.Format(TimeSpan.FromSeconds(hoverFraction * _totalDurationSeconds));
                string stateText = isSilent ? $"{timeText} · SILENCIO" : $"{timeText} · SONIDO {pct}%";

                Size pillTextSize = TextRenderer.MeasureText(stateText, _pillFont);
                float pillW = pillTextSize.Width + 14.0f;
                float pillH = 17.0f;
                float pillX = Math.Clamp(hx - pillW / 2.0f, left + 4.0f, right - pillW - 4.0f);
                var pillRect = new RectangleF(pillX, trackY + 3.0f, pillW, pillH);

                using GraphicsPath pillPath = VcutTheme.RoundedRectangle(pillRect, 5.0f);
                using var pillBg = new SolidBrush(Color.FromArgb(235, 22, 16, 40));
                using var pillBorder = new Pen(isSilent ? VcutTheme.TextSecondary : VcutTheme.Success, 1.0f);
                e.Graphics.FillPath(pillBg, pillPath);
                e.Graphics.DrawPath(pillBorder, pillPath);
                TextRenderer.DrawText(
                    e.Graphics,
                    stateText,
                    _pillFont,
                    Rectangle.Round(pillRect),
                    isSilent ? VcutTheme.TextSecondary : VcutTheme.Success,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        // Synchronized Playhead Line
        float playheadX = left + (float)(_position * widthAvailable);
        using (var glowPen = new Pen(Color.FromArgb(85, VcutTheme.Success), 4.0f))
        using (var needlePen = new Pen(Color.White, 1.8f))
        {
            e.Graphics.DrawLine(glowPen, playheadX, trackY + 1, playheadX, trackY + trackHeight - 1);
            e.Graphics.DrawLine(needlePen, playheadX, trackY + 1, playheadX, trackY + trackHeight - 1);
        }
    }
}

