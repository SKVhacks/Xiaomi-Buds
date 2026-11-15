namespace XiaomiBuds.Core;

/// <summary>Tiny file logger: %LOCALAPPDATA%\XiaomiBuds\app.log (truncated on each start).</summary>
public static class Log
{
    static readonly object Gate = new();
    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XiaomiBuds");
    static readonly string FilePath = Path.Combine(Dir, "app.log");

    static Log()
    {
        Directory.CreateDirectory(Dir);
        try { File.WriteAllText(FilePath, ""); } catch { }
    }

    public static void Info(string m) => Write("INFO", m);
    public static void Warn(string m) => Write("WARN", m);
    public static void Error(string m, Exception? e = null) => Write("ERROR", e is null ? m : $"{m}: {e}");

    static void Write(string level, string m)
    {
        lock (Gate)
        {
            try { File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {level} {m}{Environment.NewLine}"); }
            catch { }
        }
    }
}
