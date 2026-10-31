using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using VCUT.Core;

namespace VCUT;

public partial class principal
{
    private static readonly Color PageColor = VcutTheme.Canvas;
    private static readonly Color CardColor = VcutTheme.Surface;
    private static readonly Color AccentColor = VcutTheme.Accent;
    private readonly VideoService _service = new(Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg"));
    private readonly VideoThumbnailService _thumbnailService = new(Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg"));
    private readonly TimelineThumbnailPopup _thumbnailPopup = new();
    private readonly VideoRamCache _ramCache = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 50 };
    private readonly Stopwatch _watch = new();
    private readonly IReadOnlyList<GpuInfo> _gpus = GpuDetector.Detect();
    private readonly ModernButton _openButton = MakeButton("📂  Abrir vídeo", ModernButtonStyle.Primary);
    private readonly ModernButton _playButton = MakeButton("▶  Reproducir", ModernButtonStyle.Primary);
    private readonly ModernButton _jumpBackButton = MakeButton("⏪  − 5 s", ModernButtonStyle.Ghost);
    private readonly ModernButton _jumpForwardButton = MakeButton("+ 5 s  ⏩", ModernButtonStyle.Ghost);
    private readonly ModernButton _cutButton = MakeButton("✂  EXPORTAR RECORTE SIN PÉRDIDA", ModernButtonStyle.Primary);
    private readonly ModernButton _cancelButton = MakeButton("✖  Cancelar", ModernButtonStyle.Danger);
    private readonly ModernButton _folderButton = MakeButton("📁  Mostrar archivo", ModernButtonStyle.Secondary);
    private readonly ModernButton _markStartButton = MakeButton("[ I ]  Fijar inicio", ModernButtonStyle.Secondary);
    private readonly ModernButton _markEndButton = MakeButton("Fijar fin  [ O ]", ModernButtonStyle.Secondary);
    private readonly ModernButton _previewButton = MakeButton("🎬  Previsualizar intervalo seleccionado", ModernButtonStyle.Secondary);
    private readonly Label _fileName = MakeLabel("Arrastra un vídeo al estudio o pulsa Abrir vídeo", 11.2f, true);
    private readonly FlowLayoutPanel _metaContainer = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false, Margin = new Padding(0), Padding = new Padding(0) };
    private readonly BadgeLabel _badgeFormat = MakeStyledBadge("SIN VÍDEO", BadgeStyle.Neutral);
    private readonly BadgeLabel _badgeRes = MakeStyledBadge("—", BadgeStyle.Cyan);
    private readonly BadgeLabel _badgeCodec = MakeStyledBadge("—", BadgeStyle.Purple);
    private readonly BadgeLabel _badgeDuration = MakeStyledBadge("—", BadgeStyle.Amber);
    private readonly BadgeLabel _badgeHardware = MakeStyledBadge("RAM + GPU", BadgeStyle.Coral);
    private readonly BadgeLabel _badgePreviewState = MakeStyledBadge("EN ESPERA", BadgeStyle.Neutral);
    private readonly BadgeLabel _badgeClipShare = MakeStyledBadge("100% DEL VÍDEO", BadgeStyle.Coral);
    private readonly Label _positionLabel = MakeLabel("00:00:00.000  /  00:00:00.000", 10.5f, true, false);
    private readonly Label _selectionLabel = MakeLabel("00:00:00.000", 15.0f, true);
    private readonly Label _outputLabel = MakeLabel("Destino: carpeta del archivo original", 9.0f, false, true);
    private readonly Label _statusLabel = MakeLabel("●  Estudio listo · Carga en RAM y GPU activadas", 10.0f);
    private readonly Label _elapsedLabel = MakeLabel("Tiempo  ·  00:00:00", 8.5f, false, true);
    private readonly Label _remainingLabel = MakeLabel("Estado  ·  Listo", 8.5f, false, true);
    private readonly TextBox _startBox = MakeTimeBox("Marca de inicio");
    private readonly TextBox _endBox = MakeTimeBox("Marca de fin");
    private readonly CheckBox _ramGpuCheckBox = new()
    {
        Text = "⚡ Precargar en RAM y acelerar con GPU (activo)",
        Checked = true,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Variable Text", 9.2f, FontStyle.Bold),
        ForeColor = VcutTheme.AccentHover,
        BackColor = Color.Transparent,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        Padding = new Padding(4, 0, 0, 0),
        AccessibleName = "Cargar vídeo en RAM y acelerar con GPU"
    };
    private readonly CheckBox _mkvCheckBox = new()
    {
        Text = "📦 Guardar como MKV (remultiplexado sin recodificar)",
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Variable Text", 9.2f),
        ForeColor = VcutTheme.TextPrimary,
        BackColor = Color.Transparent,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        Padding = new Padding(4, 0, 0, 0),
        AccessibleName = "Guardar como MKV"
    };
    private readonly VideoTimelineControl _timeline = new() { Dock = DockStyle.Fill, Enabled = false };
    private readonly ThinProgressBar _progress = new() { Dock = DockStyle.Fill, Maximum = 1000 };
    private readonly VideoView _videoView = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private readonly HighQualityPictureBox _monitorFrameBox = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(8, 5, 14),
        SizeMode = PictureBoxSizeMode.Zoom,
        Visible = false,
        Cursor = Cursors.Hand
    };
    private readonly Label _placeholder = MakeLabel("SUELTA TU VÍDEO AQUÍ O PULSA ABRIR VÍDEO\r\nMP4 · MKV · MOV · AVI · WebM · TS · ProRes · Copia directa sin pérdida", 10.5f, true, true);
    private readonly TableLayoutPanel _body = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    private readonly RoundedTableLayoutPanel _previewCard = new() { Dock = DockStyle.Fill, FillColor = CardColor, BorderColor = VcutTheme.Border, CornerRadius = 18, Padding = new Padding(16), Margin = new Padding(0, 0, 14, 0) };
    private readonly RoundedTableLayoutPanel _marksCard = new() { Dock = DockStyle.Fill, FillColor = CardColor, BorderColor = VcutTheme.Border, CornerRadius = 18, Padding = new Padding(18), Margin = new Padding(0) };
    private readonly RoundedTableLayoutPanel _fileCard = new() { Dock = DockStyle.Fill, FillColor = VcutTheme.Surface, BorderColor = VcutTheme.Border, CornerRadius = 16, Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0, 0, 0, 10) };
    private readonly RoundedTableLayoutPanel _statusCard = new() { Dock = DockStyle.Fill, FillColor = VcutTheme.Surface, BorderColor = VcutTheme.Border, CornerRadius = 16, Padding = new Padding(16, 10, 16, 10), Margin = new Padding(0, 10, 0, 0) };
    private readonly RoundedTableLayoutPanel _heroSelectionCard = new() { Dock = DockStyle.Fill, FillColor = VcutTheme.SurfaceRaised, FillColorBottom = Color.FromArgb(24, 18, 42), BorderColor = VcutTheme.BorderStrong, CornerRadius = 14, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0, 2, 0, 6), ShowTopAccentBar = false };
    private readonly Label _brandSubtitle = MakeLabel("STUDIO  ·  DIRECT STREAM ENGINE", 8.0f, true, true);
    private readonly Image _brandImage = LoadBrandImage();
    private readonly ToolTip _toolTip = new();
    private ColumnStyle _headerBrandStyle;
    private Panel _emptyStatePanel;
    private float _ambientPhase;
    private LibVLC _libVlc;
    private MediaPlayer _player;
    private Media _media;
    private VideoInfo _video;
    private CancellationTokenSource _operation;
    private CancellationTokenSource _ramPreloadCts;
    private const int MaxDecodedBitmapsInRam = 200;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<byte[], Bitmap> _decodedBitmapByJpeg = new(ReferenceEqualityComparer.Instance);
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _decodedBitmapOrder = new();
    private CancellationTokenSource _thumbPrewarmCts;
    private CancellationTokenSource _thumbHoverCts;
    private CancellationTokenSource _monitorFrameCts;
    private Task _monitorFrameWorkerTask;
    private Task _hoverFrameWorkerTask;
    private double _desiredMonitorFraction = -1;
    private double _desiredHoverFraction = -1;
    private volatile bool _wantHighResMonitorFrame;
    private int _renderedMonitorBucket = -1;
    private int _lastLookaheadCenterBucket = -1000;
    private Size _lastMonitorTargetSize;
    private byte[] _lastMonitorJpeg;
    private long _currentPositionMs;
    private Point _lastHoverScreenAnchor;
    private double _lastHoverFraction = -1;
    private CutProgress _lastProgress;
    private string _lastOutput;
    private bool _busy;
    private bool _cutting;
    private bool _narrow;
    private bool _closeAfterOperation;
    private bool _previewSelection;
    private bool _atEnd;
    private long? _pendingSeek;
    private Task _playerPreloadTask;

    public principal()
    {
        InitializeComponent();
        BuildInterface();
        UpdateHardwareBadge(null);
        StartPlayerPreload();
        _openButton.Click += OpenClicked;
        _playButton.Click += (_, _) => TogglePlayback();
        _monitorFrameBox.Click += (_, _) => TogglePlayback();
        _jumpBackButton.Click += (_, _) => SeekRelative(-5);
        _jumpForwardButton.Click += (_, _) => SeekRelative(5);
        _markStartButton.Click += (_, _) => MarkPosition(true);
        _markEndButton.Click += (_, _) => MarkPosition(false);
        _startBox.TextChanged += (_, _) => UpdateSelection();
        _endBox.TextChanged += (_, _) => UpdateSelection();
        _ramGpuCheckBox.CheckedChanged += RamGpuModeChanged;
        _mkvCheckBox.CheckedChanged += OutputContainerChanged;
        _cutButton.Click += CutClicked;
        _cancelButton.Click += (_, _) => CancelOperation();
        _folderButton.Click += (_, _) => RevealOutput();
        _previewButton.Click += (_, _) => PreviewSelection();
        _timeline.SeekRequested += SeekTo;
        _timeline.ScrubEnded += OnTimelineScrubEnded;
        _timeline.HoverPositionChanged += OnTimelineHoverChanged;
        _timeline.HoverEnded += OnTimelineHoverEnded;
        _clock.Tick += TickClock;
        Resize += (_, _) =>
        {
            _thumbnailPopup.HidePopup();
            AdaptLayout();
        };
        LocationChanged += (_, _) => _thumbnailPopup.HidePopup();
        Deactivate += (_, _) => _thumbnailPopup.HidePopup();
        FormClosing += ClosingForm;
        Disposed += (_, _) => ReleaseResources();
        DragEnter += OnVideoDragEnter;
        DragOver += OnVideoDragOver;
        DragLeave += OnVideoDragLeave;
        DragDrop += OnVideoDrop;
        KeyDown += HandleShortcuts;
        _clock.Start();
        SetBusy(false);
        AdaptLayout();
        AppLogger.Info("INTERFAZ", $"Formulario principal creado. DPI={DeviceDpi}; tamaño={ClientSize.Width}x{ClientSize.Height}.");
    }

    private static string[] BuildLibVlcOptions(bool enableGpu, string vlcDirectory)
    {
        var options = new List<string>
        {
            "--no-video-title-show",
            "--no-snapshot-preview",
            "--no-osd",
            "--no-stats",
            "--no-sub-autodetect-file",
            "--file-caching=100"
        };

        if (!string.IsNullOrWhiteSpace(vlcDirectory))
        {
            string cacheFile = Path.Combine(vlcDirectory, "plugins", "plugins.dat");
            if (!File.Exists(cacheFile))
            {
                options.Add("--reset-plugins-cache");
            }
        }

        if (enableGpu)
        {
            options.Add("--avcodec-hw=any");
            options.Add("--avcodec-fast");
        }

        return options.ToArray();
    }

    private void StartPlayerPreload()
    {
        _playerPreloadTask = Task.Run(async () =>
        {
            try
            {
                if (_gpus.Count > 0 && _gpus[0].IsDiscrete)
                {
                    GpuDetector.ConfigureHighPerformanceGpuPreference(Application.ExecutablePath);
                }

                Task warmTask = _service.WarmUpAsync();
                string vlcDir = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
                if (Directory.Exists(vlcDir))
                {
                    LibVLCSharp.Shared.Core.Initialize(vlcDir);
                }
                else
                {
                    LibVLCSharp.Shared.Core.Initialize();
                }

                string[] options = BuildLibVlcOptions(_gpus.Count > 0, vlcDir);
                var vlc = new LibVLC(options);
                var mp = new MediaPlayer(vlc);
                await warmTask.ConfigureAwait(false);
                PostToUi(() =>
                {
                    if (_libVlc is null)
                    {
                        _libVlc = vlc;
                        _player = mp;
                        _videoView.MediaPlayer = _player;
                        AttachPlayerEvents();
                        if (_gpus.Count > 0)
                        {
                            GpuInfo primaryGpu = _gpus[0];
                            AppLogger.Info(
                                "GPU",
                                $"Motor GPU inicializado en segundo plano. Principal={primaryGpu.Name} ({primaryGpu.DedicatedVideoMemoryMb} MB VRAM, {primaryGpu.PreferredAccelerator.ToUpperInvariant()}). Todos: " +
                                string.Join("; ", _gpus.Select(adapter => adapter.Name)));
                        }
                        else
                        {
                            AppLogger.Info("GPU", "Previsualización por software preparada en segundo plano: no hay adaptador de hardware.");
                        }
                    }
                    else
                    {
                        mp.Dispose();
                        vlc.Dispose();
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.Warning("REPRODUCTOR", "Error durante la inicialización previa de LibVLC en segundo plano.", ex);
            }
        });
    }

    private async Task EnsurePlayerAsync()
    {
        if (_player is not null)
        {
            return;
        }

        if (_playerPreloadTask is not null)
        {
            await _playerPreloadTask.ConfigureAwait(true);
        }

        if (_player is null)
        {
            string vlcDir = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
            if (Directory.Exists(vlcDir))
            {
                LibVLCSharp.Shared.Core.Initialize(vlcDir);
            }
            else
            {
                LibVLCSharp.Shared.Core.Initialize();
            }

            string[] options = BuildLibVlcOptions(_gpus.Count > 0 && _ramGpuCheckBox.Checked, vlcDir);
            _libVlc = new LibVLC(options);
            _player = new MediaPlayer(_libVlc);
            _videoView.MediaPlayer = _player;
            AttachPlayerEvents();
            if (_gpus.Count > 0)
            {
                AppLogger.Info("GPU", "Decodificación por hardware solicitada a LibVLC. Adaptadores: " + string.Join("; ", _gpus.Select(adapter => adapter.Name)));
            }
            else
            {
                AppLogger.Info("GPU", "Previsualización por software: no hay un adaptador de hardware disponible.");
            }
        }
    }

    private void AttachPlayerEvents()
    {
        _player.EndReached += (_, _) => PostToUi(() =>
        {
            _atEnd = true;
            _previewSelection = false;
            _playButton.Text = "▶  Reproducir";
            _playButton.ButtonStyle = ModernButtonStyle.Primary;
            _badgePreviewState.Text = "FINALIZADO";
            _badgePreviewState.Style = BadgeStyle.Amber;
        });

        _player.EncounteredError += (_, _) =>
        {
            AppLogger.Warning("REPRODUCTOR", "LibVLC notificó un error de reproducción.");
            PostToUi(() => _statusLabel.Text = "●  VLC no puede reproducir este archivo. Puedes introducir las marcas manualmente.");
        };
    }

    private void BuildInterface()
    {
        SuspendLayout();
        Name = "principal";
        Text = "VCutStudio · Recorte de vídeo instantáneo sin pérdida";
        Font = new Font("Segoe UI Variable Text", 10);
        BackColor = PageColor;
        ForeColor = VcutTheme.TextPrimary;
        ClientSize = new Size(1280, 850);
        MinimumSize = new Size(720, 650);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;
        AllowDrop = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (ArgumentException)
        {
        }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 10, 18, 12),
            BackColor = PageColor,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (RowStyle row in new[]
        {
            new RowStyle(SizeType.Absolute, 76),
            new RowStyle(SizeType.Percent, 100),
            new RowStyle(SizeType.Absolute, 92),
            new RowStyle(SizeType.Absolute, 26)
        })
        {
            root.RowStyles.Add(row);
        }

        _fileCard.Padding = new Padding(16, 8, 16, 8);
        _fileCard.Margin = new Padding(0, 0, 0, 10);
        _fileCard.ColumnCount = 3;
        _fileCard.RowCount = 1;
        _headerBrandStyle = new ColumnStyle(SizeType.Absolute, 180);
        _fileCard.ColumnStyles.Add(_headerBrandStyle);
        _fileCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _fileCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));

        var brandText = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(4, 4, 10, 4) };
        brandText.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        brandText.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Label brandTitle = MakeLabel("VCutStudio", 15.0f, true);
        brandTitle.ForeColor = VcutTheme.AccentHover;
        _brandSubtitle.ForeColor = VcutTheme.Amber;
        brandText.Controls.Add(brandTitle, 0, 0);
        brandText.Controls.Add(_brandSubtitle, 0, 1);
        _fileCard.Controls.Add(brandText, 0, 0);

        var sourcePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(4, 0, 8, 0) };
        sourcePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        sourcePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        sourcePanel.Controls.Add(_fileName, 0, 0);

        _badgeFormat.Height = 22;
        _badgeFormat.Width = 84;
        _badgeRes.Height = 22;
        _badgeRes.Width = 90;
        _badgeCodec.Height = 22;
        _badgeCodec.Width = 82;
        _badgeDuration.Height = 22;
        _badgeDuration.Width = 105;
        _badgeHardware.Height = 22;
        _badgeHardware.Width = 175;

        _metaContainer.Controls.Add(_badgeFormat);
        _metaContainer.Controls.Add(_badgeRes);
        _metaContainer.Controls.Add(_badgeCodec);
        _metaContainer.Controls.Add(_badgeDuration);
        _metaContainer.Controls.Add(_badgeHardware);
        sourcePanel.Controls.Add(_metaContainer, 0, 1);

        _fileCard.Controls.Add(sourcePanel, 1, 0);

        var openButtonPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0) };
        _openButton.Dock = DockStyle.None;
        _openButton.Anchor = AnchorStyles.None;
        _openButton.Size = new Size(156, 44);
        _openButton.Location = new Point((170 - 156) / 2, (60 - 44) / 2);
        openButtonPanel.Controls.Add(_openButton);
        _fileCard.Controls.Add(openButtonPanel, 2, 0);
        root.Controls.Add(_fileCard, 0, 0);

        BuildPreviewCard();
        BuildMarksCard();
        _body.Controls.Add(_previewCard, 0, 0);
        _body.Controls.Add(_marksCard, 1, 0);
        root.Controls.Add(_body, 0, 1);

        _statusCard.ColumnCount = 4;
        _statusCard.RowCount = 3;
        _statusCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        _statusCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        _statusCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        _statusCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        _statusCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        _statusCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        _statusCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _statusCard.Controls.Add(_progress, 0, 0);
        _statusCard.SetColumnSpan(_progress, 4);
        _statusCard.Controls.Add(_statusLabel, 0, 1);
        _statusCard.SetColumnSpan(_statusLabel, 2);
        _cancelButton.Margin = new Padding(4, 2, 4, 1);
        _folderButton.Margin = new Padding(4, 2, 0, 1);
        _statusCard.Controls.Add(_cancelButton, 2, 1);
        _statusCard.SetRowSpan(_cancelButton, 2);
        _statusCard.Controls.Add(_folderButton, 3, 1);
        _statusCard.SetRowSpan(_folderButton, 2);
        _statusCard.Controls.Add(_elapsedLabel, 0, 2);
        _statusCard.Controls.Add(_remainingLabel, 1, 2);
        root.Controls.Add(_statusCard, 0, 2);

        Label note = MakeLabel("MOTOR DE COPIA DIRECTA (-c copy)  ·  Cero recodificación, 100% calidad original, precarga en RAM y aceleración GPU dedicada.", 8.5f, false, true);
        note.ForeColor = VcutTheme.TextSecondary;
        note.AutoEllipsis = false;
        root.Controls.Add(note, 0, 3);
        Controls.Add(root);

        _toolTip.SetToolTip(_openButton, "Abrir un vídeo (Ctrl+O). También puedes arrastrarlo a la ventana.");
        _toolTip.SetToolTip(_startBox, "Horas:minutos:segundos.milisegundos · 00:01:25.500");
        _toolTip.SetToolTip(_endBox, "Horas:minutos:segundos.milisegundos · 00:02:10.000");
        _toolTip.SetToolTip(_ramGpuCheckBox, "Precarga el vídeo en memoria RAM para acceso instantáneo y usa la GPU dedicada para acelerar la apertura y previsualización.");
        _toolTip.SetToolTip(_mkvCheckBox, "Remultiplexa todas las pistas a Matroska sin recodificarlas. Úsalo si el contenedor original no las admite.");
        ResumeLayout(true);
    }

    private void BuildPreviewCard()
    {
        _previewCard.ColumnCount = 1;
        _previewCard.RowCount = 4;
        _previewCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (RowStyle row in new[]
        {
            new RowStyle(SizeType.Absolute, 38),
            new RowStyle(SizeType.Percent, 100),
            new RowStyle(SizeType.Absolute, 68),
            new RowStyle(SizeType.Absolute, 48)
        })
        {
            _previewCard.RowStyles.Add(row);
        }

        var previewHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        previewHeader.Controls.Add(MakeLabel("🎬  Monitor de estudio", 12.0f, true), 0, 0);

        _positionLabel.Font = new Font("Cascadia Mono", 9.8f, FontStyle.Bold);
        _positionLabel.ForeColor = VcutTheme.Amber;
        _positionLabel.TextAlign = ContentAlignment.MiddleRight;
        previewHeader.Controls.Add(_positionLabel, 1, 0);

        _badgePreviewState.Width = 135;
        _badgePreviewState.Height = 24;
        _badgePreviewState.Anchor = AnchorStyles.Right;
        previewHeader.Controls.Add(_badgePreviewState, 2, 0);
        _previewCard.Controls.Add(previewHeader, 0, 0);

        var canvas = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(8, 5, 14),
            Margin = new Padding(0, 4, 0, 4),
            Padding = new Padding(2)
        };
        typeof(PictureBox).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(_monitorFrameBox, true);
        canvas.Controls.Add(_videoView);
        canvas.Controls.Add(_monitorFrameBox);
        _monitorFrameBox.BringToFront();

        _emptyStatePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(14, 10, 24),
            Margin = new Padding(0)
        };
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(_emptyStatePanel, true);
        _emptyStatePanel.Paint += PaintAnimatedEmptyState;
        _emptyStatePanel.Click += (_, _) => _openButton.PerformClick();
        _emptyStatePanel.Cursor = Cursors.Hand;
        canvas.Controls.Add(_emptyStatePanel);
        _placeholder.Tag = _emptyStatePanel;
        _emptyStatePanel.BringToFront();

        _previewCard.Controls.Add(canvas, 0, 1);
        _previewCard.Controls.Add(_timeline, 0, 2);

        // Studio Transport Deck directly below timeline
        var transport = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Margin = new Padding(0, 2, 0, 0) };
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));

        _markStartButton.Margin = new Padding(0, 2, 4, 2);
        _jumpBackButton.Margin = new Padding(2, 2, 2, 2);
        _playButton.Margin = new Padding(4, 2, 4, 2);
        _jumpForwardButton.Margin = new Padding(2, 2, 2, 2);
        _markEndButton.Margin = new Padding(4, 2, 0, 2);

        transport.Controls.Add(_markStartButton, 0, 0);
        transport.Controls.Add(_jumpBackButton, 1, 0);
        transport.Controls.Add(_playButton, 2, 0);
        transport.Controls.Add(_jumpForwardButton, 3, 0);
        transport.Controls.Add(_markEndButton, 4, 0);
        _previewCard.Controls.Add(transport, 0, 3);
    }

    private void PaintAnimatedEmptyState(object sender, PaintEventArgs e)
    {
        if (sender is not Control panel)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = panel.ClientRectangle;
        if (rect.Width < 20 || rect.Height < 20)
        {
            return;
        }

        using (var bg = new LinearGradientBrush(
            rect,
            Color.FromArgb(16, 11, 28),
            Color.FromArgb(10, 7, 18),
            LinearGradientMode.ForwardDiagonal))
        {
            e.Graphics.FillRectangle(bg, rect);
        }

        float cx = rect.Width / 2.0f;
        float cy = rect.Height / 2.0f - 18.0f;
        float pulse = 1.0f + 0.08f * (float)Math.Sin(_ambientPhase * 2.0f);
        float rOuter = 66.0f * pulse;
        float rInner = 46.0f / pulse;

        // Animated glowing rings
        using (var outerPen = new Pen(Color.FromArgb(75, VcutTheme.Violet), 2.0f) { DashStyle = DashStyle.Dash })
        using (var midPen = new Pen(Color.FromArgb(110, VcutTheme.Accent), 2.2f))
        using (var glowBrush = new SolidBrush(Color.FromArgb(28, VcutTheme.Accent)))
        {
            e.Graphics.FillEllipse(glowBrush, cx - rOuter, cy - rOuter, rOuter * 2, rOuter * 2);
            e.Graphics.DrawEllipse(outerPen, cx - rOuter, cy - rOuter, rOuter * 2, rOuter * 2);
            e.Graphics.DrawEllipse(midPen, cx - rInner, cy - rInner, rInner * 2, rInner * 2);
        }

        if (_brandImage is not null)
        {
            var iconRect = new RectangleF(cx - 26, cy - 26, 52, 52);
            e.Graphics.DrawImage(_brandImage, iconRect);
        }

        using var titleFont = new Font("Segoe UI Variable Display", 12.0f, FontStyle.Bold);
        using var subFont = new Font("Segoe UI Variable Text", 9.2f, FontStyle.Regular);
        var titleRect = new Rectangle(16, (int)(cy + rOuter + 10), rect.Width - 32, 28);
        var subRect = new Rectangle(16, (int)(cy + rOuter + 38), rect.Width - 32, 42);

        TextRenderer.DrawText(
            e.Graphics,
            "Arrastra un vídeo aquí o haz clic para abrirlo",
            titleFont,
            titleRect,
            VcutTheme.TextPrimary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            e.Graphics,
            "MP4 · MKV · MOV · AVI · WebM · TS · ProRes  |  Análisis instantáneo en RAM + GPU",
            subFont,
            subRect,
            VcutTheme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    private void BuildMarksCard()
    {
        _marksCard.ColumnCount = 2;
        _marksCard.RowCount = 10;
        _marksCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _marksCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (float rowHeight in new float[] { 36, 76, 24, 44, 46, 32, 32, 36, 54 })
        {
            _marksCard.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
        }

        _marksCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var cutHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        cutHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cutHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        cutHeader.Controls.Add(MakeLabel("✂  Inspector de corte", 12.0f, true), 0, 0);
        BadgeLabel badgeLossless = MakeStyledBadge("COPIA 1:1", BadgeStyle.Coral);
        badgeLossless.Width = 105;
        badgeLossless.Height = 24;
        cutHeader.Controls.Add(badgeLossless, 1, 0);
        AddWide(_marksCard, cutHeader, 0);

        // Hero Duration Sub-Card
        _heroSelectionCard.ColumnCount = 2;
        _heroSelectionCard.RowCount = 2;
        _heroSelectionCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _heroSelectionCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        _heroSelectionCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        _heroSelectionCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Label heroTitle = MakeLabel("DURACIÓN DEL FRAGMENTO", 8.0f, true, true);
        heroTitle.ForeColor = VcutTheme.TextSecondary;
        _selectionLabel.Font = new Font("Cascadia Mono", 14.0f, FontStyle.Bold);
        _selectionLabel.ForeColor = VcutTheme.AccentHover;
        _badgeClipShare.Width = 115;
        _badgeClipShare.Height = 22;
        _badgeClipShare.Anchor = AnchorStyles.Right;
        _heroSelectionCard.Controls.Add(heroTitle, 0, 0);
        _heroSelectionCard.Controls.Add(_badgeClipShare, 1, 0);
        _heroSelectionCard.SetRowSpan(_badgeClipShare, 2);
        _heroSelectionCard.Controls.Add(_selectionLabel, 0, 1);
        AddWide(_marksCard, _heroSelectionCard, 1);

        Label lblIn = MakeLabel("PUNTO IN  [I]", 8.5f, true, true);
        lblIn.ForeColor = VcutTheme.AccentHover;
        Label lblOut = MakeLabel("PUNTO OUT  [O]", 8.5f, true, true);
        lblOut.ForeColor = VcutTheme.Amber;
        _marksCard.Controls.Add(lblIn, 0, 2);
        _marksCard.Controls.Add(lblOut, 1, 2);
        _marksCard.Controls.Add(_startBox, 0, 3);
        _marksCard.Controls.Add(_endBox, 1, 3);

        AddWide(_marksCard, _previewButton, 4);
        AddWide(_marksCard, _ramGpuCheckBox, 5);
        AddWide(_marksCard, _mkvCheckBox, 6);
        AddWide(_marksCard, _outputLabel, 7);

        _cutButton.Height = 48;
        _cutButton.Font = new Font("Segoe UI Variable Text", 10.2f, FontStyle.Bold);
        AddWide(_marksCard, _cutButton, 8);

        Label help = MakeLabel("Atajos rápidos: Espacio (Reproducir/Pausar) · I (Inicio) · O (Fin) · ←/→ (Ajuste fino en línea de tiempo).", 8.3f, false, true);
        help.AutoEllipsis = false;
        help.TextAlign = ContentAlignment.TopLeft;
        help.Padding = new Padding(0, 6, 0, 0);
        AddWide(_marksCard, help, 9);
    }

    private static void AddWide(TableLayoutPanel panel, Control control, int row)
    {
        panel.Controls.Add(control, 0, row);
        panel.SetColumnSpan(control, 2);
    }

    private void AdaptLayout()
    {
        if (_body.Parent is null)
        {
            return;
        }

        bool narrow = ClientSize.Width < (int)(1020 * DeviceDpi / 96.0);
        if (_body.ColumnStyles.Count > 0 && narrow == _narrow)
        {
            return;
        }

        _narrow = narrow;
        _brandSubtitle.Visible = !narrow;
        if (_headerBrandStyle is not null)
        {
            _headerBrandStyle.Width = narrow ? 110 : 235;
        }

        _body.SuspendLayout();
        _body.ColumnStyles.Clear();
        _body.RowStyles.Clear();
        if (narrow)
        {
            _body.SetCellPosition(_marksCard, new TableLayoutPanelCellPosition(0, 1));
            _body.ColumnCount = 1;
            _body.RowCount = 2;
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _body.RowStyles.Add(new RowStyle(SizeType.Absolute, (float)(440 * DeviceDpi / 96.0)));
            _body.RowStyles.Add(new RowStyle(SizeType.Absolute, (float)(460 * DeviceDpi / 96.0)));
            _previewCard.Margin = new Padding(0, 0, 0, 12);
            _marksCard.Padding = new Padding(16);
        }
        else
        {
            _body.ColumnCount = 2;
            _body.RowCount = 1;
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (float)(405 * DeviceDpi / 96.0)));
            _body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _body.SetCellPosition(_marksCard, new TableLayoutPanelCellPosition(1, 0));
            _previewCard.Margin = new Padding(0, 0, 14, 0);
            _marksCard.Padding = new Padding(18);
        }

        _body.AutoScroll = true;
        _body.AutoScrollMinSize = new Size(0, (int)((narrow ? 900 : 420) * DeviceDpi / 96.0));
        _body.ResumeLayout(true);
    }

    private void RamGpuModeChanged(object sender, EventArgs e)
    {
        bool active = _ramGpuCheckBox.Checked;
        _ramGpuCheckBox.Text = active
            ? "⚡ Precargar en RAM y acelerar con GPU (activo)"
            : "⚡ Precargar en RAM y acelerar con GPU";
        _ramGpuCheckBox.ForeColor = active ? VcutTheme.AccentHover : VcutTheme.TextSecondary;
        if (!active)
        {
            _ramPreloadCts?.Cancel();
            _ramCache.Clear();
        }

        UpdateHardwareBadge(_ramCache.CurrentStatus);
        AppLogger.Info("RENDIMIENTO", active ? "Activada precarga en RAM y aceleración por GPU." : "Desactivada precarga en RAM y aceleración por GPU.");
    }

    private void UpdateHardwareBadge(RamCacheStatus ramStatus)
    {
        string gpuName = _gpus.Count > 0 ? _gpus[0].ShortName : "CPU";
        if (!_ramGpuCheckBox.Checked)
        {
            _badgeHardware.Text = "MODO ESTÁNDAR";
            _badgeHardware.Style = BadgeStyle.Neutral;
            _badgeHardware.Width = 124;
            return;
        }

        string text = ramStatus is not null && ramStatus.CachedBytes > 0
            ? $"⚡ {gpuName} · {ramStatus.Summary.ToUpperInvariant()}"
            : $"⚡ {gpuName} · RAM ACTIVA";

        _badgeHardware.Text = text;
        _badgeHardware.Style = ramStatus is not null && ramStatus.IsFullyCached ? BadgeStyle.Mint : BadgeStyle.Coral;
        _badgeHardware.Width = Math.Max(148, text.Length * 7 + 20);
        if (_gpus.Count > 0)
        {
            GpuInfo primary = _gpus[0];
            _toolTip.SetToolTip(
                _badgeHardware,
                $"GPU principal: {primary.Name} ({primary.DedicatedVideoMemoryMb} MB VRAM · {primary.PreferredAccelerator.ToUpperInvariant()})" +
                (ramStatus is not null ? $"\r\nCaché RAM: {ramStatus.Summary} en {ramStatus.Elapsed.TotalMilliseconds:0} ms" : string.Empty));
        }
    }

    private async void OpenClicked(object sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Selecciona un vídeo",
            Filter = "Vídeos compatibles|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.ts;*.mts;*.m2ts;*.wmv;*.asf;*.flv;*.f4v;*.mpg;*.mpeg;*.vob;*.3gp;*.3g2;*.ogv;*.ogg;*.mxf;*.dv;*.divx;*.rm;*.rmvb;*.mjpeg;*.mjpg;*.y4m;*.nut;*.wtv;*.dvr-ms;*.amv;*.roq;*.nsv|MP4 / MOV / M4V|*.mp4;*.mov;*.m4v|Matroska / WebM|*.mkv;*.webm|MPEG / Transport Stream|*.mpg;*.mpeg;*.ts;*.mts;*.m2ts;*.vob|AVI / Windows Media|*.avi;*.divx;*.wmv;*.asf;*.wtv;*.dvr-ms|Todos los archivos|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AppLogger.Info("ARCHIVO", "Seleccionado mediante diálogo: " + dialog.FileName);
            await LoadVideoAsync(dialog.FileName);
        }
    }

    public async Task LoadVideoAsync(string filePath)
    {
        if (_busy)
        {
            return;
        }

        var loadWatch = Stopwatch.StartNew();
        _thumbnailPopup.HidePopup();
        _thumbHoverCts?.Cancel();
        _thumbHoverCts?.Dispose();
        _thumbHoverCts = new CancellationTokenSource();
        _monitorFrameCts?.Cancel();
        _monitorFrameCts?.Dispose();
        _monitorFrameCts = new CancellationTokenSource();
        _thumbPrewarmCts?.Cancel();
        _thumbPrewarmCts?.Dispose();
        _thumbPrewarmCts = new CancellationTokenSource();
        _ramPreloadCts?.Cancel();
        _ramPreloadCts?.Dispose();
        _ramPreloadCts = new CancellationTokenSource();
        _operation = new CancellationTokenSource();
        _lastOutput = null;
        _renderedMonitorBucket = -1;
        _currentPositionMs = 0;
        _progress.Value = 120;
        _progress.IsIndeterminate = true;
        SetBusy(true);

        bool useRamAndGpu = _ramGpuCheckBox.Checked;
        string gpuLabel = _gpus.Count > 0 && useRamAndGpu ? _gpus[0].ShortName : "CPU";
        _badgePreviewState.Text = "ANALIZANDO…";
        _badgePreviewState.Style = BadgeStyle.Coral;
        _statusLabel.Text = useRamAndGpu
            ? $"●  Cargando en RAM y analizando con {gpuLabel}…"
            : "●  Analizando el vídeo con FFprobe…";
        _statusLabel.ForeColor = VcutTheme.AccentHover;
        AppLogger.Info("CARGA", $"Iniciando carga (RAM+GPU={useRamAndGpu}; GPU={gpuLabel}): {filePath}");

        try
        {
            var probeProgress = new Progress<double>(p =>
            {
                if (_busy && !_cutting)
                {
                    _progress.Value = (int)Math.Clamp(120 + p * 450, 120, 570);
                }
            });

            Task<VideoInfo> probeTask = _service.ProbeAsync(
                filePath,
                _operation.Token,
                probeProgress,
                primeRamHeaders: useRamAndGpu,
                allowNativeFastPath: useRamAndGpu);
            Task playerTask = EnsurePlayerAsync();

            if (useRamAndGpu)
            {
                CancellationToken ramToken = _ramPreloadCts.Token;
                _ = _ramCache.PreloadAsync(filePath, ramToken).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully && !ramToken.IsCancellationRequested)
                    {
                        RamCacheStatus status = t.Result;
                        AppLogger.Info("RAM", $"Vídeo en caché RAM: {status.CachedBytes} / {status.FileBytes} bytes ({status.Summary}) en {status.Elapsed.TotalMilliseconds:0} ms.");
                        PostToUi(() =>
                        {
                            UpdateHardwareBadge(status);
                            if (!_cutting)
                            {
                                _remainingLabel.Text = $"{status.Summary} ({status.Elapsed.TotalMilliseconds:0} ms)";
                            }
                        });
                    }
                }, TaskScheduler.Default);
            }
            else
            {
                _ramCache.Clear();
            }

            _video = await probeTask;

            _progress.Value = Math.Max(_progress.Value, 650);
            _startBox.Text = Timecode.Format(TimeSpan.Zero);
            _endBox.Text = Timecode.Format(TimeSpan.FromMilliseconds(Math.Floor(_video.Duration.TotalMilliseconds)));
            _fileName.Text = Path.GetFileName(filePath);
            _toolTip.SetToolTip(_fileName, _video.FilePath);

            string ext = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
            _badgeFormat.Text = string.IsNullOrEmpty(ext) ? "VÍDEO" : ext;
            _badgeFormat.Style = BadgeStyle.Coral;

            string resText = _video.Width > 0 && _video.Height > 0 ? $"{_video.Width}×{_video.Height}" : "Desconocida";
            if (_video.Height >= 2160)
            {
                resText = $"4K UHD ({_video.Width}×{_video.Height})";
            }
            else if (_video.Height >= 1080)
            {
                resText = $"1080p FHD ({_video.Width}×{_video.Height})";
            }
            else if (_video.Height >= 720)
            {
                resText = $"720p HD ({_video.Width}×{_video.Height})";
            }

            _badgeRes.Text = resText;
            _badgeRes.Width = Math.Max(88, resText.Length * 7 + 18);
            _badgeRes.Style = BadgeStyle.Cyan;

            string codecDisplay = string.IsNullOrEmpty(_video.Codec) || _video.Codec == "desconocido"
                ? "VÍDEO"
                : _video.Codec.ToUpperInvariant();
            if (!string.IsNullOrEmpty(_video.AudioCodec))
            {
                codecDisplay += " · " + _video.AudioCodec.ToUpperInvariant();
            }

            _badgeCodec.Text = codecDisplay;
            _badgeCodec.Width = Math.Max(80, codecDisplay.Length * 7 + 18);
            _badgeCodec.Style = BadgeStyle.Purple;

            _badgeDuration.Text = Timecode.Format(_video.Duration);
            _badgeDuration.Style = BadgeStyle.Amber;

            UpdateOutputPreview();
            _timeline.TotalDurationSeconds = _video.Duration.TotalSeconds;
            _timeline.Position = 0;
            _timeline.SetSelection(0, 1);
            _currentPositionMs = 0;
            _renderedMonitorBucket = -1;
            _lastLookaheadCenterBucket = -1000;
            _lastMonitorJpeg = null;
            ClearDecodedBitmaps(keepCurrentDisplayed: false);
            _thumbnailService.Reset(_video.FilePath, _video.Duration, 0);
            RequestMonitorFrameUpdate(0.0, requestHighRes: true);
            if (_thumbPrewarmCts is not null)
            {
                CancellationToken prewarmToken = _thumbPrewarmCts.Token;
                _ = Task.Run(() => _thumbnailService.PrewarmAllHalfSecondsAsync(prewarmToken), prewarmToken);
            }
            _positionLabel.Text = $"00:00:00.000  /  {Timecode.Format(_video.Duration)}";
            _previewSelection = false;
            _atEnd = false;
            _pendingSeek = null;

            _progress.Value = 850;
            _statusLabel.Text = "●  Activando motor de reproducción GPU…";

            try
            {
                await playerTask;
                _player.Stop();
                _player.Media = null;
                _media?.Dispose();

                _media = new Media(_libVlc, _video.FilePath, FromType.FromPath);
                _media.AddOption(":start-paused");
                _media.AddOption(":no-sub-autodetect-file");
                _media.AddOption(":file-caching=100");
                if (useRamAndGpu && _gpus.Count > 0)
                {
                    _media.AddOption(":avcodec-hw=any");
                    _media.AddOption(":avcodec-threads=0");
                }
                else
                {
                    _media.AddOption(":avcodec-hw=none");
                }

                SetEmptyStateVisible(false);

                if (!_player.Play(_media))
                {
                    throw new InvalidOperationException("El reproductor no pudo abrir el vídeo.");
                }

                loadWatch.Stop();
                _fileCard.BorderColor = VcutTheme.Accent;
                _fileCard.Invalidate();

                _badgePreviewState.Text = useRamAndGpu ? $"GPU · {gpuLabel.ToUpperInvariant()}" : "LISTO";
                _badgePreviewState.Style = BadgeStyle.Mint;

                _progress.IsIndeterminate = false;
                _progress.Value = 1000;
                RamCacheStatus ramNow = _ramCache.CurrentStatus;
                UpdateHardwareBadge(ramNow);
                string perfDetails = ramNow is not null ? $" · {ramNow.Summary}" : (useRamAndGpu ? " · Cargando RAM" : "");
                _statusLabel.Text = $"✔  Vídeo listo en {loadWatch.ElapsedMilliseconds} ms{perfDetails} · GPU {gpuLabel}";
                _statusLabel.ForeColor = VcutTheme.Success;
                _elapsedLabel.Text = $"Análisis inicial  ·  {loadWatch.ElapsedMilliseconds} ms";
                _remainingLabel.Text = ramNow is not null ? ramNow.Summary : "Listo para recortar";
                AppLogger.Info("CARGA", $"Vídeo cargado con éxito en {loadWatch.ElapsedMilliseconds} ms. Duración={Timecode.Format(_video.Duration)}; resolución={_video.Width}x{_video.Height}; códec={_video.Codec}; ruta={_video.FilePath}.");
            }
            catch (Exception ex)
            {
                _placeholder.Text = "Previsualización visual no disponible\r\nPuedes introducir las marcas de tiempo y recortar sin recodificar.";
                SetEmptyStateVisible(true);
                _badgePreviewState.Text = "SIN VISTA PREVIA";
                _badgePreviewState.Style = BadgeStyle.Warning;
                _statusLabel.Text = "●  Vídeo analizado; previsualización directa no disponible.";
                _statusLabel.ForeColor = VcutTheme.Warning;
                _toolTip.SetToolTip(_placeholder, ex.Message);
                AppLogger.Warning("REPRODUCTOR", "El vídeo se cargó, pero la previsualización no está disponible.", ex);
            }
        }
        catch (OperationCanceledException)
        {
            _progress.IsIndeterminate = false;
            _progress.Value = 0;
            _statusLabel.Text = "●  Carga cancelada.";
            _statusLabel.ForeColor = VcutTheme.Warning;
            AppLogger.Info("CARGA", "Carga cancelada por el usuario: " + filePath);
        }
        catch (Exception ex)
        {
            _progress.IsIndeterminate = false;
            _progress.Value = 0;
            ShowError("No se ha podido cargar el vídeo", ex);
        }
        finally
        {
            _progress.IsIndeterminate = false;
            FinishOperation();
        }
    }

    private void TogglePlayback()
    {
        if (_busy || _video is null || _player is null)
        {
            return;
        }

        _previewSelection = false;
        if (_atEnd)
        {
            _currentPositionMs = 0;
            HideMonitorFrameOverlay();
            _player.Stop();
            _player.Play(_media);
            _player.SetPause(false);
            _atEnd = false;
            _playButton.Text = "⏸  Pausar";
            _playButton.ButtonStyle = ModernButtonStyle.Secondary;
            _badgePreviewState.Text = "EN REPRODUCCIÓN";
            _badgePreviewState.Style = BadgeStyle.Mint;
        }
        else
        {
            bool shouldPause = _player.IsPlaying;
            if (shouldPause)
            {
                long playerNow = Math.Clamp(_player.Time, 0L, (long)_video.Duration.TotalMilliseconds);
                if (playerNow > 0)
                {
                    _currentPositionMs = playerNow;
                }

                _player.SetPause(true);
                double pauseFraction = _video.Duration.TotalMilliseconds > 0
                    ? _currentPositionMs / _video.Duration.TotalMilliseconds
                    : 0.0;
                RequestMonitorFrameUpdate(pauseFraction, requestHighRes: true);
            }
            else
            {
                HideMonitorFrameOverlay();
                _player.Time = _currentPositionMs;
                _pendingSeek = _currentPositionMs;
                _player.SetPause(false);
            }

            _playButton.Text = shouldPause ? "▶  Reproducir" : "⏸  Pausar";
            _playButton.ButtonStyle = shouldPause ? ModernButtonStyle.Primary : ModernButtonStyle.Secondary;
            _badgePreviewState.Text = shouldPause ? "PAUSADO" : "EN REPRODUCCIÓN";
            _badgePreviewState.Style = shouldPause ? BadgeStyle.Amber : BadgeStyle.Mint;
        }
    }

    private void SeekTo(double fraction)
    {
        if (_busy || _video is null)
        {
            return;
        }

        double clampedFraction = Math.Clamp(fraction, 0.0, 1.0);
        _previewSelection = false;
        long target = (long)Math.Min(clampedFraction * _video.Duration.TotalMilliseconds, Math.Max(0, _video.Duration.TotalMilliseconds - 1));
        _currentPositionMs = target;

        if (_player is not null)
        {
            if (_atEnd)
            {
                _player.Stop();
                _player.Play(_media);
                _atEnd = false;
                _pendingSeek = target;
            }
            else
            {
                _pendingSeek = target;
                if (_player.IsPlaying && !_timeline.IsDragging)
                {
                    _player.Time = target;
                }
            }
        }

        _timeline.Position = clampedFraction;
        var targetSpan = TimeSpan.FromMilliseconds(target);
        _positionLabel.Text = $"{Timecode.Format(targetSpan)}  /  {Timecode.Format(_video.Duration)}";

        _monitorFrameBox.IsScrubbing = _timeline.IsDragging;
        RequestMonitorFrameUpdate(clampedFraction, requestHighRes: !_timeline.IsDragging);
    }

    private void OnTimelineScrubEnded(double fraction)
    {
        if (_busy || _video is null)
        {
            return;
        }

        _monitorFrameBox.IsScrubbing = false;
        double clampedFraction = Math.Clamp(fraction, 0.0, 1.0);
        if (_player is not null && _player.IsPlaying)
        {
            _player.Time = _currentPositionMs;
            HideMonitorFrameOverlay();
        }
        else
        {
            _monitorFrameBox.Invalidate();
            RequestMonitorFrameUpdate(clampedFraction, requestHighRes: true);
        }
    }

    private void RequestMonitorFrameUpdate(double fraction, bool requestHighRes)
    {
        if (_video is null)
        {
            return;
        }

        double clamped = Math.Clamp(fraction, 0.0, 1.0);
        int targetBucket = _thumbnailService.FractionToHalfSecondBucket(clamped);

        Size currentBoxSize = _monitorFrameBox.ClientSize;
        if (currentBoxSize.Width > 32 && currentBoxSize.Height > 32)
        {
            if (_lastMonitorTargetSize != currentBoxSize)
            {
                _lastMonitorTargetSize = currentBoxSize;
                ClearDecodedBitmaps(keepCurrentDisplayed: true);
            }
        }

        byte[] candidateJpeg = null;
        if (!_thumbnailService.TryGetFullQualityFrame(clamped, out candidateJpeg) || candidateJpeg is not { Length: > 0 })
        {
            _thumbnailService.TryGetClosestFrame(clamped, out candidateJpeg);
        }

        if (candidateJpeg is { Length: > 0 })
        {
            if (ReferenceEquals(_lastMonitorJpeg, candidateJpeg) && _monitorFrameBox.Visible)
            {
                _renderedMonitorBucket = targetBucket;
            }
            else if (_decodedBitmapByJpeg.TryGetValue(candidateJpeg, out Bitmap cachedBitmap) && cachedBitmap is not null)
            {
                ApplyMonitorBitmap(cachedBitmap, candidateJpeg, targetBucket);
            }
        }

        _wantHighResMonitorFrame = requestHighRes;
        Interlocked.Exchange(ref _desiredMonitorFraction, clamped);

        if (_monitorFrameWorkerTask is null || _monitorFrameWorkerTask.IsCompleted)
        {
            CancellationToken token = _monitorFrameCts?.Token ?? CancellationToken.None;
            _monitorFrameWorkerTask = Task.Run(() => RunMonitorFrameWorkerAsync(token), token);
        }
    }

    private async Task RunMonitorFrameWorkerAsync(CancellationToken token)
    {
        double lastProcessedFraction = -1.0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                double nextFraction = Interlocked.Exchange(ref _desiredMonitorFraction, -1.0);
                if (nextFraction < 0.0)
                {
                    break;
                }

                lastProcessedFraction = nextFraction;
                int bucket = _thumbnailService.FractionToHalfSecondBucket(nextFraction);
                Size targetSize = _lastMonitorTargetSize;

                // 1. If the exact bucket isn't in RAM yet, immediately decode and display the closest cached frame off the UI thread
                if (!_thumbnailService.TryGetFullQualityFrame(nextFraction, out byte[] immediateBytes) &&
                    _thumbnailService.TryGetClosestFrame(nextFraction, out immediateBytes) &&
                    immediateBytes is { Length: > 0 } &&
                    !ReferenceEquals(immediateBytes, _lastMonitorJpeg))
                {
                    Bitmap immediateBitmap = GetOrDecodeMonitorBitmap(immediateBytes, targetSize);
                    if (immediateBitmap is not null && !token.IsCancellationRequested)
                    {
                        PostToUi(() =>
                        {
                            if (_video is not null)
                            {
                                ApplyMonitorBitmap(immediateBitmap, immediateBytes, bucket);
                            }
                        });
                    }
                }

                // 2. Multi-core lookahead: pre-decode surrounding half-second buckets in parallel into Format32bppPArgb HighQualityBicubic bitmaps
                if (Math.Abs(bucket - Volatile.Read(ref _lastLookaheadCenterBucket)) >= 4)
                {
                    Volatile.Write(ref _lastLookaheadCenterBucket, bucket);
                    _ = Task.Run(() => PredecodeNeighborhoodBitmaps(bucket, targetSize, token), token);
                }

                // 3. Get or extract the native-resolution (-qmin 1 -q:v 1) frame for this bucket
                byte[] frameBytes = await _thumbnailService.GetFullQualityFrameAsync(nextFraction, accurateSeek: false, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (frameBytes is { Length: > 0 } && _video is not null)
                {
                    Bitmap decodedBitmap = GetOrDecodeMonitorBitmap(frameBytes, targetSize);
                    if (decodedBitmap is not null)
                    {
                        PostToUi(() =>
                        {
                            if (_video is null)
                            {
                                return;
                            }

                            ApplyMonitorBitmap(decodedBitmap, frameBytes, bucket);
                            if (_thumbnailPopup.Visible && Math.Abs(_lastHoverFraction - nextFraction) < 0.03)
                            {
                                string currentText = Timecode.Format(TimeSpan.FromSeconds(_lastHoverFraction * _video.Duration.TotalSeconds));
                                _thumbnailPopup.ShowAt(this, _lastHoverScreenAnchor, currentText, frameBytes, isExact: true);
                            }
                        });
                    }
                }
            }

            if (!token.IsCancellationRequested &&
                _wantHighResMonitorFrame &&
                lastProcessedFraction >= 0.0 &&
                !_timeline.IsDragging &&
                (_player is null || !_player.IsPlaying) &&
                _video is not null)
            {
                byte[] exactHqBytes = await _thumbnailService.GetFullQualityFrameAsync(lastProcessedFraction, accurateSeek: true, token).ConfigureAwait(false);
                if (!token.IsCancellationRequested &&
                    exactHqBytes is { Length: > 0 } &&
                    _desiredMonitorFraction < 0.0 &&
                    !_timeline.IsDragging &&
                    (_player is null || !_player.IsPlaying))
                {
                    int bucket = _thumbnailService.FractionToHalfSecondBucket(lastProcessedFraction);
                    Bitmap exactBitmap = GetOrDecodeMonitorBitmap(exactHqBytes, _lastMonitorTargetSize);
                    if (exactBitmap is not null)
                    {
                        PostToUi(() =>
                        {
                            if (_video is null || _desiredMonitorFraction >= 0.0 || _timeline.IsDragging)
                            {
                                return;
                            }

                            _monitorFrameBox.IsScrubbing = false;
                            ApplyMonitorBitmap(exactBitmap, exactHqBytes, bucket);
                        });
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void PredecodeNeighborhoodBitmaps(int centerBucket, Size targetBoxSize, CancellationToken token)
    {
        int totalBuckets = _thumbnailService.TotalHalfSecondBuckets;
        if (totalBuckets <= 0)
        {
            return;
        }

        int start = Math.Max(0, centerBucket - 24);
        int endExclusive = Math.Min(totalBuckets, centerBucket + 25);
        Parallel.For(
            start,
            endExclusive,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8),
                CancellationToken = token
            },
            b =>
            {
                byte[] jpeg = _thumbnailService.TryGetFrameByBucket(b);
                if (jpeg is { Length: > 0 } && !_decodedBitmapByJpeg.ContainsKey(jpeg))
                {
                    GetOrDecodeMonitorBitmap(jpeg, targetBoxSize);
                }
            });
    }

    private Bitmap GetOrDecodeMonitorBitmap(byte[] jpegBytes, Size targetBoxSize)
    {
        if (jpegBytes is null || jpegBytes.Length == 0)
        {
            return null;
        }

        if (_decodedBitmapByJpeg.TryGetValue(jpegBytes, out Bitmap existing) && existing is not null)
        {
            return existing;
        }

        Bitmap decoded = DecodeBitmapOffUiThread(jpegBytes, targetBoxSize);
        if (decoded is null)
        {
            return null;
        }

        if (_decodedBitmapByJpeg.TryAdd(jpegBytes, decoded))
        {
            _decodedBitmapOrder.Enqueue(jpegBytes);
            while (_decodedBitmapOrder.Count > MaxDecodedBitmapsInRam && _decodedBitmapOrder.TryDequeue(out byte[] oldKey))
            {
                if (_decodedBitmapByJpeg.TryRemove(oldKey, out Bitmap evicted) && evicted is not null)
                {
                    PostToUi(() =>
                    {
                        if (!ReferenceEquals(_monitorFrameBox.Image, evicted))
                        {
                            evicted.Dispose();
                        }
                    });
                }
            }

            return decoded;
        }

        decoded.Dispose();
        return _decodedBitmapByJpeg.TryGetValue(jpegBytes, out Bitmap winner) ? winner : null;
    }

    private static Bitmap DecodeBitmapOffUiThread(byte[] jpegBytes, Size targetBoxSize)
    {
        if (jpegBytes is null || jpegBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var ms = new MemoryStream(jpegBytes, writable: false);
            using var decoded = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            int destW = decoded.Width;
            int destH = decoded.Height;
            if (targetBoxSize.Width > 32 && targetBoxSize.Height > 32 && decoded.Width > 0 && decoded.Height > 0)
            {
                double scale = Math.Min((double)targetBoxSize.Width / decoded.Width, (double)targetBoxSize.Height / decoded.Height);
                destW = Math.Max(1, (int)Math.Round(decoded.Width * scale));
                destH = Math.Max(1, (int)Math.Round(decoded.Height * scale));
            }

            var bitmap = new Bitmap(destW, destH, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(decoded, new Rectangle(0, 0, destW, destH));
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void ClearDecodedBitmaps(bool keepCurrentDisplayed)
    {
        Image currentDisplayed = keepCurrentDisplayed ? _monitorFrameBox.Image : null;
        while (_decodedBitmapOrder.TryDequeue(out byte[] key))
        {
            if (_decodedBitmapByJpeg.TryRemove(key, out Bitmap bmp) && bmp is not null && !ReferenceEquals(bmp, currentDisplayed))
            {
                bmp.Dispose();
            }
        }

        foreach (KeyValuePair<byte[], Bitmap> kv in _decodedBitmapByJpeg)
        {
            if (_decodedBitmapByJpeg.TryRemove(kv.Key, out Bitmap bmp) && bmp is not null && !ReferenceEquals(bmp, currentDisplayed))
            {
                bmp.Dispose();
            }
        }
    }

    private void ApplyMonitorBitmap(Bitmap bitmap, byte[] sourceJpeg, int bucket)
    {
        if (IsDisposed || bitmap is null)
        {
            return;
        }

        _lastMonitorJpeg = sourceJpeg;
        _renderedMonitorBucket = bucket;
        if (!ReferenceEquals(_monitorFrameBox.Image, bitmap))
        {
            _monitorFrameBox.Image = bitmap;
        }

        if (_emptyStatePanel is null || !_emptyStatePanel.Visible)
        {
            if (!_monitorFrameBox.Visible)
            {
                _monitorFrameBox.Visible = true;
            }

            _monitorFrameBox.BringToFront();
        }
    }

    private void HideMonitorFrameOverlay()
    {
        _wantHighResMonitorFrame = false;
        _monitorFrameBox.IsScrubbing = false;
        Interlocked.Exchange(ref _desiredMonitorFraction, -1.0);
        if (_monitorFrameBox.Visible)
        {
            _monitorFrameBox.Visible = false;
        }
    }

    private void OnTimelineHoverChanged(double fraction, Point screenAnchor)
    {
        if (_busy || _video is null)
        {
            _thumbnailPopup.HidePopup();
            return;
        }

        _lastHoverFraction = fraction;
        _lastHoverScreenAnchor = screenAnchor;
        string timeText = Timecode.Format(TimeSpan.FromSeconds(fraction * _video.Duration.TotalSeconds));

        _thumbnailService.TryGetClosestFrame(fraction, out byte[] closestBytes);
        bool isExactCached = closestBytes is not null &&
                             _thumbnailService.TryGetClosestFrame(fraction, out _, maxDistanceBuckets: 0);
        _thumbnailPopup.ShowAt(this, screenAnchor, timeText, closestBytes, isExact: isExactCached);

        if (isExactCached || _timeline.IsDragging)
        {
            return;
        }

        Interlocked.Exchange(ref _desiredHoverFraction, fraction);
        if (_hoverFrameWorkerTask is null || _hoverFrameWorkerTask.IsCompleted)
        {
            CancellationToken token = _thumbHoverCts?.Token ?? CancellationToken.None;
            _hoverFrameWorkerTask = Task.Run(() => RunHoverFrameWorkerAsync(token), token);
        }
    }

    private async Task RunHoverFrameWorkerAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                double nextFraction = Interlocked.Exchange(ref _desiredHoverFraction, -1.0);
                if (nextFraction < 0.0)
                {
                    break;
                }

                byte[] exactBytes = await _thumbnailService.GetFrameAsync(nextFraction, token).ConfigureAwait(false);
                if (!token.IsCancellationRequested && exactBytes is { Length: > 0 })
                {
                    PostToUi(() =>
                    {
                        if (_thumbnailPopup.Visible && _video is not null)
                        {
                            string currentText = Timecode.Format(TimeSpan.FromSeconds(_lastHoverFraction * _video.Duration.TotalSeconds));
                            _thumbnailPopup.ShowAt(this, _lastHoverScreenAnchor, currentText, exactBytes, isExact: true);
                        }
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void OnTimelineHoverEnded()
    {
        Interlocked.Exchange(ref _desiredHoverFraction, -1.0);
        _thumbnailPopup.HidePopup();
    }

    private void SeekRelative(double seconds)
    {
        if (_busy || _video is null)
        {
            return;
        }

        double currentMilliseconds = _atEnd ? _video.Duration.TotalMilliseconds : Math.Max(0, _currentPositionMs);
        double targetMilliseconds = Math.Clamp(currentMilliseconds + seconds * 1000, 0, _video.Duration.TotalMilliseconds);
        SeekTo(targetMilliseconds / _video.Duration.TotalMilliseconds);
    }

    private void MarkPosition(bool isStart)
    {
        if (_busy || _video is null)
        {
            return;
        }

        TimeSpan current = _atEnd
            ? _video.Duration
            : TimeSpan.FromMilliseconds(Math.Max(0, _currentPositionMs));
        current = TimeSpan.FromMilliseconds(Math.Min(Math.Floor(_video.Duration.TotalMilliseconds), current.TotalMilliseconds));
        if (isStart)
        {
            _startBox.Text = Timecode.Format(current);
        }
        else
        {
            _endBox.Text = Timecode.Format(current);
        }
    }

    private CutRequest CurrentRequest()
    {
        if (_video is null)
        {
            throw new InvalidOperationException("Selecciona un vídeo.");
        }

        if (!Timecode.TryParse(_startBox.Text, out TimeSpan startTime) || !Timecode.TryParse(_endBox.Text, out TimeSpan endTime))
        {
            throw new ArgumentException("Escribe las marcas como hh:mm:ss.fff (ej. 00:01:25.500).");
        }

        OutputContainer container = _mkvCheckBox.Checked ? OutputContainer.Matroska : OutputContainer.Original;
        return new CutRequest(_video, startTime, endTime, container);
    }

    private void OutputContainerChanged(object sender, EventArgs e)
    {
        UpdateOutputPreview();
        AppLogger.Info("SALIDA", _mkvCheckBox.Checked ? "Seleccionado contenedor Matroska (.mkv), sin recodificación." : "Seleccionado el contenedor original, sin recodificación.");
    }

    private void UpdateOutputPreview()
    {
        if (_video is null)
        {
            return;
        }

        OutputContainer container = _mkvCheckBox.Checked ? OutputContainer.Matroska : OutputContainer.Original;
        string output = new CutRequest(_video, TimeSpan.Zero, _video.Duration, container).OutputPath;
        _outputLabel.Text = "Destino: " + Path.GetFileName(output) + (container == OutputContainer.Matroska ? "  ·  MKV sin pérdida" : "  ·  Formato original");
        _toolTip.SetToolTip(_outputLabel, output);
    }

    private void UpdateSelection()
    {
        if (_video is null)
        {
            return;
        }

        try
        {
            CutRequest request = CurrentRequest();
            _selectionLabel.Text = Timecode.Format(request.Duration);
            _selectionLabel.ForeColor = VcutTheme.AccentHover;
            double share = Math.Clamp(request.Duration.TotalSeconds / Math.Max(0.001, _video.Duration.TotalSeconds) * 100.0, 0.1, 100.0);
            _badgeClipShare.Text = $"{share:0.#}% DEL TOTAL";
            _badgeClipShare.Style = BadgeStyle.Coral;
            _timeline.SetSelection(
                request.StartTime.TotalSeconds / _video.Duration.TotalSeconds,
                request.EndTime.TotalSeconds / _video.Duration.TotalSeconds);
            _toolTip.SetToolTip(_selectionLabel, null);
            _cutButton.Enabled = !_busy;
            _previewButton.Enabled = !_busy && _player is not null;
        }
        catch (ArgumentException ex)
        {
            _selectionLabel.Text = ex.Message;
            _selectionLabel.ForeColor = VcutTheme.Warning;
            _badgeClipShare.Text = "REVISA MARCAS";
            _badgeClipShare.Style = BadgeStyle.Warning;
            _toolTip.SetToolTip(_selectionLabel, ex.Message);
            _cutButton.Enabled = false;
            _previewButton.Enabled = false;
        }
    }

    private void PreviewSelection()
    {
        if (_busy || _player is null)
        {
            return;
        }

        try
        {
            CutRequest request = CurrentRequest();
            long startMs = (long)Math.Clamp(request.StartTime.TotalMilliseconds, 0, Math.Max(0, _video.Duration.TotalMilliseconds - 1));
            _currentPositionMs = startMs;
            _timeline.Position = Math.Clamp(request.StartTime.TotalSeconds / _video.Duration.TotalSeconds, 0, 1);
            _positionLabel.Text = $"{Timecode.Format(request.StartTime)}  /  {Timecode.Format(_video.Duration)}";
            HideMonitorFrameOverlay();
            if (_atEnd)
            {
                _player.Stop();
                _player.Play(_media);
                _atEnd = false;
            }

            _player.Time = startMs;
            _pendingSeek = startMs;
            _previewSelection = true;
            _player.SetPause(false);
            _playButton.Text = "⏸  Pausar";
            _playButton.ButtonStyle = ModernButtonStyle.Secondary;
            _badgePreviewState.Text = "VISTA DE RECORTE";
            _badgePreviewState.Style = BadgeStyle.Coral;
        }
        catch (ArgumentException ex)
        {
            ShowError("Revisa las marcas", ex);
        }
    }

    private async void CutClicked(object sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        CutRequest request;
        try
        {
            request = CurrentRequest();
        }
        catch (Exception ex)
        {
            ShowError("Revisa las marcas", ex);
            return;
        }

        _operation = new CancellationTokenSource();
        _cutting = true;
        _previewSelection = false;
        _player?.SetPause(true);
        _lastProgress = null;
        _lastOutput = null;
        _progress.IsIndeterminate = false;
        _progress.Value = 0;
        _watch.Restart();
        SetBusy(true);
        _badgePreviewState.Text = "EXPORTANDO…";
        _badgePreviewState.Style = BadgeStyle.Coral;
        _statusLabel.Text = "●  Exportando recorte mediante copia directa desde RAM (sin recodificar)…";
        _statusLabel.ForeColor = VcutTheme.AccentHover;
        _remainingLabel.Text = "Restante estimado  ·  Calculando…";
        AppLogger.Info("RECORTE", $"Inicio. Origen={request.Video.FilePath}; destino={request.OutputPath}; inicio={Timecode.Format(request.StartTime)}; fin={Timecode.Format(request.EndTime)}; duración={Timecode.Format(request.Duration)}; contenedor={request.Container}. Copia directa (-c copy).");

        var progress = new Progress<CutProgress>(update =>
        {
            if (!_cutting)
            {
                return;
            }

            _lastProgress = update;
            _progress.Value = (int)Math.Clamp(update.Percent * 10, 0, 1000);
            _statusLabel.Text = update.Percent >= 99.9 && update.Percent < 100
                ? "●  Finalizando y verificando archivo…"
                : $"●  Exportando recorte  ·  {update.Percent:0}%";
        });

        try
        {
            _lastOutput = await _service.CutAsync(request, progress, _operation.Token);
            _progress.Value = 1000;
            _badgePreviewState.Text = "EXPORTADO";
            _badgePreviewState.Style = BadgeStyle.Mint;
            _statusLabel.Text = "✔  Guardado con éxito: " + Path.GetFileName(_lastOutput);
            _statusLabel.ForeColor = VcutTheme.Success;
            _toolTip.SetToolTip(_statusLabel, _lastOutput);
            _remainingLabel.Text = "Completado";
            AppLogger.Info("RECORTE", $"Completado. Destino={_lastOutput}; tamaño={new FileInfo(_lastOutput).Length} bytes; tiempo={_watch.Elapsed}.");
        }
        catch (OperationCanceledException)
        {
            _badgePreviewState.Text = "CANCELADO";
            _badgePreviewState.Style = BadgeStyle.Warning;
            _statusLabel.Text = "✖  Recorte cancelado. El original sigue intacto.";
            _statusLabel.ForeColor = VcutTheme.Warning;
            _remainingLabel.Text = "Cancelado";
            _progress.Value = 0;
            AppLogger.Info("RECORTE", "Operación cancelada; se ha limpiado el archivo temporal.");
        }
        catch (Exception ex)
        {
            _remainingLabel.Text = "No se ha guardado el recorte";
            _progress.Value = 0;
            _statusLabel.ForeColor = VcutTheme.Danger;
            ShowError("No se ha podido recortar", ex);
        }
        finally
        {
            _watch.Stop();
            _elapsedLabel.Text = "Transcurrido  " + _watch.Elapsed.ToString(@"hh\:mm\:ss");
            _cutting = false;
            FinishOperation();
        }
    }

    private void TickClock(object sender, EventArgs e)
    {
        _ambientPhase = (_ambientPhase + 0.06f) % (float)(Math.PI * 2.0);
        if (_emptyStatePanel is not null && _emptyStatePanel.Visible)
        {
            _emptyStatePanel.Invalidate();
        }

        if (_busy)
        {
            _fileCard.PhaseOffset = _ambientPhase;
            _statusCard.PhaseOffset = _ambientPhase + 1.2f;
            _fileCard.Invalidate();
            _statusCard.Invalidate();
        }

        if (_player is not null && _video is not null && !_busy)
        {
            if (_pendingSeek.HasValue && _player.Length > 0 && (_player.IsPlaying || _previewSelection))
            {
                _player.Time = _pendingSeek.Value;
                _pendingSeek = null;
            }

            if (_player.IsPlaying && !_timeline.IsDragging)
            {
                if (_monitorFrameBox.Visible)
                {
                    HideMonitorFrameOverlay();
                }

                long liveTime = _player.Time;
                if (liveTime >= 0)
                {
                    _currentPositionMs = Math.Clamp(liveTime, 0L, (long)_video.Duration.TotalMilliseconds);
                }

                TimeSpan current = _atEnd
                    ? _video.Duration
                    : TimeSpan.FromMilliseconds(_currentPositionMs);
                _timeline.Position = Math.Clamp(current.TotalSeconds / _video.Duration.TotalSeconds, 0, 1);
                _positionLabel.Text = $"{Timecode.Format(current)}  /  {Timecode.Format(_video.Duration)}";

                if (_previewSelection && Timecode.TryParse(_endBox.Text, out TimeSpan finish) && current >= finish)
                {
                    _player.SetPause(true);
                    _previewSelection = false;
                    _currentPositionMs = (long)Math.Clamp(finish.TotalMilliseconds, 0, _video.Duration.TotalMilliseconds);
                    RequestMonitorFrameUpdate(_currentPositionMs / Math.Max(1.0, _video.Duration.TotalMilliseconds), requestHighRes: true);
                    _playButton.Text = "▶  Reproducir";
                    _playButton.ButtonStyle = ModernButtonStyle.Primary;
                    _badgePreviewState.Text = "PAUSADO";
                    _badgePreviewState.Style = BadgeStyle.Amber;
                }
            }

            _playButton.Text = _player.IsPlaying ? "⏸  Pausar" : "▶  Reproducir";
            _playButton.ButtonStyle = _player.IsPlaying ? ModernButtonStyle.Secondary : ModernButtonStyle.Primary;
        }

        if (_cutting)
        {
            _elapsedLabel.Text = "Transcurrido  " + _watch.Elapsed.ToString(@"hh\:mm\:ss");
            if (_lastProgress is not null && _lastProgress.Remaining.HasValue)
            {
                TimeSpan remaining = _lastProgress.Remaining.Value - (_watch.Elapsed - _lastProgress.Elapsed);
                _remainingLabel.Text = remaining.TotalSeconds < 1
                    ? "Restante estimado  ·  Finalizando…"
                    : "Restante estimado  ~ " + remaining.ToString(@"hh\:mm\:ss");
            }
        }
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        _openButton.Enabled = !value;
        _cancelButton.Enabled = value;
        _folderButton.Enabled = !value && _lastOutput is not null;
        _ramGpuCheckBox.Enabled = !value;
        bool loaded = !value && _video is not null;
        _startBox.Enabled = loaded;
        _endBox.Enabled = loaded;
        _markStartButton.Enabled = loaded && _player is not null;
        _markEndButton.Enabled = loaded && _player is not null;
        _playButton.Enabled = loaded && _player is not null;
        _jumpBackButton.Enabled = loaded && _player is not null;
        _jumpForwardButton.Enabled = loaded && _player is not null;
        _timeline.Enabled = loaded && _player is not null;
        _mkvCheckBox.Enabled = loaded;
        _cutButton.Enabled = loaded;
        _previewButton.Enabled = loaded && _player is not null;
        UpdateSelection();
    }

    private void CancelOperation()
    {
        _cancelButton.Enabled = false;
        _statusLabel.Text = "●  Cancelando…";
        _statusLabel.ForeColor = VcutTheme.Warning;
        AppLogger.Info("OPERACIÓN", "Cancelación solicitada por el usuario.");
        _ramPreloadCts?.Cancel();
        _operation?.Cancel();
    }

    private void FinishOperation()
    {
        _operation?.Dispose();
        _operation = null;
        SetBusy(false);
        if (_closeAfterOperation)
        {
            Close();
        }
    }

    private void ClosingForm(object sender, FormClosingEventArgs e)
    {
        if (!_busy)
        {
            return;
        }

        e.Cancel = true;
        _closeAfterOperation = true;
        CancelOperation();
    }

    private void OnVideoDragEnter(object sender, DragEventArgs e)
    {
        if (!_busy && e.Data is not null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && File.Exists(files[0]))
            {
                e.Effect = DragDropEffects.Copy;
                _fileCard.BorderColor = VcutTheme.AccentHover;
                _previewCard.BorderColor = VcutTheme.AccentHover;
                _fileCard.Invalidate();
                _previewCard.Invalidate();
            }
        }
    }

    private void OnVideoDragOver(object sender, DragEventArgs e)
    {
        if (!_busy && e.Data is not null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effect = DragDropEffects.Copy;
        }
    }

    private void OnVideoDragLeave(object sender, EventArgs e)
    {
        _fileCard.BorderColor = _video is not null ? VcutTheme.Accent : VcutTheme.Border;
        _previewCard.BorderColor = VcutTheme.Border;
        _fileCard.Invalidate();
        _previewCard.Invalidate();
    }

    private async void OnVideoDrop(object sender, DragEventArgs e)
    {
        _fileCard.BorderColor = _video is not null ? VcutTheme.Accent : VcutTheme.Border;
        _previewCard.BorderColor = VcutTheme.Border;
        _fileCard.Invalidate();
        _previewCard.Invalidate();

        if (_busy || e.Data is null)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files)
        {
            AppLogger.Info("ARCHIVO", "Archivo arrastrado: " + files[0]);
            await LoadVideoAsync(files[0]);
        }
    }

    private void HandleShortcuts(object sender, KeyEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (e.Control && e.KeyCode == Keys.O)
        {
            _openButton.PerformClick();
        }
        else if (e.Control || e.Alt || ActiveControl is TextBox)
        {
            return;
        }
        else if (e.KeyCode == Keys.Space)
        {
            TogglePlayback();
        }
        else if (e.KeyCode == Keys.I)
        {
            MarkPosition(true);
        }
        else if (e.KeyCode == Keys.O)
        {
            MarkPosition(false);
        }
        else
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void RevealOutput()
    {
        if (_lastOutput is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
                Arguments = "/select,\"" + _lastOutput + "\""
            });
            AppLogger.Info("ARCHIVO", "Mostrado en el Explorador: " + _lastOutput);
        }
        catch (Exception ex)
        {
            ShowError("No se ha podido abrir la carpeta", ex);
        }
    }

    private void ShowError(string title, Exception ex)
    {
        _statusLabel.Text = "✖  " + title;
        _statusLabel.ForeColor = VcutTheme.Danger;
        AppLogger.Error(title.ToUpperInvariant(), ex.Message, ex);
        if (!_closeAfterOperation)
        {
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void PostToUi(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed && !Disposing)
                {
                    action();
                }
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ReleaseResources()
    {
        AppLogger.Info("INTERFAZ", "Liberando reproductor, miniaturas, caché RAM y recursos de la ventana.");
        _clock.Stop();
        _clock.Dispose();
        _monitorFrameCts?.Cancel();
        _monitorFrameCts?.Dispose();
        _thumbHoverCts?.Cancel();
        _thumbHoverCts?.Dispose();
        _thumbPrewarmCts?.Cancel();
        _thumbPrewarmCts?.Dispose();
        _ramPreloadCts?.Cancel();
        _ramPreloadCts?.Dispose();
        _operation?.Cancel();
        ClearDecodedBitmaps(keepCurrentDisplayed: false);
        _monitorFrameBox.Image?.Dispose();
        _monitorFrameBox.Dispose();
        _thumbnailPopup.Dispose();
        _thumbnailService.Dispose();
        _player?.Dispose();
        _media?.Dispose();
        _libVlc?.Dispose();
        _ramCache.Dispose();
        _toolTip.Dispose();
        _brandImage.Dispose();
    }

    private void SetEmptyStateVisible(bool visible)
    {
        if (_placeholder.Tag is Control emptyState)
        {
            emptyState.Visible = visible;
            if (visible)
            {
                emptyState.BringToFront();
            }
            else if (_monitorFrameBox.Image is not null && (_player is null || !_player.IsPlaying))
            {
                _monitorFrameBox.Visible = true;
                _monitorFrameBox.BringToFront();
            }
        }
    }

    private static Image LoadBrandImage()
    {
        using Stream stream = typeof(principal).Assembly.GetManifestResourceStream("VCUT.Assets.vcut-mark.png");
        if (stream is null)
        {
            return new Bitmap(1, 1);
        }

        using var source = new Bitmap(stream);
        return new Bitmap(source);
    }

    private static BadgeLabel MakeStyledBadge(string text, BadgeStyle style)
    {
        return new BadgeLabel { Text = text, Style = style, AccessibleName = text };
    }

    private static Label MakeLabel(string text, float size, bool bold = false, bool muted = false)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI Variable Text", size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = muted ? VcutTheme.TextSecondary : VcutTheme.TextPrimary,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
    }

    private static ModernButton MakeButton(string text, ModernButtonStyle style = ModernButtonStyle.Secondary)
    {
        return new ModernButton { Text = text, Dock = DockStyle.Fill, ButtonStyle = style, Margin = new Padding(3) };
    }

    private static TextBox MakeTimeBox(string accessibleName)
    {
        return new TextBox
        {
            Text = "00:00:00.000",
            AccessibleName = accessibleName,
            Dock = DockStyle.Fill,
            Font = new Font("Cascadia Mono", 11.5f, FontStyle.Bold),
            BackColor = VcutTheme.SurfaceRaised,
            ForeColor = VcutTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(3, 4, 3, 4),
            MaxLength = 18,
            TextAlign = HorizontalAlignment.Center
        };
    }

    private sealed class HighQualityPictureBox : PictureBox
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsScrubbing { get; set; }

        public HighQualityPictureBox()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            pe.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            pe.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            pe.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            pe.Graphics.CompositingQuality = CompositingQuality.HighQuality;

            Image img = Image;
            if (img is not null && ClientSize.Width > 0 && ClientSize.Height > 0)
            {
                int cw = ClientSize.Width;
                int ch = ClientSize.Height;
                int iw = img.Width;
                int ih = img.Height;
                if (iw <= cw + 2 && ih <= ch + 2 && (Math.Abs(iw - cw) <= 2 || Math.Abs(ih - ch) <= 2))
                {
                    int x = (cw - iw) / 2;
                    int y = (ch - ih) / 2;
                    if (x > 0 || y > 0)
                    {
                        pe.Graphics.Clear(BackColor);
                    }

                    pe.Graphics.DrawImageUnscaled(img, x, y);
                    return;
                }
            }

            base.OnPaint(pe);
        }
    }
}
