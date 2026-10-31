Imports System.Drawing.Drawing2D
Imports System.ComponentModel

Public Class ThinProgressBar
    Inherits Control

    Private _value As Integer
    Private _maximum As Integer = 1000
    Private _isIndeterminate As Boolean
    Private _animationPhase As Single
    Private ReadOnly _animTimer As New Timer With {.Interval = 30}

    Public Sub New()
        DoubleBuffered = True
        Height = 8
        MinimumSize = New Size(60, 6)
        AccessibleName = "Progreso del recorte"
        AddHandler _animTimer.Tick, Sub()
                                       _animationPhase = (_animationPhase + 0.035F) Mod 1.0F
                                       Invalidate()
                                   End Sub
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _animTimer.Stop()
            _animTimer.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Maximum As Integer
        Get
            Return _maximum
        End Get
        Set(value As Integer)
            _maximum = Math.Max(1, value)
            Invalidate()
        End Set
    End Property

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Value As Integer
        Get
            Return _value
        End Get
        Set(newValue As Integer)
            _value = Math.Clamp(newValue, 0, Math.Max(1, Maximum))
            AccessibleDescription = $"{_value * 100 \ Math.Max(1, Maximum)} por ciento"
            Invalidate()
        End Set
    End Property

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property IsIndeterminate As Boolean
        Get
            Return _isIndeterminate
        End Get
        Set(value As Boolean)
            If _isIndeterminate = value Then Return
            _isIndeterminate = value
            If value Then
                _animTimer.Start()
            ElseIf Not _isActivePulse Then
                _animTimer.Stop()
            End If
            Invalidate()
        End Set
    End Property

    Private _isActivePulse As Boolean
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property IsActivePulse As Boolean
        Get
            Return _isActivePulse
        End Get
        Set(value As Boolean)
            If _isActivePulse = value Then Return
            _isActivePulse = value
            If value OrElse _isIndeterminate Then
                _animTimer.Start()
            Else
                _animTimer.Stop()
            End If
            Invalidate()
        End Set
    End Property

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        e.Graphics.Clear(VcutTheme.ParentBackground(Me, VcutTheme.Surface))

        Dim bounds = New RectangleF(0, 0, Math.Max(1, Width), Math.Max(1, Height))
        Dim radius = Height / 2.0F

        ' Track background
        Using path = VcutTheme.RoundedRectangle(bounds, radius), brush As New SolidBrush(Color.FromArgb(28, 38, 54))
            e.Graphics.FillPath(brush, path)
        End Using

        If _isIndeterminate Then
            ' Animated scanning glow
            Dim scanWidth = Width * 0.35F
            Dim scanX = (Width + scanWidth) * _animationPhase - scanWidth
            Dim scanRect = New RectangleF(scanX, 0, scanWidth, Height)
            If scanRect.Right > 0 AndAlso scanRect.Left < Width Then
                Using path = VcutTheme.RoundedRectangle(bounds, radius)
                    e.Graphics.SetClip(path)
                    Using glowBrush As New LinearGradientBrush(scanRect, Color.Transparent, VcutTheme.Accent, LinearGradientMode.Horizontal)
                        Dim cb As New ColorBlend(3)
                        cb.Colors = {Color.FromArgb(0, 42, 176, 245), VcutTheme.AccentHover, Color.FromArgb(0, 45, 224, 190)}
                        cb.Positions = {0.0F, 0.5F, 1.0F}
                        glowBrush.InterpolationColors = cb
                        e.Graphics.FillRectangle(glowBrush, scanRect)
                    End Using
                    e.Graphics.ResetClip()
                End Using
            End If
            Return
        End If

        Dim progressWidth = CSng(Width * (_value / CDbl(Math.Max(1, Maximum))))
        If progressWidth < 1 Then Return

        Dim progress = New RectangleF(0, 0, progressWidth, Height)
        Using path = VcutTheme.RoundedRectangle(progress, radius)
            Using brush As New LinearGradientBrush(New RectangleF(0, 0, Math.Max(2, progressWidth), Height), VcutTheme.Cyan, VcutTheme.Accent, LinearGradientMode.Horizontal)
                e.Graphics.FillPath(brush, path)
            End Using
        End Using

        ' Leading edge luminous cap
        If progressWidth >= 4 Then
            Dim capRect = New RectangleF(Math.Max(0, progressWidth - 6), 0, 6, Height)
            Using capPath = VcutTheme.RoundedRectangle(capRect, radius),
                  capBrush As New SolidBrush(Color.FromArgb(200, 255, 255, 255))
                e.Graphics.FillPath(capBrush, capPath)
            End Using
        End If
    End Sub
End Class
