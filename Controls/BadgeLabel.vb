Imports System.Drawing.Drawing2D
Imports System.ComponentModel

Public Enum BadgeStyle
    Mint
    Cyan
    Purple
    Warning
    Neutral
End Enum

Public Class BadgeLabel
    Inherits Label

    Private _style As BadgeStyle = BadgeStyle.Mint

    Public Sub New()
        AutoSize = False
        TextAlign = ContentAlignment.MiddleCenter
        Font = New Font("Segoe UI Variable Text", 8.0F, FontStyle.Bold)
        ForeColor = VcutTheme.Accent
        BackColor = Color.Transparent
        Margin = New Padding(4, 4, 4, 4)
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
    End Sub

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Style As BadgeStyle
        Get
            Return _style
        End Get
        Set(value As BadgeStyle)
            _style = value
            Invalidate()
        End Set
    End Property

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        e.Graphics.Clear(VcutTheme.ParentBackground(Me, VcutTheme.Surface))
        Dim bounds = New RectangleF(1.0F, 1.0F, Math.Max(1, Width - 2), Math.Max(1, Height - 2))

        Dim fill As Color
        Dim border As Color
        Dim textCol As Color

        Select Case _style
            Case BadgeStyle.Cyan
                fill = Color.FromArgb(20, 20, 55, 75)
                border = Color.FromArgb(90, 42, 176, 245)
                textCol = VcutTheme.CyanBright
            Case BadgeStyle.Purple
                fill = Color.FromArgb(25, 45, 25, 75)
                border = Color.FromArgb(90, 168, 85, 247)
                textCol = Color.FromArgb(216, 180, 254)
            Case BadgeStyle.Warning
                fill = Color.FromArgb(25, 60, 45, 20)
                border = Color.FromArgb(90, 255, 184, 108)
                textCol = VcutTheme.Warning
            Case BadgeStyle.Neutral
                fill = Color.FromArgb(30, 40, 55)
                border = Color.FromArgb(60, 80, 105)
                textCol = VcutTheme.TextSecondary
            Case Else
                fill = Color.FromArgb(21, 55, 60)
                border = Color.FromArgb(80, 45, 224, 190)
                textCol = VcutTheme.Accent
        End Select

        Using path = VcutTheme.RoundedRectangle(bounds, Math.Min(8.0F, bounds.Height / 2.0F)),
              fillBrush As New SolidBrush(fill),
              borderPen As New Pen(border, 1)
            e.Graphics.FillPath(fillBrush, path)
            e.Graphics.DrawPath(borderPen, path)
        End Using

        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textCol,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
    End Sub
End Class
