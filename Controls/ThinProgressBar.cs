using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VCUT;

public class ThinProgressBar : Control
{
    private int _value;
    private float _displayValue;
    private int _maximum = 1000;
    private bool _isIndeterminate;
    private bool _isActivePulse;
    private float _animationPhase;
    private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 20 };

    public ThinProgressBar()
    {
        DoubleBuffered = true;
        Height = 10;
        MinimumSize = new Size(60, 8);
        AccessibleName = "Progreso del recorte";
        _animTimer.Tick += (_, _) =>
        {
            _animationPhase = (_animationPhase + 0.032f) % 1.0f;
            float diff = _value - _displayValue;
            if (Math.Abs(diff) > 0.5f)
            {
                _displayValue += diff * 0.35f;
            }
            else
            {
                _displayValue = _value;
                if (!_isIndeterminate && !_isActivePulse && (_value <= 0 || _value >= _maximum))
                {
                    _animTimer.Stop();
                }
            }

            Invalidate();
        };
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
    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(1, value);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, Math.Max(1, Maximum));
            if (_value == 0)
            {
                _displayValue = 0;
            }

            AccessibleDescription = $"{_value * 100 / Math.Max(1, Maximum)} por ciento";
            _animTimer.Start();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set
        {
            if (_isIndeterminate == value)
            {
                return;
            }

            _isIndeterminate = value;
            if (value)
            {
                _animTimer.Start();
            }

            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsActivePulse
    {
        get => _isActivePulse;
        set
        {
            if (_isActivePulse == value)
            {
                return;
            }

            _isActivePulse = value;
            if (value || _isIndeterminate)
            {
                _animTimer.Start();
            }

            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(VcutTheme.ParentBackground(this, VcutTheme.Surface));

        var bounds = new RectangleF(0, 0, Math.Max(2, Width), Math.Max(2, Height));
        float radius = Height / 2.0f;

        using (GraphicsPath path = VcutTheme.RoundedRectangle(bounds, radius))
        using (var brush = new SolidBrush(Color.FromArgb(30, 22, 48)))
        using (var borderPen = new Pen(Color.FromArgb(56, 42, 88), 1f))
        {
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(borderPen, path);
        }

        if (_isIndeterminate)
        {
            float scanWidth = Width * 0.42f;
            float scanX = (Width + scanWidth) * _animationPhase - scanWidth;
            var scanRect = new RectangleF(scanX, 0, scanWidth, Height);
            if (scanRect.Right > 0 && scanRect.Left < Width)
            {
                using GraphicsPath path = VcutTheme.RoundedRectangle(bounds, radius);
                e.Graphics.SetClip(path);
                using (var glowBrush = new LinearGradientBrush(scanRect, Color.Transparent, VcutTheme.Accent, LinearGradientMode.Horizontal))
                {
                    var cb = new ColorBlend(4)
                    {
                        Colors = new[]
                        {
                            Color.FromArgb(0, VcutTheme.Violet),
                            VcutTheme.Violet,
                            VcutTheme.Accent,
                            Color.FromArgb(0, VcutTheme.Amber)
                        },
                        Positions = new[] { 0.0f, 0.35f, 0.7f, 1.0f }
                    };
                    glowBrush.InterpolationColors = cb;
                    e.Graphics.FillRectangle(glowBrush, scanRect);
                }

                e.Graphics.ResetClip();
            }

            return;
        }

        float effectiveVal = Math.Max(_displayValue, _value > 0 ? 1f : 0f);
        float progressWidth = (float)(Width * (effectiveVal / Math.Max(1.0, Maximum)));
        if (progressWidth < 1)
        {
            return;
        }

        var progress = new RectangleF(0, 0, progressWidth, Height);
        using (GraphicsPath path = VcutTheme.RoundedRectangle(progress, radius))
        using (var brush = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(4, progressWidth), Height), VcutTheme.Violet, VcutTheme.Amber, LinearGradientMode.Horizontal))
        {
            var cb = new ColorBlend(4)
            {
                Colors = new[] { VcutTheme.Violet, VcutTheme.Accent, VcutTheme.Amber, VcutTheme.Success },
                Positions = new[] { 0.0f, 0.45f, 0.82f, 1.0f }
            };
            brush.InterpolationColors = cb;
            e.Graphics.FillPath(brush, path);
        }

        if (progressWidth >= 6)
        {
            var capRect = new RectangleF(Math.Max(0, progressWidth - 8), 0, 8, Height);
            using GraphicsPath capPath = VcutTheme.RoundedRectangle(capRect, radius);
            using var capBrush = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
            e.Graphics.FillPath(capBrush, capPath);
        }
    }
}
