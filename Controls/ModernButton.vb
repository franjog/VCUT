Imports System.Drawing.Drawing2D
Imports System.ComponentModel

Public Enum ModernButtonStyle
    Primary
    Secondary
    Ghost
    Danger
End Enum

Public Class ModernButton
    Inherits Button

    Private _hovered As Boolean
    Private _pressed As Boolean
    Private _buttonStyle As ModernButtonStyle = ModernButtonStyle.Secondary

    Public Sub New()
        FlatStyle = FlatStyle.Flat
        FlatAppearance.BorderSize = 0
        UseVisualStyleBackColor = False
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        Cursor = Cursors.Hand
        Font = New Font("Segoe UI Variable Text", 9.5F, FontStyle.Bold)
        Height = 42
        TabStop = True
    End Sub

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property ButtonStyle As ModernButtonStyle
        Get
            Return _buttonStyle
        End Get
        Set(value As ModernButtonStyle)
            _buttonStyle = value
            Invalidate()
        End Set
    End Property

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property CornerRadius As Integer = 10

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        _hovered = True
        Invalidate()
        MyBase.OnMouseEnter(e)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        _hovered = False
        _pressed = False
        Invalidate()
        MyBase.OnMouseLeave(e)
    End Sub

    Protected Overrides Sub OnMouseDown(mevent As MouseEventArgs)
        If mevent.Button = MouseButtons.Left Then _pressed = True
        Invalidate()
        MyBase.OnMouseDown(mevent)
    End Sub

    Protected Overrides Sub OnMouseUp(mevent As MouseEventArgs)
        _pressed = False
        Invalidate()
        MyBase.OnMouseUp(mevent)
    End Sub

    Protected Overrides Sub OnEnabledChanged(e As EventArgs)
        Cursor = If(Enabled, Cursors.Hand, Cursors.Default)
        Invalidate()
        MyBase.OnEnabledChanged(e)
    End Sub

    Protected Overrides Sub OnPaint(pevent As PaintEventArgs)
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        pevent.Graphics.Clear(VcutTheme.ParentBackground(Me, VcutTheme.Surface))
        Dim bounds = New RectangleF(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2))
        Using path = VcutTheme.RoundedRectangle(bounds, CornerRadius)
            Dim fill, textColor, borderColor As Color
            ResolveColors(fill, textColor, borderColor)
            Using brush As New SolidBrush(fill)
                pevent.Graphics.FillPath(brush, path)
            End Using
            If borderColor.A > 0 Then
                Using pen As New Pen(borderColor, 1)
                    pevent.Graphics.DrawPath(pen, path)
                End Using
            End If
        End Using
        TextRenderer.DrawText(pevent.Graphics, Text, Font, ClientRectangle, ResolveTextColor(),
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        If Focused AndAlso ShowFocusCues Then
            Dim focusBounds = Rectangle.Inflate(ClientRectangle, -4, -4)
            ControlPaint.DrawFocusRectangle(pevent.Graphics, focusBounds, ResolveTextColor(), Color.Transparent)
        End If
    End Sub

    Private Sub ResolveColors(ByRef fill As Color, ByRef textColor As Color, ByRef borderColor As Color)
        textColor = ResolveTextColor()
        If Not Enabled Then
            fill = Color.FromArgb(20, 26, 38)
            borderColor = Color.FromArgb(38, 48, 64)
            Return
        End If
        Select Case ButtonStyle
            Case ModernButtonStyle.Primary
                fill = If(_pressed, VcutTheme.AccentPressed, If(_hovered, VcutTheme.AccentHover, VcutTheme.Accent))
                borderColor = If(_hovered, Color.White, Color.Transparent)
            Case ModernButtonStyle.Ghost
                fill = If(_pressed, Color.FromArgb(42, 55, 74), If(_hovered, Color.FromArgb(32, 43, 60), Color.Transparent))
                borderColor = If(_hovered, VcutTheme.BorderStrong, Color.Transparent)
            Case ModernButtonStyle.Danger
                fill = If(_pressed, Color.FromArgb(110, 35, 50), If(_hovered, Color.FromArgb(90, 32, 45), Color.FromArgb(55, 25, 36)))
                borderColor = If(_hovered, VcutTheme.Danger, Color.FromArgb(120, 50, 65))
            Case Else
                fill = If(_pressed, Color.FromArgb(36, 48, 68), If(_hovered, VcutTheme.SurfaceHover, VcutTheme.SurfaceRaised))
                borderColor = If(_hovered, VcutTheme.BorderGlow, VcutTheme.Border)
        End Select
    End Sub

    Private Function ResolveTextColor() As Color
        If Not Enabled Then Return Color.FromArgb(91, 104, 123)
        If ButtonStyle = ModernButtonStyle.Primary Then Return Color.FromArgb(6, 26, 26)
        If ButtonStyle = ModernButtonStyle.Danger Then Return Color.FromArgb(255, 195, 205)
        Return VcutTheme.TextPrimary
    End Function
End Class
