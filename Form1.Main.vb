Imports System.Diagnostics
Imports System.IO
Imports System.Threading
Imports LibVLCSharp.Shared
Imports LibVLCSharp.WinForms
Imports VCUT.Core

Partial Public Class Form1
    Private Shared ReadOnly PageColor As Color = VcutTheme.Canvas
    Private Shared ReadOnly CardColor As Color = VcutTheme.Surface
    Private Shared ReadOnly AccentColor As Color = VcutTheme.Accent
    Private ReadOnly _service As New VideoService(Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg"))
    Private ReadOnly _clock As New System.Windows.Forms.Timer With {.Interval = 100}
    Private ReadOnly _watch As New Stopwatch()
    Private ReadOnly _gpus As IReadOnlyList(Of GpuInfo) = GpuDetector.Detect()
    Private ReadOnly _openButton As ModernButton = MakeButton("📂  Abrir vídeo", ModernButtonStyle.Primary)
    Private ReadOnly _playButton As ModernButton = MakeButton("▶  Reproducir", ModernButtonStyle.Primary)
    Private ReadOnly _jumpBackButton As ModernButton = MakeButton("⏪  − 5 s", ModernButtonStyle.Ghost)
    Private ReadOnly _jumpForwardButton As ModernButton = MakeButton("+ 5 s  ⏩", ModernButtonStyle.Ghost)
    Private ReadOnly _cutButton As ModernButton = MakeButton("✂  Exportar recorte", ModernButtonStyle.Primary)
    Private ReadOnly _cancelButton As ModernButton = MakeButton("✖  Cancelar", ModernButtonStyle.Danger)
    Private ReadOnly _folderButton As ModernButton = MakeButton("📁  Mostrar archivo", ModernButtonStyle.Secondary)
    Private ReadOnly _markStartButton As ModernButton = MakeButton("Usar posición  [I]", ModernButtonStyle.Secondary)
    Private ReadOnly _markEndButton As ModernButton = MakeButton("Usar posición  [O]", ModernButtonStyle.Secondary)
    Private ReadOnly _previewButton As ModernButton = MakeButton("▶  Previsualizar recorte", ModernButtonStyle.Secondary)
    Private ReadOnly _fileName As Label = MakeLabel("Arrastra un vídeo aquí o pulsa Abrir vídeo", 11.0F, True)
    Private ReadOnly _metaContainer As FlowLayoutPanel = New FlowLayoutPanel With {.Dock = DockStyle.Fill, .WrapContents = False, .AutoScroll = False, .Margin = New Padding(0), .Padding = New Padding(0)}
    Private ReadOnly _badgeFormat As BadgeLabel = MakeStyledBadge("SIN VÍDEO", BadgeStyle.Neutral)
    Private ReadOnly _badgeRes As BadgeLabel = MakeStyledBadge("—", BadgeStyle.Mint)
    Private ReadOnly _badgeCodec As BadgeLabel = MakeStyledBadge("—", BadgeStyle.Purple)
    Private ReadOnly _badgeDuration As BadgeLabel = MakeStyledBadge("—", BadgeStyle.Cyan)
    Private ReadOnly _positionLabel As Label = MakeLabel("00:00:00.000  /  00:00:00.000", 10.0F, True, False)
    Private ReadOnly _selectionLabel As Label = MakeLabel("Aún no hay una selección válida", 11.5F, True)
    Private ReadOnly _outputLabel As Label = MakeLabel("Se guardará en la carpeta del original", 9.0F, False, True)
    Private ReadOnly _statusLabel As Label = MakeLabel("●  Listo para abrir un vídeo", 10.0F)
    Private ReadOnly _elapsedLabel As Label = MakeLabel("Transcurrido  ·  00:00:00", 8.5F, False, True)
    Private ReadOnly _remainingLabel As Label = MakeLabel("Restante estimado  ·  —", 8.5F, False, True)
    Private ReadOnly _startBox As TextBox = MakeTimeBox("Marca de inicio")
    Private ReadOnly _endBox As TextBox = MakeTimeBox("Marca de fin")
    Private ReadOnly _mkvCheckBox As New CheckBox With {.Text = "Guardar como MKV (remultiplexado sin recodificar)", .Dock = DockStyle.Fill, .Font = New Font("Segoe UI Variable Text", 9.5F), .ForeColor = VcutTheme.TextPrimary, .BackColor = Color.Transparent, .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand, .Padding = New Padding(3, 0, 0, 0), .AccessibleName = "Guardar como MKV"}
    Private ReadOnly _timeline As New VideoTimelineControl With {.Dock = DockStyle.Fill, .Enabled = False}
    Private ReadOnly _progress As New ThinProgressBar With {.Dock = DockStyle.Fill, .Maximum = 1000}
    Private ReadOnly _videoView As New VideoView With {.Dock = DockStyle.Fill, .BackColor = Color.Black}
    Private ReadOnly _placeholder As Label = MakeLabel("Abre o arrastra un archivo de vídeo para comenzar" & vbCrLf & "MP4 · MKV · MOV · AVI · WebM · TS · ProRes y más", 11.0F, True, True)
    Private ReadOnly _body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(0)}
    Private ReadOnly _previewCard As New RoundedTableLayoutPanel With {.Dock = DockStyle.Fill, .FillColor = CardColor, .BorderColor = VcutTheme.Border, .CornerRadius = 16, .Padding = New Padding(16), .Margin = New Padding(0, 0, 14, 0)}
    Private ReadOnly _marksCard As New RoundedTableLayoutPanel With {.Dock = DockStyle.Fill, .FillColor = CardColor, .BorderColor = VcutTheme.Border, .CornerRadius = 16, .Padding = New Padding(20), .Margin = New Padding(0)}
    Private ReadOnly _fileCard As New RoundedTableLayoutPanel With {.Dock = DockStyle.Fill, .FillColor = VcutTheme.Surface, .BorderColor = VcutTheme.Border, .CornerRadius = 14, .Padding = New Padding(14, 8, 14, 8), .Margin = New Padding(0, 0, 0, 10)}
    Private ReadOnly _statusCard As New RoundedTableLayoutPanel With {.Dock = DockStyle.Fill, .FillColor = VcutTheme.Surface, .BorderColor = VcutTheme.Border, .CornerRadius = 14, .Padding = New Padding(16, 10, 16, 10), .Margin = New Padding(0, 10, 0, 0)}
    Private ReadOnly _brandSubtitle As Label = MakeLabel("Rápido. Sin recodificar.", 8.5F, False, True)
    Private ReadOnly _brandImage As Image = LoadBrandImage()
    Private ReadOnly _toolTip As New ToolTip()
    Private _headerBrandStyle As ColumnStyle
    Private _libVlc As LibVLC
    Private _player As MediaPlayer
    Private _media As Media
    Private _video As VideoInfo
    Private _operation As CancellationTokenSource
    Private _lastProgress As CutProgress
    Private _lastOutput As String
    Private _busy, _cutting, _narrow, _closeAfterOperation, _previewSelection, _atEnd As Boolean
    Private _pendingSeek As Long?
    Private _playerPreloadTask As Task

    Public Sub New()
        InitializeComponent()
        BuildInterface()
        StartPlayerPreload()
        AddHandler _openButton.Click, AddressOf OpenClicked
        AddHandler _playButton.Click, Sub() TogglePlayback()
        AddHandler _jumpBackButton.Click, Sub() SeekRelative(-5)
        AddHandler _jumpForwardButton.Click, Sub() SeekRelative(5)
        AddHandler _markStartButton.Click, Sub() MarkPosition(True)
        AddHandler _markEndButton.Click, Sub() MarkPosition(False)
        AddHandler _startBox.TextChanged, Sub() UpdateSelection()
        AddHandler _endBox.TextChanged, Sub() UpdateSelection()
        AddHandler _mkvCheckBox.CheckedChanged, AddressOf OutputContainerChanged
        AddHandler _cutButton.Click, AddressOf CutClicked
        AddHandler _cancelButton.Click, Sub() CancelOperation()
        AddHandler _folderButton.Click, Sub() RevealOutput()
        AddHandler _previewButton.Click, Sub() PreviewSelection()
        AddHandler _timeline.SeekRequested, Sub(fraction) SeekTo(fraction)
        AddHandler _clock.Tick, AddressOf TickClock
        AddHandler Resize, Sub() AdaptLayout()
        AddHandler FormClosing, AddressOf ClosingForm
        AddHandler Disposed, Sub() ReleaseResources()
        AddHandler DragEnter, AddressOf OnVideoDragEnter
        AddHandler DragOver, AddressOf OnVideoDragOver
        AddHandler DragLeave, AddressOf OnVideoDragLeave
        AddHandler DragDrop, AddressOf OnVideoDrop
        AddHandler KeyDown, AddressOf HandleShortcuts
        _clock.Start()
        SetBusy(False)
        AdaptLayout()
        AppLogger.Info("INTERFAZ", $"Formulario creado. DPI={DeviceDpi}; tamaño={ClientSize.Width}x{ClientSize.Height}.")
    End Sub

    Private Sub StartPlayerPreload()
        _playerPreloadTask = Task.Run(
            Sub()
                Try
                    Global.LibVLCSharp.Shared.Core.Initialize()
                    Dim options As New List(Of String) From {"--no-video-title-show", "--no-snapshot-preview"}
                    If _gpus.Count > 0 Then options.Add("--avcodec-hw=any")
                    Dim vlc = New LibVLC(options.ToArray())
                    Dim mp = New MediaPlayer(vlc)
                    PostToUi(Sub()
                                 If _libVlc Is Nothing Then
                                     _libVlc = vlc
                                     _player = mp
                                     _videoView.MediaPlayer = _player
                                     AttachPlayerEvents()
                                     If _gpus.Count > 0 Then
                                         AppLogger.Info("GPU", "Decodificación por hardware preparada en segundo plano para LibVLC. Adaptadores: " & String.Join("; ", _gpus.Select(Function(adapter) adapter.Name)))
                                     Else
                                         AppLogger.Info("GPU", "Previsualización por software preparada en segundo plano: no hay adaptador de hardware.")
                                     End If
                                 Else
                                     mp.Dispose()
                                     vlc.Dispose()
                                 End If
                             End Sub)
                Catch ex As Exception
                    AppLogger.Warning("REPRODUCTOR", "Error durante la inicialización previa de LibVLC en segundo plano.", ex)
                End Try
            End Sub)
    End Sub

    Private Async Function EnsurePlayerAsync() As Task
        If _player IsNot Nothing Then Return
        If _playerPreloadTask IsNot Nothing Then
            Await _playerPreloadTask.ConfigureAwait(True)
        End If
        If _player Is Nothing Then
            Global.LibVLCSharp.Shared.Core.Initialize()
            Dim options As New List(Of String) From {"--no-video-title-show", "--no-snapshot-preview"}
            If _gpus.Count > 0 Then options.Add("--avcodec-hw=any")
            _libVlc = New LibVLC(options.ToArray())
            _player = New MediaPlayer(_libVlc)
            _videoView.MediaPlayer = _player
            AttachPlayerEvents()
            If _gpus.Count > 0 Then
                AppLogger.Info("GPU", "Decodificación por hardware solicitada a LibVLC para la previsualización. Adaptadores: " & String.Join("; ", _gpus.Select(Function(adapter) adapter.Name)))
            Else
                AppLogger.Info("GPU", "Previsualización por software: no hay un adaptador de hardware disponible.")
            End If
        End If
    End Function

    Private Sub AttachPlayerEvents()
        AddHandler _player.EndReached, Sub() PostToUi(Sub()
                                                        _atEnd = True
                                                        _previewSelection = False
                                                        _playButton.Text = "▶  Reproducir"
                                                        _playButton.ButtonStyle = ModernButtonStyle.Primary
                                                    End Sub)
        AddHandler _player.EncounteredError, Sub()
                                                 AppLogger.Warning("REPRODUCTOR", "LibVLC notificó un error de reproducción.")
                                                 PostToUi(Sub() _statusLabel.Text = "●  VLC no puede reproducir este archivo. Puedes introducir las marcas manualmente.")
                                             End Sub
    End Sub

    Private Sub BuildInterface()
        SuspendLayout()
        Text = "VCUT · Recorte de vídeo sin pérdida"
        Font = New Font("Segoe UI Variable Text", 10)
        BackColor = PageColor
        ForeColor = VcutTheme.TextPrimary
        ClientSize = New Size(1260, 840)
        MinimumSize = New Size(700, 640)
        StartPosition = FormStartPosition.CenterScreen
        KeyPreview = True
        AllowDrop = True
        AutoScaleMode = AutoScaleMode.Dpi
        Try
            Icon = Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        Catch ex As ArgumentException
        End Try

        Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4, .Padding = New Padding(20, 10, 20, 14), .BackColor = PageColor, .Margin = New Padding(0)}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each row In New RowStyle() {New RowStyle(SizeType.Absolute, 74), New RowStyle(SizeType.Percent, 100), New RowStyle(SizeType.Absolute, 94), New RowStyle(SizeType.Absolute, 28)}
            root.RowStyles.Add(row)
        Next

        _fileCard.Padding = New Padding(12, 6, 12, 6)
        _fileCard.Margin = New Padding(0, 0, 0, 8)
        _fileCard.ColumnCount = 4
        _fileCard.RowCount = 1
        _fileCard.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 46))
        _headerBrandStyle = New ColumnStyle(SizeType.Absolute, 220)
        _fileCard.ColumnStyles.Add(_headerBrandStyle)
        _fileCard.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        _fileCard.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150))

        Dim logo As New PictureBox With {.Dock = DockStyle.Fill, .Image = _brandImage, .SizeMode = PictureBoxSizeMode.Zoom, .Margin = New Padding(0, 4, 8, 4), .AccessibleName = "Logotipo de VCUT"}
        _fileCard.Controls.Add(logo, 0, 0)

        Dim brandText As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .Margin = New Padding(0, 0, 10, 0)}
        brandText.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 68))
        brandText.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        brandText.Controls.Add(MakeLabel("VCUT", 16.5F, True), 0, 0)
        brandText.Controls.Add(_brandSubtitle, 1, 0)
        _fileCard.Controls.Add(brandText, 1, 0)

        Dim sourcePanel As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .Margin = New Padding(0)}
        sourcePanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 26))
        sourcePanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
        sourcePanel.Controls.Add(_fileName, 0, 0)

        _badgeFormat.Height = 22
        _badgeFormat.Width = 80
        _badgeRes.Height = 22
        _badgeRes.Width = 84
        _badgeCodec.Height = 22
        _badgeCodec.Width = 76
        _badgeDuration.Height = 22
        _badgeDuration.Width = 105

        _metaContainer.Controls.Add(_badgeFormat)
        _metaContainer.Controls.Add(_badgeRes)
        _metaContainer.Controls.Add(_badgeCodec)
        _metaContainer.Controls.Add(_badgeDuration)
        sourcePanel.Controls.Add(_metaContainer, 0, 1)

        _fileCard.Controls.Add(sourcePanel, 2, 0)

        _openButton.Dock = DockStyle.None
        _openButton.Anchor = AnchorStyles.Right
        _openButton.Size = New Size(138, 42)
        _openButton.Margin = New Padding(6, 0, 0, 0)
        _fileCard.Controls.Add(_openButton, 3, 0)
        root.Controls.Add(_fileCard, 0, 0)

        BuildPreviewCard()
        BuildMarksCard()
        _body.Controls.Add(_previewCard, 0, 0)
        _body.Controls.Add(_marksCard, 1, 0)
        root.Controls.Add(_body, 0, 1)

        _statusCard.ColumnCount = 4
        _statusCard.RowCount = 3
        _statusCard.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 55))
        _statusCard.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 45))
        _statusCard.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 126))
        _statusCard.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150))
        _statusCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 8))
        _statusCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 34))
        _statusCard.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        _statusCard.Controls.Add(_progress, 0, 0)
        _statusCard.SetColumnSpan(_progress, 4)
        _statusCard.Controls.Add(_statusLabel, 0, 1)
        _statusCard.SetColumnSpan(_statusLabel, 2)
        _cancelButton.Margin = New Padding(4, 1, 4, 1)
        _folderButton.Margin = New Padding(4, 1, 0, 1)
        _statusCard.Controls.Add(_cancelButton, 2, 1)
        _statusCard.SetRowSpan(_cancelButton, 2)
        _statusCard.Controls.Add(_folderButton, 3, 1)
        _statusCard.SetRowSpan(_folderButton, 2)
        _statusCard.Controls.Add(_elapsedLabel, 0, 2)
        _statusCard.Controls.Add(_remainingLabel, 1, 2)
        root.Controls.Add(_statusCard, 0, 2)

        Dim note = MakeLabel("COPIA DIRECTA  ·  Mantiene las pistas originales sin recodificar. El punto de corte puede ajustarse al fotograma clave anterior.", 8.5F, False, True)
        note.AutoEllipsis = False
        root.Controls.Add(note, 0, 3)
        Controls.Add(root)

        _toolTip.SetToolTip(_openButton, "Abrir un vídeo (Ctrl+O). También puedes arrastrarlo a la ventana.")
        _toolTip.SetToolTip(_startBox, "Horas:minutos:segundos.milisegundos · 00:01:25.500")
        _toolTip.SetToolTip(_endBox, "Horas:minutos:segundos.milisegundos · 00:02:10.000")
        _toolTip.SetToolTip(_mkvCheckBox, "Remultiplexa todas las pistas a Matroska sin recodificarlas. Úsalo si el contenedor original no las admite.")
        ResumeLayout(True)
    End Sub

    Private Sub BuildPreviewCard()
        _previewCard.ColumnCount = 1
        _previewCard.RowCount = 4
        _previewCard.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each row In New RowStyle() {New RowStyle(SizeType.Absolute, 36), New RowStyle(SizeType.Percent, 100), New RowStyle(SizeType.Absolute, 56), New RowStyle(SizeType.Absolute, 46)}
            _previewCard.RowStyles.Add(row)
        Next

        Dim previewHeader As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Margin = New Padding(0)}
        previewHeader.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        previewHeader.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        previewHeader.Controls.Add(MakeLabel("Previsualización del vídeo", 12.0F, True), 0, 0)
        Dim originalBadge = MakeStyledBadge("ORIGINAL", BadgeStyle.Mint)
        originalBadge.Width = 110
        originalBadge.Height = 24
        previewHeader.Controls.Add(originalBadge, 1, 0)
        _previewCard.Controls.Add(previewHeader, 0, 0)

        Dim canvas As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.FromArgb(4, 7, 13), .Margin = New Padding(0, 4, 0, 0), .Padding = New Padding(1)}
        _placeholder.TextAlign = ContentAlignment.MiddleCenter
        _placeholder.AutoEllipsis = False
        canvas.Controls.Add(_videoView)

        Dim emptyState As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .BackColor = Color.FromArgb(7, 11, 20), .Margin = New Padding(0)}
        emptyState.RowStyles.Add(New RowStyle(SizeType.Percent, 45))
        emptyState.RowStyles.Add(New RowStyle(SizeType.Absolute, 100))
        emptyState.RowStyles.Add(New RowStyle(SizeType.Percent, 55))
        Dim emptyContent As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .Margin = New Padding(0)}
        emptyContent.RowStyles.Add(New RowStyle(SizeType.Absolute, 48))
        emptyContent.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        emptyContent.Controls.Add(New PictureBox With {.Dock = DockStyle.Fill, .Image = _brandImage, .SizeMode = PictureBoxSizeMode.Zoom, .Margin = New Padding(0)}, 0, 0)
        emptyContent.Controls.Add(_placeholder, 0, 1)
        emptyState.Controls.Add(emptyContent, 0, 1)
        canvas.Controls.Add(emptyState)
        _placeholder.Tag = emptyState
        emptyState.BringToFront()
        _previewCard.Controls.Add(canvas, 0, 1)
        _previewCard.Controls.Add(_timeline, 0, 2)

        Dim playback As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 5, .Margin = New Padding(0)}
        playback.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 86))
        playback.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140))
        playback.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 86))
        playback.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        playback.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 240))

        _jumpBackButton.Margin = New Padding(0, 2, 4, 2)
        _playButton.Margin = New Padding(0, 2, 4, 2)
        _jumpForwardButton.Margin = New Padding(0, 2, 4, 2)
        playback.Controls.Add(_jumpBackButton, 0, 0)
        playback.Controls.Add(_playButton, 1, 0)
        playback.Controls.Add(_jumpForwardButton, 2, 0)

        _positionLabel.Font = New Font("Cascadia Mono", 9.5F, FontStyle.Bold)
        _positionLabel.ForeColor = VcutTheme.CyanBright
        _positionLabel.TextAlign = ContentAlignment.MiddleRight
        playback.Controls.Add(_positionLabel, 4, 0)
        _previewCard.Controls.Add(playback, 0, 3)
    End Sub

    Private Sub BuildMarksCard()
        _marksCard.ColumnCount = 2
        _marksCard.RowCount = 10
        _marksCard.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        _marksCard.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        For Each rowHeight In New Single() {36, 44, 22, 42, 44, 46, 36, 40, 52}
            _marksCard.RowStyles.Add(New RowStyle(SizeType.Absolute, rowHeight))
        Next
        _marksCard.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim cutHeader As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Margin = New Padding(0)}
        cutHeader.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        cutHeader.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110))
        cutHeader.Controls.Add(MakeLabel("Tu recorte", 12.0F, True), 0, 0)
        Dim badgeLossless = MakeStyledBadge("SIN PÉRDIDA", BadgeStyle.Cyan)
        badgeLossless.Width = 100
        badgeLossless.Height = 24
        cutHeader.Controls.Add(badgeLossless, 1, 0)

        AddWide(_marksCard, cutHeader, 0)
        AddWide(_marksCard, _selectionLabel, 1)

        _marksCard.Controls.Add(MakeLabel("PUNTO DE INICIO  [I]", 8.5F, True, True), 0, 2)
        _marksCard.Controls.Add(MakeLabel("PUNTO DE FIN  [O]", 8.5F, True, True), 1, 2)
        _marksCard.Controls.Add(_startBox, 0, 3)
        _marksCard.Controls.Add(_endBox, 1, 3)
        _marksCard.Controls.Add(_markStartButton, 0, 4)
        _marksCard.Controls.Add(_markEndButton, 1, 4)

        AddWide(_marksCard, _previewButton, 5)
        AddWide(_marksCard, _mkvCheckBox, 6)
        AddWide(_marksCard, _outputLabel, 7)
        AddWide(_marksCard, _cutButton, 8)

        Dim help = MakeLabel("Escribe los tiempos como hh:mm:ss.fff (ej. 00:01:25.500) o pulsa I / O mientras reproduces.", 8.5F, False, True)
        help.AutoEllipsis = False
        help.TextAlign = ContentAlignment.TopLeft
        help.Padding = New Padding(0, 6, 0, 0)
        AddWide(_marksCard, help, 9)
    End Sub

    Private Shared Sub AddWide(panel As TableLayoutPanel, control As Control, row As Integer)
        panel.Controls.Add(control, 0, row)
        panel.SetColumnSpan(control, 2)
    End Sub

    Private Sub AdaptLayout()
        If _body.Parent Is Nothing Then Return
        Dim narrow = ClientSize.Width < CInt(1020 * DeviceDpi / 96.0)
        If _body.ColumnStyles.Count > 0 AndAlso narrow = _narrow Then Return
        _narrow = narrow
        _brandSubtitle.Visible = Not narrow
        If _headerBrandStyle IsNot Nothing Then _headerBrandStyle.Width = If(narrow, 88, 220)
        _body.SuspendLayout()
        _body.ColumnStyles.Clear()
        _body.RowStyles.Clear()
        If narrow Then
            _body.SetCellPosition(_marksCard, New TableLayoutPanelCellPosition(0, 1))
            _body.ColumnCount = 1
            _body.RowCount = 2
            _body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            _body.RowStyles.Add(New RowStyle(SizeType.Absolute, CSng(430 * DeviceDpi / 96.0)))
            _body.RowStyles.Add(New RowStyle(SizeType.Absolute, CSng(450 * DeviceDpi / 96.0)))
            _previewCard.Margin = New Padding(0, 0, 0, 12)
            _marksCard.Padding = New Padding(16)
        Else
            _body.ColumnCount = 2
            _body.RowCount = 1
            _body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            _body.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, CSng(395 * DeviceDpi / 96.0)))
            _body.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            _body.SetCellPosition(_marksCard, New TableLayoutPanelCellPosition(1, 0))
            _previewCard.Margin = New Padding(0, 0, 12, 0)
            _marksCard.Padding = New Padding(18)
        End If
        _body.AutoScroll = True
        _body.AutoScrollMinSize = New Size(0, CInt(If(narrow, 880, 420) * DeviceDpi / 96.0))
        _body.ResumeLayout(True)
    End Sub

    Private Async Sub OpenClicked(sender As Object, e As EventArgs)
        If _busy Then Return
        Using dialog As New OpenFileDialog With {
            .Title = "Selecciona un vídeo",
            .Filter = "Vídeos compatibles|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.ts;*.mts;*.m2ts;*.wmv;*.asf;*.flv;*.f4v;*.mpg;*.mpeg;*.vob;*.3gp;*.3g2;*.ogv;*.ogg;*.mxf;*.dv;*.divx;*.rm;*.rmvb;*.mjpeg;*.mjpg;*.y4m;*.nut;*.wtv;*.dvr-ms;*.amv;*.roq;*.nsv|MP4 / MOV / M4V|*.mp4;*.mov;*.m4v|Matroska / WebM|*.mkv;*.webm|MPEG / Transport Stream|*.mpg;*.mpeg;*.ts;*.mts;*.m2ts;*.vob|AVI / Windows Media|*.avi;*.divx;*.wmv;*.asf;*.wtv;*.dvr-ms|Todos los archivos|*.*",
            .CheckFileExists = True
        }
            If dialog.ShowDialog(Me) = DialogResult.OK Then
                AppLogger.Info("ARCHIVO", "Seleccionado mediante diálogo: " & dialog.FileName)
                Await LoadVideoAsync(dialog.FileName)
            End If
        End Using
    End Sub

    Private Async Function LoadVideoAsync(filePath As String) As Task
        If _busy Then Return
        _operation = New CancellationTokenSource()
        _lastOutput = Nothing
        _progress.Value = 100
        _progress.IsIndeterminate = True
        SetBusy(True)
        _statusLabel.Text = "●  Analizando el vídeo con FFprobe…"
        _statusLabel.ForeColor = VcutTheme.CyanBright
        AppLogger.Info("CARGA", "Analizando vídeo: " & filePath)

        Try
            ' Fast probe with cancellation support
            Dim probeProgress = New Progress(Of Double)(
                Sub(p)
                    If _busy AndAlso Not _cutting Then
                        _progress.Value = CInt(Math.Clamp(p * 500, 100, 500))
                    End If
                End Sub)

            _video = Await _service.ProbeAsync(filePath, _operation.Token, probeProgress)

            _progress.Value = 600
            _statusLabel.Text = "●  Preparando motor de reproducción y GPU…"

            _startBox.Text = Timecode.Format(TimeSpan.Zero)
            _endBox.Text = Timecode.Format(TimeSpan.FromMilliseconds(Math.Floor(_video.Duration.TotalMilliseconds)))
            _fileName.Text = Path.GetFileName(filePath)
            _toolTip.SetToolTip(_fileName, _video.FilePath)

            ' Dynamic badges
            Dim ext = Path.GetExtension(filePath).TrimStart("."c).ToUpperInvariant()
            _badgeFormat.Text = If(String.IsNullOrEmpty(ext), "VÍDEO", ext)
            _badgeFormat.Style = BadgeStyle.Cyan

            Dim resText = If(_video.Width > 0 AndAlso _video.Height > 0, $"{_video.Width}×{_video.Height}", "Desconocida")
            If _video.Height >= 2160 Then
                resText = $"4K UHD ({_video.Width}×{_video.Height})"
            ElseIf _video.Height >= 1080 Then
                resText = $"1080p FHD ({_video.Width}×{_video.Height})"
            ElseIf _video.Height >= 720 Then
                resText = $"720p HD ({_video.Width}×{_video.Height})"
            End If
            _badgeRes.Text = resText
            _badgeRes.Width = Math.Max(84, resText.Length * 7 + 16)
            _badgeRes.Style = BadgeStyle.Mint

            Dim codecDisplay = If(String.IsNullOrEmpty(_video.Codec) OrElse _video.Codec = "desconocido", "VÍDEO", _video.Codec.ToUpperInvariant())
            If Not String.IsNullOrEmpty(_video.AudioCodec) Then
                codecDisplay &= " · " & _video.AudioCodec.ToUpperInvariant()
            End If
            _badgeCodec.Text = codecDisplay
            _badgeCodec.Width = Math.Max(76, codecDisplay.Length * 7 + 16)
            _badgeCodec.Style = BadgeStyle.Purple

            _badgeDuration.Text = Timecode.Format(_video.Duration)
            _badgeDuration.Style = BadgeStyle.Cyan

            UpdateOutputPreview()
            _timeline.Position = 0
            _timeline.SetSelection(0, 1)
            _positionLabel.Text = $"00:00:00.000 / {Timecode.Format(_video.Duration)}"
            _previewSelection = False
            _atEnd = False
            _pendingSeek = Nothing

            _progress.Value = 800
            _statusLabel.Text = "●  Inicializando previsualización…"

            Try
                Await EnsurePlayerAsync()
                _player.Stop()
                _player.Media = Nothing
                _media?.Dispose()

                _media = New Media(_libVlc, _video.FilePath, FromType.FromPath)
                _media.AddOption(":start-paused")
                SetEmptyStateVisible(False)

                If Not _player.Play(_media) Then
                    Throw New InvalidOperationException("El reproductor no pudo abrir el vídeo.")
                End If

                _fileCard.BorderColor = Color.FromArgb(90, 45, 224, 190)
                _fileCard.Invalidate()

                _progress.IsIndeterminate = False
                _progress.Value = 1000
                _statusLabel.Text = "●  Vídeo listo. Selecciona las marcas de inicio y fin."
                _statusLabel.ForeColor = VcutTheme.Success
                AppLogger.Info("CARGA", $"Vídeo cargado con éxito. Duración={Timecode.Format(_video.Duration)}; resolución={_video.Width}x{_video.Height}; códec={_video.Codec}; ruta={_video.FilePath}.")
            Catch ex As Exception
                _placeholder.Text = "Previsualización visual no disponible" & vbCrLf & "Puedes introducir las marcas de tiempo y recortar sin recodificar."
                SetEmptyStateVisible(True)
                _statusLabel.Text = "●  Vídeo analizado; previsualización directa no disponible."
                _statusLabel.ForeColor = VcutTheme.Warning
                _toolTip.SetToolTip(_placeholder, ex.Message)
                AppLogger.Warning("REPRODUCTOR", "El vídeo se cargó, pero la previsualización no está disponible.", ex)
            End Try
        Catch ex As OperationCanceledException
            _progress.IsIndeterminate = False
            _progress.Value = 0
            _statusLabel.Text = "●  Carga cancelada."
            _statusLabel.ForeColor = VcutTheme.Warning
            AppLogger.Info("CARGA", "Carga cancelada por el usuario: " & filePath)
        Catch ex As Exception
            _progress.IsIndeterminate = False
            _progress.Value = 0
            ShowError("No se ha podido cargar el vídeo", ex)
        Finally
            _progress.IsIndeterminate = False
            FinishOperation()
        End Try
    End Function

    Private Sub TogglePlayback()
        If _busy OrElse _video Is Nothing OrElse _player Is Nothing Then Return
        _previewSelection = False
        If _atEnd Then
            _player.Stop()
            _player.Play(_media)
            _player.SetPause(False)
            _atEnd = False
            _playButton.Text = "⏸  Pausar"
            _playButton.ButtonStyle = ModernButtonStyle.Secondary
        Else
            Dim shouldPause = _player.IsPlaying
            _player.SetPause(shouldPause)
            _playButton.Text = If(shouldPause, "▶  Reproducir", "⏸  Pausar")
            _playButton.ButtonStyle = If(shouldPause, ModernButtonStyle.Primary, ModernButtonStyle.Secondary)
        End If
    End Sub

    Private Sub SeekTo(fraction As Double)
        If _busy OrElse _video Is Nothing OrElse _player Is Nothing Then Return
        _previewSelection = False
        Dim target = CLng(Math.Min(fraction * _video.Duration.TotalMilliseconds, Math.Max(0, _video.Duration.TotalMilliseconds - 1)))
        If _atEnd Then
            _player.Stop()
            _player.Play(_media)
            _atEnd = False
            _pendingSeek = target
        Else
            _player.Time = target
        End If
        _timeline.Position = Math.Clamp(fraction, 0, 1)
    End Sub

    Private Sub SeekRelative(seconds As Double)
        If _busy OrElse _video Is Nothing OrElse _player Is Nothing Then Return
        Dim currentMilliseconds = If(_atEnd, _video.Duration.TotalMilliseconds, Math.Max(0, _player.Time))
        Dim targetMilliseconds = Math.Clamp(currentMilliseconds + seconds * 1000, 0, _video.Duration.TotalMilliseconds)
        SeekTo(targetMilliseconds / _video.Duration.TotalMilliseconds)
    End Sub

    Private Sub MarkPosition(isStart As Boolean)
        If _busy OrElse _video Is Nothing Then Return
        Dim current = If(_atEnd, _video.Duration, TimeSpan.FromMilliseconds(Math.Max(0, If(_player Is Nothing, 0L, _player.Time))))
        current = TimeSpan.FromMilliseconds(Math.Min(Math.Floor(_video.Duration.TotalMilliseconds), current.TotalMilliseconds))
        If isStart Then
            _startBox.Text = Timecode.Format(current)
        Else
            _endBox.Text = Timecode.Format(current)
        End If
    End Sub

    Private Function CurrentRequest() As CutRequest
        If _video Is Nothing Then Throw New InvalidOperationException("Selecciona un vídeo.")
        Dim startTime, endTime As TimeSpan
        If Not Timecode.TryParse(_startBox.Text, startTime) OrElse Not Timecode.TryParse(_endBox.Text, endTime) Then Throw New ArgumentException("Escribe las marcas como hh:mm:ss.fff (ej. 00:01:25.500).")
        Dim container = If(_mkvCheckBox.Checked, OutputContainer.Matroska, OutputContainer.Original)
        Return New CutRequest(_video, startTime, endTime, container)
    End Function

    Private Sub OutputContainerChanged(sender As Object, e As EventArgs)
        UpdateOutputPreview()
        AppLogger.Info("SALIDA", If(_mkvCheckBox.Checked, "Seleccionado contenedor Matroska (.mkv), sin recodificación.", "Seleccionado el contenedor original, sin recodificación."))
    End Sub

    Private Sub UpdateOutputPreview()
        If _video Is Nothing Then Return
        Dim container = If(_mkvCheckBox.Checked, OutputContainer.Matroska, OutputContainer.Original)
        Dim output = New CutRequest(_video, TimeSpan.Zero, _video.Duration, container).OutputPath
        _outputLabel.Text = "Destino: " & Path.GetFileName(output) & If(container = OutputContainer.Matroska, "  ·  MKV sin recodificar", "  ·  Mismo formato original")
        _toolTip.SetToolTip(_outputLabel, output)
    End Sub

    Private Sub UpdateSelection()
        If _video Is Nothing Then Return
        Try
            Dim request = CurrentRequest()
            _selectionLabel.Text = "⏱  Duración del recorte  ·  " & Timecode.Format(request.Duration)
            _selectionLabel.ForeColor = AccentColor
            _timeline.SetSelection(request.StartTime.TotalSeconds / _video.Duration.TotalSeconds,
                                   request.EndTime.TotalSeconds / _video.Duration.TotalSeconds)
            _toolTip.SetToolTip(_selectionLabel, Nothing)
            _cutButton.Enabled = Not _busy
            _previewButton.Enabled = Not _busy AndAlso _player IsNot Nothing
        Catch ex As ArgumentException
            _selectionLabel.Text = ex.Message
            _selectionLabel.ForeColor = VcutTheme.Warning
            _toolTip.SetToolTip(_selectionLabel, ex.Message)
            _cutButton.Enabled = False
            _previewButton.Enabled = False
        End Try
    End Sub

    Private Sub PreviewSelection()
        If _busy OrElse _player Is Nothing Then Return
        Try
            Dim request = CurrentRequest()
            SeekTo(request.StartTime.TotalSeconds / _video.Duration.TotalSeconds)
            _previewSelection = True
            _player.SetPause(False)
            _playButton.Text = "⏸  Pausar"
            _playButton.ButtonStyle = ModernButtonStyle.Secondary
        Catch ex As ArgumentException
            ShowError("Revisa las marcas", ex)
        End Try
    End Sub

    Private Async Sub CutClicked(sender As Object, e As EventArgs)
        If _busy Then Return
        Dim request As CutRequest
        Try
            request = CurrentRequest()
        Catch ex As Exception
            ShowError("Revisa las marcas", ex)
            Return
        End Try

        _operation = New CancellationTokenSource()
        _cutting = True
        _previewSelection = False
        _player?.SetPause(True)
        _lastProgress = Nothing
        _lastOutput = Nothing
        _progress.IsIndeterminate = False
        _progress.Value = 0
        _watch.Restart()
        SetBusy(True)
        _statusLabel.Text = "●  Recortando mediante copia directa (sin recodificar)…"
        _statusLabel.ForeColor = VcutTheme.CyanBright
        _remainingLabel.Text = "Restante estimado  ·  Calculando…"
        AppLogger.Info("RECORTE", $"Inicio. Origen={request.Video.FilePath}; destino={request.OutputPath}; inicio={Timecode.Format(request.StartTime)}; fin={Timecode.Format(request.EndTime)}; duración={Timecode.Format(request.Duration)}; contenedor={request.Container}. Copia directa: la GPU no interviene porque no se decodifica ni recodifica.")

        Dim progress As New Progress(Of CutProgress)(
            Sub(update)
                If Not _cutting Then Return
                _lastProgress = update
                _progress.Value = CInt(Math.Clamp(update.Percent * 10, 0, 1000))
                _statusLabel.Text = If(update.Percent >= 99.9 AndAlso update.Percent < 100, "●  Finalizando y verificando archivo…", $"●  Recortando  ·  {update.Percent:0}%")
            End Sub)

        Try
            _lastOutput = Await _service.CutAsync(request, progress, _operation.Token)
            _progress.Value = 1000
            _statusLabel.Text = "✔  Guardado con éxito: " & Path.GetFileName(_lastOutput)
            _statusLabel.ForeColor = VcutTheme.Success
            _toolTip.SetToolTip(_statusLabel, _lastOutput)
            _remainingLabel.Text = "Completado"
            AppLogger.Info("RECORTE", $"Completado. Destino={_lastOutput}; tamaño={New FileInfo(_lastOutput).Length} bytes; tiempo={_watch.Elapsed}.")
        Catch ex As OperationCanceledException
            _statusLabel.Text = "✖  Recorte cancelado. El original sigue intacto."
            _statusLabel.ForeColor = VcutTheme.Warning
            _remainingLabel.Text = "Cancelado"
            _progress.Value = 0
            AppLogger.Info("RECORTE", "Operación cancelada; se ha limpiado el archivo temporal.")
        Catch ex As Exception
            _remainingLabel.Text = "No se ha guardado el recorte"
            _progress.Value = 0
            _statusLabel.ForeColor = VcutTheme.Danger
            ShowError("No se ha podido recortar", ex)
        Finally
            _watch.Stop()
            _elapsedLabel.Text = "Transcurrido  " & _watch.Elapsed.ToString("hh\:mm\:ss")
            _cutting = False
            FinishOperation()
        End Try
    End Sub

    Private Sub TickClock(sender As Object, e As EventArgs)
        If _player IsNot Nothing AndAlso _video IsNot Nothing AndAlso Not _busy Then
            If _pendingSeek.HasValue AndAlso _player.Length > 0 Then
                _player.Time = _pendingSeek.Value
                _player.SetPause(Not _previewSelection)
                _pendingSeek = Nothing
            End If
            Dim current = If(_atEnd, _video.Duration, TimeSpan.FromMilliseconds(Math.Clamp(_player.Time, 0L, CLng(_video.Duration.TotalMilliseconds))))
            _timeline.Position = Math.Clamp(current.TotalSeconds / _video.Duration.TotalSeconds, 0, 1)
            _positionLabel.Text = $"{Timecode.Format(current)}  /  {Timecode.Format(_video.Duration)}"
            _playButton.Text = If(_player.IsPlaying, "⏸  Pausar", "▶  Reproducir")
            _playButton.ButtonStyle = If(_player.IsPlaying, ModernButtonStyle.Secondary, ModernButtonStyle.Primary)

            If _previewSelection Then
                Dim finish As TimeSpan
                If Timecode.TryParse(_endBox.Text, finish) AndAlso current >= finish Then
                    _player.SetPause(True)
                    _previewSelection = False
                    _playButton.Text = "▶  Reproducir"
                    _playButton.ButtonStyle = ModernButtonStyle.Primary
                End If
            End If
        End If

        If _cutting Then
            _elapsedLabel.Text = "Transcurrido  " & _watch.Elapsed.ToString("hh\:mm\:ss")
            If _lastProgress IsNot Nothing AndAlso _lastProgress.Remaining.HasValue Then
                Dim remaining = _lastProgress.Remaining.Value - (_watch.Elapsed - _lastProgress.Elapsed)
                _remainingLabel.Text = If(remaining.TotalSeconds < 1, "Restante estimado  ·  Finalizando…", "Restante estimado  ~ " & remaining.ToString("hh\:mm\:ss"))
            End If
        End If
    End Sub

    Private Sub SetBusy(value As Boolean)
        _busy = value
        _openButton.Enabled = Not value
        _cancelButton.Enabled = value
        _folderButton.Enabled = Not value AndAlso _lastOutput IsNot Nothing
        Dim loaded = Not value AndAlso _video IsNot Nothing
        _startBox.Enabled = loaded
        _endBox.Enabled = loaded
        _markStartButton.Enabled = loaded AndAlso _player IsNot Nothing
        _markEndButton.Enabled = loaded AndAlso _player IsNot Nothing
        _playButton.Enabled = loaded AndAlso _player IsNot Nothing
        _jumpBackButton.Enabled = loaded AndAlso _player IsNot Nothing
        _jumpForwardButton.Enabled = loaded AndAlso _player IsNot Nothing
        _timeline.Enabled = loaded AndAlso _player IsNot Nothing
        _mkvCheckBox.Enabled = loaded
        _cutButton.Enabled = loaded
        _previewButton.Enabled = loaded AndAlso _player IsNot Nothing
        UpdateSelection()
    End Sub

    Private Sub CancelOperation()
        _cancelButton.Enabled = False
        _statusLabel.Text = "●  Cancelando…"
        _statusLabel.ForeColor = VcutTheme.Warning
        AppLogger.Info("OPERACIÓN", "Cancelación solicitada por el usuario.")
        _operation?.Cancel()
    End Sub

    Private Sub FinishOperation()
        _operation?.Dispose()
        _operation = Nothing
        SetBusy(False)
        If _closeAfterOperation Then Close()
    End Sub

    Private Sub ClosingForm(sender As Object, e As FormClosingEventArgs)
        If Not _busy Then Return
        e.Cancel = True
        _closeAfterOperation = True
        CancelOperation()
    End Sub

    Private Sub OnVideoDragEnter(sender As Object, e As DragEventArgs)
        If Not _busy AndAlso e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop) Then
            Dim files = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
            If files IsNot Nothing AndAlso files.Length = 1 AndAlso File.Exists(files(0)) Then
                e.Effect = DragDropEffects.Copy
                _fileCard.BorderColor = VcutTheme.Accent
                _fileCard.Invalidate()
            End If
        End If
    End Sub

    Private Sub OnVideoDragOver(sender As Object, e As DragEventArgs)
        If Not _busy AndAlso e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effect = DragDropEffects.Copy
        End If
    End Sub

    Private Sub OnVideoDragLeave(sender As Object, e As EventArgs)
        If _video IsNot Nothing Then
            _fileCard.BorderColor = Color.FromArgb(90, 45, 224, 190)
        Else
            _fileCard.BorderColor = VcutTheme.Border
        End If
        _fileCard.Invalidate()
    End Sub

    Private Async Sub OnVideoDrop(sender As Object, e As DragEventArgs)
        If _video IsNot Nothing Then
            _fileCard.BorderColor = Color.FromArgb(90, 45, 224, 190)
        Else
            _fileCard.BorderColor = VcutTheme.Border
        End If
        _fileCard.Invalidate()

        If _busy OrElse e.Data Is Nothing Then Return
        Dim files = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If files IsNot Nothing AndAlso files.Length = 1 Then
            AppLogger.Info("ARCHIVO", "Archivo arrastrado: " & files(0))
            Await LoadVideoAsync(files(0))
        End If
    End Sub

    Private Sub HandleShortcuts(sender As Object, e As KeyEventArgs)
        If _busy Then Return
        If e.Control AndAlso e.KeyCode = Keys.O Then
            _openButton.PerformClick()
        ElseIf e.Control OrElse e.Alt Then
            Return
        ElseIf TypeOf ActiveControl Is TextBox Then
            Return
        ElseIf e.KeyCode = Keys.Space Then
            TogglePlayback()
        ElseIf e.KeyCode = Keys.I Then
            MarkPosition(True)
        ElseIf e.KeyCode = Keys.O Then
            MarkPosition(False)
        Else
            Return
        End If
        e.Handled = True
        e.SuppressKeyPress = True
    End Sub

    Private Sub RevealOutput()
        If _lastOutput Is Nothing Then Return
        Try
            Process.Start(New ProcessStartInfo("explorer.exe") With {.UseShellExecute = True, .Arguments = "/select,""" & _lastOutput & """"})
            AppLogger.Info("ARCHIVO", "Mostrado en el Explorador: " & _lastOutput)
        Catch ex As Exception
            ShowError("No se ha podido abrir la carpeta", ex)
        End Try
    End Sub

    Private Sub ShowError(title As String, ex As Exception)
        _statusLabel.Text = "✖  " & title
        _statusLabel.ForeColor = VcutTheme.Danger
        AppLogger.Error(title.ToUpperInvariant(), ex.Message, ex)
        If Not _closeAfterOperation Then MessageBox.Show(Me, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
    End Sub

    Private Sub PostToUi(action As Action)
        If IsDisposed OrElse Disposing OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(Sub()
                            If Not IsDisposed AndAlso Not Disposing Then action()
                        End Sub)
        Catch ex As InvalidOperationException
        End Try
    End Sub

    Private Sub ReleaseResources()
        AppLogger.Info("INTERFAZ", "Liberando reproductor y recursos de la ventana.")
        _clock.Stop()
        _clock.Dispose()
        _operation?.Cancel()
        _player?.Dispose()
        _media?.Dispose()
        _libVlc?.Dispose()
        _toolTip.Dispose()
        _brandImage.Dispose()
    End Sub

    Private Sub SetEmptyStateVisible(visible As Boolean)
        Dim emptyState = TryCast(_placeholder.Tag, Control)
        If emptyState IsNot Nothing Then emptyState.Visible = visible
    End Sub

    Private Shared Function LoadBrandImage() As Image
        Using stream = GetType(Form1).Assembly.GetManifestResourceStream("VCUT.Assets.vcut-mark.png")
            If stream Is Nothing Then Return New Bitmap(1, 1)
            Using source As New Bitmap(stream)
                Return New Bitmap(source)
            End Using
        End Using
    End Function

    Private Shared Function MakeStyledBadge(text As String, style As BadgeStyle) As BadgeLabel
        Return New BadgeLabel With {.Text = text, .Style = style, .AccessibleName = text}
    End Function

    Private Shared Function MakeLabel(text As String, size As Single, Optional bold As Boolean = False, Optional muted As Boolean = False) As Label
        Return New Label With {.Text = text, .Font = New Font("Segoe UI Variable Text", size, If(bold, FontStyle.Bold, FontStyle.Regular)), .ForeColor = If(muted, VcutTheme.TextSecondary, VcutTheme.TextPrimary), .BackColor = Color.Transparent, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft, .AutoEllipsis = True, .Margin = New Padding(0)}
    End Function

    Private Shared Function MakeButton(text As String, Optional style As ModernButtonStyle = ModernButtonStyle.Secondary) As ModernButton
        Return New ModernButton With {.Text = text, .Dock = DockStyle.Fill, .ButtonStyle = style, .Margin = New Padding(3)}
    End Function

    Private Shared Function MakeTimeBox(accessibleName As String) As TextBox
        Return New TextBox With {.Text = "00:00:00.000", .AccessibleName = accessibleName, .Dock = DockStyle.Fill, .Font = New Font("Cascadia Mono", 11.5F, FontStyle.Bold), .BackColor = VcutTheme.SurfaceRaised, .ForeColor = VcutTheme.TextPrimary, .BorderStyle = BorderStyle.FixedSingle, .Margin = New Padding(3, 4, 3, 4), .MaxLength = 18, .TextAlign = HorizontalAlignment.Center}
    End Function
End Class