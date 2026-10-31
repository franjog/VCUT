using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace VCUT;

internal static class AppLogger
{
    private static readonly object Gate = new();
    private static string _logPath;
    private static bool _initialized;

    internal static string LogPath
    {
        get
        {
            EnsurePath();
            return _logPath;
        }
    }

    internal static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            EnsurePath();
            RotateIfNeeded();
            _initialized = true;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var failure = args.ExceptionObject as Exception;
            Error("PROCESO", $"Excepción no controlada. Terminación={args.IsTerminating}", failure);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Error("TAREA", "Excepción no observada en una tarea.", args.Exception);
            args.SetObserved();
        };

        Version version = Assembly.GetExecutingAssembly().GetName().Version;
        Info("APLICACIÓN", $"Inicio de VCUT {version}.");
        Info("ENTORNO", $"SO={RuntimeInformation.OSDescription}; .NET={RuntimeInformation.FrameworkDescription}; Arquitectura={RuntimeInformation.ProcessArchitecture}; PID={Environment.ProcessId}.");
        Info("RUTAS", $"Base={AppContext.BaseDirectory}; Ejecutable={Environment.ProcessPath}; Registro={LogPath}.");
    }

    internal static void Shutdown()
    {
        if (_initialized)
        {
            Info("APLICACIÓN", "Cierre normal de VCUT.");
        }
    }

    internal static void Info(string category, string message)
    {
        Write("INFO", category, message, null);
    }

    internal static void Warning(string category, string message, Exception failure = null)
    {
        Write("AVISO", category, message, failure);
    }

    internal static void Error(string category, string message, Exception failure)
    {
        Write("ERROR", category, message, failure);
    }

    private static void Write(string level, string category, string message, Exception failure)
    {
        try
        {
            EnsurePath();
            var entries = new List<string>();
            AddPhysicalLines(entries, level, category, message);
            if (failure is not null)
            {
                AddPhysicalLines(entries, level, category, failure.ToString());
            }

            lock (Gate)
            {
                string dir = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                foreach (string entry in entries)
                {
                    writer.WriteLine(entry);
                }
            }
        }
        catch
        {
            // El registro nunca debe impedir que VCUT se inicie o cierre.
        }
    }

    private static void AddPhysicalLines(List<string> entries, string level, string category, string value)
    {
        string normalized = (value ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        foreach (string line in normalized.Split('\n'))
        {
            string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
            entries.Add($"{timestamp} | {level,-5} | {category} | {line}");
        }
    }

    private static void EnsurePath()
    {
        if (!string.IsNullOrEmpty(_logPath))
        {
            return;
        }

        string executableDirectory = string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string preferred = Path.Combine(executableDirectory, "vcut.log");
        try
        {
            string dir = Path.GetDirectoryName(preferred);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var stream = new FileStream(preferred, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _logPath = preferred;
        }
        catch
        {
            _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCUT", "vcut.log");
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_logPath) || new FileInfo(_logPath).Length < 5L * 1024L * 1024L)
            {
                return;
            }

            string previous = Path.Combine(Path.GetDirectoryName(_logPath) ?? string.Empty, "vcut.previous.log");
            if (File.Exists(previous))
            {
                File.Delete(previous);
            }

            File.Move(_logPath, previous);
        }
        catch
        {
            // Si no se puede rotar, se continúa añadiendo al archivo actual.
        }
    }
}

