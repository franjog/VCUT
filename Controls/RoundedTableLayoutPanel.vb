Imports System.Drawing.Drawing2D
Imports System.ComponentModel

Public Class RoundedTableLayoutPanel
    Inherits TableLayoutPanel

    Public Sub New()
        DoubleBuffered = True
        BackColor = Color.Transparent
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
    End Sub

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property FillColor As Color = VcutTheme.Surface
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property BorderColor As Color = VcutTheme.Border
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property CornerRadius As Integer = 16

    Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        e.Graphics.Clear(VcutTheme.ParentBackground(Me, VcutTheme.Canvas))
        Dim bounds = New RectangleF(0.5F, 0.5F, Math.Max(1, Width - 1), Math.Max(1, Height - 1))
        Using path = VcutTheme.RoundedRectangle(bounds, CornerRadius), brush As New SolidBrush(FillColor), pen As New Pen(BorderColor, 1)
            e.Graphics.FillPath(brush, path)
            e.Graphics.DrawPath(pen, path)
        End Using
    End Sub
End Class
