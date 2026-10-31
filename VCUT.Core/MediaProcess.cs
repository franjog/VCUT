using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace VCUT.Core;

public sealed class ProcessResult
{
    public ProcessResult(int exitCode, string output, string errors)
    {
        ExitCode = exitCode;
        Output = output;
        Errors = errors;
    }

    public int ExitCode { get; }
    public string Output { get; }
    public string Errors { get; }
}

public static class MediaProcess
{
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        Action<string> outputLine = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = info };
        if (!process.Start())
        {
            throw new IOException("No se ha podido iniciar el motor de vídeo.");
        }

        try
        {
            process.StandardInput.Close();
        }
        catch
        {
            // Input stream close is best effort.
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process has already exited.
            }
            catch (Win32Exception)
            {
                // Exit can race with cancellation.
            }
        });

        Task<string> outputTask = ReadAsync(process.StandardOutput, outputLine, 4_000_000);
        Task<string> errorTask = ReadAsync(process.StandardError, null, 16_000);
        await Task.WhenAll(process.WaitForExitAsync( CancellationToken.None), outputTask, errorTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessResult(process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }

    private static async Task<string> ReadAsync(StreamReader reader, Action<string> onLine, int limit)
    {
        var buffer = new StringBuilder();
        while (true)
        {
            string line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            onLine?.Invoke(line);
            buffer.AppendLine(line);
            if (buffer.Length > limit)
            {
                buffer.Remove(0, buffer.Length - limit);
            }
        }

        return buffer.ToString();
    }
}

