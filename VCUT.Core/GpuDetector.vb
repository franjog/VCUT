Imports System.Runtime.InteropServices

Public NotInheritable Class GpuInfo
    Public Sub New(name As String, deviceName As String, isPrimary As Boolean)
        Me.Name = name
        Me.DeviceName = deviceName
        Me.IsPrimary = isPrimary
    End Sub

    Public ReadOnly Property Name As String
    Public ReadOnly Property DeviceName As String
    Public ReadOnly Property IsPrimary As Boolean
End Class

Public NotInheritable Class GpuDetector
    Private Const AttachedToDesktop As Integer = &H1
    Private Const PrimaryDevice As Integer = &H4
    Private Const MirroringDriver As Integer = &H8
    Private Const RemoteDevice As Integer = &H4000000
    Private Const Disconnect As Integer = &H2000000

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure DisplayDevice
        Public Size As Integer
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=32)>
        Public DeviceName As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=128)>
        Public DeviceString As String
        Public StateFlags As Integer
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=128)>
        Public DeviceId As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=128)>
        Public DeviceKey As String
    End Structure

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function EnumDisplayDevices(device As String, deviceNumber As UInteger, ByRef displayDevice As DisplayDevice, flags As UInteger) As Boolean
    End Function

    Private Sub New()
    End Sub

    Public Shared Function Detect() As IReadOnlyList(Of GpuInfo)
        Dim result As New List(Of GpuInfo)()
        If Not OperatingSystem.IsWindows() Then Return result
        Dim index As UInteger
        While True
            Dim device As New DisplayDevice With {.Size = Marshal.SizeOf(Of DisplayDevice)()}
            If Not EnumDisplayDevices(Nothing, index, device, 0) Then Exit While
            Dim available = (device.StateFlags And (AttachedToDesktop Or PrimaryDevice)) <> 0
            Dim excluded = (device.StateFlags And (MirroringDriver Or RemoteDevice Or Disconnect)) <> 0
            If available AndAlso Not excluded AndAlso IsHardwareAdapterName(device.DeviceString) Then
                result.Add(New GpuInfo(device.DeviceString.Trim(), device.DeviceName.Trim(), (device.StateFlags And PrimaryDevice) <> 0))
            End If
            index += 1UI
        End While
        Return result.GroupBy(Function(gpu) gpu.Name, StringComparer.OrdinalIgnoreCase).Select(Function(group) group.First()).ToList()
    End Function

    Public Shared Function IsHardwareAdapterName(name As String) As Boolean
        If String.IsNullOrWhiteSpace(name) Then Return False
        Dim normalized = name.Trim()
        Return normalized.IndexOf("Microsoft Basic", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
               normalized.IndexOf("Remote Display", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
               normalized.IndexOf("Indirect Display", StringComparison.OrdinalIgnoreCase) < 0
    End Function
End Class
