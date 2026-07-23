using System.Runtime.InteropServices;

namespace ISDesk.Services;

/// Ueberwacht den Fuellstand des Papierkorbs (SHQueryRecycleBin, alle 15 s)
/// und meldet Wechsel leer/voll — damit Papierkorb-Objekte in Bereichen ihr
/// Icon aktualisieren (das Shell-Icon wird bei uns sonst nur einmal geladen).
public static class RecycleBinMonitor
{
    public const string ClsidMarker = "{645FF040-5081-101B-9F08-00AA002F954E}";

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    private static System.Timers.Timer? _timer;
    private static bool _initialized;
    private static bool _lastFull;
    private static readonly object Sync = new();

    public static event Action? StateChanged;

    public static void Ensure()
    {
        lock (Sync)
        {
            if (_timer != null) return;
            _timer = new System.Timers.Timer(15000) { AutoReset = true };
            _timer.Elapsed += (_, _) => Poll();
            _timer.Start();
        }
        Poll();
    }

    private static void Poll()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            if (SHQueryRecycleBin(null, ref info) != 0) return;

            var full = info.i64NumItems > 0;
            bool changed;
            lock (Sync)
            {
                changed = _initialized && full != _lastFull;
                _lastFull = full;
                _initialized = true;
            }
            if (changed) StateChanged?.Invoke();
        }
        catch (Exception)
        {
            // Abfrage fehlgeschlagen → naechster Versuch beim folgenden Tick
        }
    }
}
