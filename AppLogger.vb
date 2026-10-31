Imports System.Globalization
Imports System.IO
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text

Friend Module AppLogger
    Private ReadOnly Gate As New Object()
    Private _logPath As String
    Private _initialized As Boolean

    Friend ReadOnly Property LogPath As String
        Get
            EnsurePath()
            Return _logPath
        End Get
    End Property

    Friend Sub Initialize()
        SyncLock Gate
            If _initialized Then Return
            EnsurePath()
            RotateIfNeeded()
            _initialized = True
        End SyncLock

        AddHandler AppDomain.CurrentDomain.UnhandledException,
            Sub(sender, args)
                Dim failure = TryCast(args.ExceptionObject, Exception)
                [Error]("PROCESO", $"Excepción no controlada. Terminación={args.IsTerminating}", failure)
            End Sub
        AddHandler TaskScheduler.UnobservedTaskException,
            Sub(sender, args)
                [Error]("TAREA", "Excepción no observada en una tarea.", args.Exception)
                args.SetObserved()
            End Sub

        Dim version = Assembly.GetExecutingAssembly().GetName().Version
        Info("APLICACIÓN", $"Inicio de VCUT {version}.")
        Info("ENTORNO", $"SO={RuntimeInformation.OSDescription}; .NET={RuntimeInformation.FrameworkDescription}; Arquitectura={RuntimeInformation.ProcessArchitecture}; PID={Environment.ProcessId}.")
        Info("RUTAS", $"Base={AppContext.BaseDirectory}; Ejecutable={Environment.ProcessPath}; Registro={LogPath}.")
    End Sub

    Friend Sub Shutdown()
        If _initialized Then Info("APLICACIÓN", "Cierre normal de VCUT.")
    End Sub

    Friend Sub Info(category As String, message As String)
        Write("INFO", category, message, Nothing)
    End Sub

    Friend Sub Warning(category As String, message As String, Optional failure As Exception = Nothing)
        Write("AVISO", category, message, failure)
    End Sub

    Friend Sub [Error](category As String, message As String, failure As Exception)
        Write("ERROR", category, message, failure)
    End Sub

    Private Sub Write(level As String, category As String, message As String, failure As Exception)
        Try
            EnsurePath()
            Dim entries As New List(Of String)()
            AddPhysicalLines(entries, level, category, message)
            If failure IsNot Nothing Then AddPhysicalLines(entries, level, category, failure.ToString())
            SyncLock Gate
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath))
                Using stream As New FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                    Using writer As New StreamWriter(stream, New UTF8Encoding(False))
                        For Each entry In entries
                            writer.WriteLine(entry)
                        Next
                    End Using
                End Using
            End SyncLock
        Catch
            ' El registro nunca debe impedir que VCUT se inicie o cierre.
        End Try
    End Sub

    Private Sub AddPhysicalLines(entries As List(Of String), level As String, category As String, value As String)
        Dim normalized = If(value, String.Empty).Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
        For Each line In normalized.Split(ControlChars.Lf)
            Dim timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)
            entries.Add($"{timestamp} | {level,-5} | {category} | {line}")
        Next
    End Sub

    Private Sub EnsurePath()
        If Not String.IsNullOrEmpty(_logPath) Then Return
        Dim executableDirectory = If(String.IsNullOrWhiteSpace(Environment.ProcessPath), AppContext.BaseDirectory, Path.GetDirectoryName(Environment.ProcessPath))
        Dim preferred = Path.Combine(executableDirectory, "vcut.log")
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(preferred))
            Using stream = New FileStream(preferred, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
            End Using
            _logPath = preferred
        Catch
            _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCUT", "vcut.log")
        End Try
    End Sub

    Private Sub RotateIfNeeded()
        Try
            If Not File.Exists(_logPath) OrElse New FileInfo(_logPath).Length < 5L * 1024L * 1024L Then Return
            Dim previous = Path.Combine(Path.GetDirectoryName(_logPath), "vcut.previous.log")
            If File.Exists(previous) Then File.Delete(previous)
            File.Move(_logPath, previous)
        Catch
            ' Si no se puede rotar, se continúa añadiendo al archivo actual.
        End Try
    End Sub
End Module
