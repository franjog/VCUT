Imports System.Drawing.Drawing2D

Friend Module VcutTheme
    Friend ReadOnly Canvas As Color = Color.FromArgb(11, 15, 24)
    Friend ReadOnly Surface As Color = Color.FromArgb(20, 27, 40)
    Friend ReadOnly SurfaceRaised As Color = Color.FromArgb(27, 36, 52)
    Friend ReadOnly SurfaceHover As Color = Color.FromArgb(35, 47, 66)
    Friend ReadOnly Border As Color = Color.FromArgb(51, 64, 84)
    Friend ReadOnly BorderStrong As Color = Color.FromArgb(71, 88, 112)
    Friend ReadOnly BorderGlow As Color = Color.FromArgb(90, 45, 224, 190)
    Friend ReadOnly TextPrimary As Color = Color.FromArgb(244, 247, 251)
    Friend ReadOnly TextSecondary As Color = Color.FromArgb(166, 177, 196)
    Friend ReadOnly TextQuiet As Color = Color.FromArgb(116, 130, 151)
    Friend ReadOnly Accent As Color = Color.FromArgb(45, 224, 190)
    Friend ReadOnly AccentHover As Color = Color.FromArgb(78, 238, 207)
    Friend ReadOnly AccentPressed As Color = Color.FromArgb(30, 190, 161)
    Friend ReadOnly Cyan As Color = Color.FromArgb(42, 176, 245)
    Friend ReadOnly CyanBright As Color = Color.FromArgb(0, 229, 255)
    Friend ReadOnly Purple As Color = Color.FromArgb(168, 85, 247)
    Friend ReadOnly Warning As Color = Color.FromArgb(255, 184, 108)
    Friend ReadOnly Danger As Color = Color.FromArgb(247, 112, 130)
    Friend ReadOnly Success As Color = Color.FromArgb(52, 211, 153)

    Friend Function RoundedRectangle(bounds As RectangleF, radius As Single) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim diameter = Math.Max(1.0F, radius * 2.0F)
        If bounds.Width <= diameter OrElse bounds.Height <= diameter Then
            path.AddRectangle(bounds)
            path.CloseFigure()
            Return path
        End If
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90)
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90)
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90)
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Friend Function ParentBackground(control As Control, fallback As Color) As Color
        Dim current = control.Parent
        While current IsNot Nothing
            Dim candidate = current.BackColor
            If candidate.A = 255 Then Return candidate
            current = current.Parent
        End While
        Return fallback
    End Function
End Module
