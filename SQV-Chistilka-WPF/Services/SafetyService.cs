using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SqvChistilka.Services;

public static class SafetyService
{
    private static readonly string AppDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SQV_Chistilka");
    private static readonly string WhiteListPath = Path.Combine(AppDir, "whitelist.json");

    private static List<string> _cache = new();
    private static bool _loaded;

    public static List<string> LoadWhitelist()
    {
        if (_loaded) return new List<string>(_cache);
        try
        {
            if (File.Exists(WhiteListPath))
                _cache = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(WhiteListPath)) ?? new();
        }
        catch { _cache = new(); }
        // Системные папки — всегда в защите, даже если юзер их не добавлял
        foreach (var sys in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.System), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (!string.IsNullOrEmpty(sys) && !_cache.Any(w => PathsEqual(w, sys)))
                _cache.Add(sys);
        }
        _loaded = true;
        return new List<string>(_cache);
    }

    public static void SaveWhitelist(List<string> items)
    {
        _cache = items.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _loaded = true;
        try
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(WhiteListPath, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static bool IsProtected(string path)
    {
        var list = LoadWhitelist();
        string full;
        try { full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
        catch { return false; }
        foreach (var w in list)
        {
            try
            {
                string ww = Path.GetFullPath(w).TrimEnd(Path.DirectorySeparatorChar);
                if (full.Equals(ww, StringComparison.OrdinalIgnoreCase) || full.StartsWith(ww + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
        }
        return false;
    }

    private static bool PathsEqual(string a, string b)
    {
        try { return Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
        catch { return a.Equals(b, StringComparison.OrdinalIgnoreCase); }
    }

    public static bool CreateRestorePoint(string description = "SQV Chistilka")
    {
        try
        {
            // Требует прав админа. Через powershell, чтобы не тащить System.Management.
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS'\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(60000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }
}
