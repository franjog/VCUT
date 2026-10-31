using System.Drawing;
using System.Windows.Forms;
using VCUT.Core;

namespace VCUT;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 10f));

        AppLogger.Initialize();
        Application.ThreadException += (_, e) =>
        {
            AppLogger.Error("INTERFAZ", "Error no controlado en la aplicación.", e.Exception);
        };

        IReadOnlyList<GpuInfo> adapters = GpuDetector.Detect();
        if (adapters.Count == 0)
        {
            AppLogger.Info("GPU", "No se ha detectado un adaptador gráfico de hardware disponible.");
        }
        else
        {
            AppLogger.Info(
                "GPU",
                "Adaptadores detectados: " + string.Join("; ", adapters.Select(a => $"{a.Name} ({a.DedicatedVideoMemoryMb} MB VRAM, {a.PreferredAccelerator.ToUpperInvariant()})" + (a.IsPrimary ? " [principal]" : string.Empty))));
        }

        try
        {
            Application.Run(new principal());
        }
        finally
        {
            AppLogger.Shutdown();
        }
    }
}

