Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Threading

Public NotInheritable Class ProcessResult
    Public Sub New(exitCode As Integer, output As String, errors As String)
        Me.ExitCode = exitCode
        Me.Output = output
        Me.Errors = errors
    End Sub
    Public ReadOnly Property ExitCode As Integer
    Public ReadOnly Property Output As String
    Public ReadOnly Property Errors As String
End Class

Public NotInheritable Class MediaProcess
    Public Shared Async Function RunAsync(executable As String, arguments As IEnumerable(Of String), cancellationToken As CancellationToken, Optional outputLine As Action(Of String) = Nothing) As Task(Of ProcessResult)
        cancellationToken.ThrowIfCancellationRequested()
        Dim info As New ProcessStartInfo(executable) With {
            .UseShellExecute = False, .CreateNoWindow = True,
            .RedirectStandardInput = True,
            .RedirectStandardOutput = True, .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8, .StandardErrorEncoding = Encoding.UTF8
        }
        For Each argument In arguments
            info.ArgumentList.Add(argument)
        Next
        Using process As New Process With {.StartInfo = info}
            If Not process.Start() Then Throw New IOException("No se ha podido iniciar el motor de vídeo.")
            Try
                process.StandardInput.Close()
            Catch ex As Exception
                ' Input stream close is best effort.
            End Try
            Using registration = cancellationToken.Register(
                Sub()
                    Try
                        process.Kill(entireProcessTree:=True)
                    Catch ex As InvalidOperationException
                        ' The process has already exited.
                    Catch ex As System.ComponentModel.Win32Exception
                        ' Exit can race with cancellation.
                    End Try
                End Sub)
                Dim outputTask = ReadAsync(process.StandardOutput, outputLine, 4000000)
                Dim errorTask = ReadAsync(process.StandardError, Nothing, 16000)
                Await Task.WhenAll(process.WaitForExitAsync(), outputTask, errorTask).ConfigureAwait(False)
                cancellationToken.ThrowIfCancellationRequested()
                Return New ProcessResult(process.ExitCode, Await outputTask.ConfigureAwait(False), Await errorTask.ConfigureAwait(False))
            End Using
        End Using
    End Function

    Private Shared Async Function ReadAsync(reader As StreamReader, onLine As Action(Of String), limit As Integer) As Task(Of String)
        Dim buffer As New StringBuilder()
        While True
            Dim line = Await reader.ReadLineAsync().ConfigureAwait(False)
            If line Is Nothing Then Exit While
            onLine?.Invoke(line)
            buffer.AppendLine(line)
            If buffer.Length > limit Then buffer.Remove(0, buffer.Length - limit)
        End While
        Return buffer.ToString()
    End Function
End Class
