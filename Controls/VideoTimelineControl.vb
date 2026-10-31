Imports System.Drawing.Drawing2D
Imports System.ComponentModel

Public Class VideoTimelineControl
    Inherits Control

    Private _position As Double
    Private _selectionStart As Double
    Private _selectionEnd As Double = 1
    Private _dragging As Boolean
    Private _hoverX As Single = -1
    Private _isHovered As Boolean

    Public Event SeekRequested(fraction As Double)

    Public Sub New()
        DoubleBuffered = True
        BackColor = VcutTheme.Surface
        Height = 54
        MinimumSize = New Size(120, 44)
        TabStop = True
        AccessibleName = "Línea de tiempo del vídeo"
        Cursor = Cursors.Hand
        SetStyle(ControlStyles.Selectable Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint, True)
    End Sub

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Position As Double
        Get
            Return _position
        End Get
        Set(value As Double)
            _position = Math.Clamp(value, 0, 1)
            Invalidate()
        End Set
    End Property

    Public Sub SetSelection(startFraction As Double, endFraction As Double)
        _selectionStart = Math.Clamp(startFraction, 0, 1)
        _selectionEnd = Math.Clamp(endFraction, _selectionStart, 1)
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        _isHovered = True
        Invalidate()
        MyBase.OnMouseEnter(e)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        _isHovered = False
        _hoverX = -1
        Invalidate()
        MyBase.OnMouseLeave(e)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        e.Graphics.Clear(VcutTheme.ParentBackground(Me, VcutTheme.Surface))

        Dim left = 14.0F
        Dim right = Math.Max(left + 1, Width - 14.0F)
        Dim widthAvailable = right - left
        Dim trackHeight = 10.0F
        Dim trackY = CSng(Height / 2.0F - trackHeight / 2.0F)
        Dim track = New RectangleF(left, trackY, widthAvailable, trackHeight)

        ' Track base
        Using basePath = VcutTheme.RoundedRectangle(track, trackHeight / 2.0F),
              baseBrush As New SolidBrush(If(Enabled, Color.FromArgb(28, 38, 54), Color.FromArgb(20, 26, 36))),
              borderPen As New Pen(Color.FromArgb(45, 58, 78), 1)
            e.Graphics.FillPath(baseBrush, basePath)
            e.Graphics.DrawPath(borderPen, basePath)
        End Using

        ' Selection range
        Dim selectionLeft = left + CSng(_selectionStart * widthAvailable)
        Dim selectionRight = left + CSng(_selectionEnd * widthAvailable)
        Dim selectionWidth = Math.Max(2.0F, selectionRight - selectionLeft)
        Dim selection = New RectangleF(selectionLeft, trackY, selectionWidth, trackHeight)

        Using selectionPath = VcutTheme.RoundedRectangle(selection, trackHeight / 2.0F),
              brush As New LinearGradientBrush(selection, VcutTheme.Cyan, VcutTheme.Accent, LinearGradientMode.Horizontal)
            e.Graphics.FillPath(brush, selectionPath)
        End Using

        ' Range handles
        DrawRangeHandle(e.Graphics, selectionLeft, trackY + trackHeight / 2.0F)
        DrawRangeHandle(e.Graphics, selectionRight, trackY + trackHeight / 2.0F)

        ' Hover guide line
        If _isHovered AndAlso _hoverX >= left AndAlso _hoverX <= right AndAlso Enabled Then
            Using hoverPen As New Pen(Color.FromArgb(120, 244, 247, 251), 1) With {.DashStyle = DashStyle.Dot}
                e.Graphics.DrawLine(hoverPen, _hoverX, trackY - 8, _hoverX, trackY + trackHeight + 8)
            End Using
        End If

        ' Current Playhead
        Dim playheadX = left + CSng(_position * widthAvailable)
        Dim centerY = trackY + trackHeight / 2.0F

        ' Playhead halo glow
        Using haloBrush As New SolidBrush(Color.FromArgb(90, 45, 224, 190))
            e.Graphics.FillEllipse(haloBrush, playheadX - 9, centerY - 9, 18, 18)
        End Using

        ' Playhead core
        Using playheadBrush As New SolidBrush(Color.FromArgb(244, 247, 251)),
              corePen As New Pen(VcutTheme.AccentHover, 2)
            e.Graphics.FillEllipse(playheadBrush, playheadX - 6, centerY - 6, 12, 12)
            e.Graphics.DrawEllipse(corePen, playheadX - 6, centerY - 6, 12, 12)
        End Using

        If Focused AndAlso ShowFocusCues Then
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2))
        End If
    End Sub

    Private Shared Sub DrawRangeHandle(graphics As Graphics, x As Single, y As Single)
        Using shadowBrush As New SolidBrush(Color.FromArgb(100, 0, 0, 0)),
              handleBrush As New SolidBrush(VcutTheme.TextPrimary),
              pen As New Pen(VcutTheme.Cyan, 1.5F)
            graphics.FillEllipse(shadowBrush, x - 5, y - 5, 10, 10)
            graphics.FillEllipse(handleBrush, x - 4, y - 4, 8, 8)
            graphics.DrawEllipse(pen, x - 4, y - 4, 8, 8)
        End Using
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button <> MouseButtons.Left OrElse Not Enabled Then Return
        Focus()
        _dragging = True
        Capture = True
        SeekAt(e.X)
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        _hoverX = e.X
        If _dragging AndAlso e.Button = MouseButtons.Left Then
            SeekAt(e.X)
        Else
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        _dragging = False
        Capture = False
        MyBase.OnMouseUp(e)
    End Sub

    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        Return keyData = Keys.Left OrElse keyData = Keys.Right OrElse MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.Left OrElse e.KeyCode = Keys.Right Then
            Position += If(e.KeyCode = Keys.Right, 0.005, -0.005)
            RaiseEvent SeekRequested(Position)
            e.Handled = True
        End If
        MyBase.OnKeyDown(e)
    End Sub

    Private Sub SeekAt(x As Integer)
        Dim left = 14.0F
        Dim right = Math.Max(left + 1, Width - 14.0F)
        Position = Math.Clamp((x - left) / CDbl(right - left), 0, 1)
        RaiseEvent SeekRequested(Position)
    End Sub
End Class
