using System.IO;
using System.Text.Json;

namespace SqvChistilka.Services;

public sealed record CleanRecord(DateTime Date, long FreedBytes, string Details);

public static class HistoryService
{
    private static readonly string AppDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SQV_Chistilka");
    private static readonly string HistoryPath = Path.Combine(AppDir, "history.json");

    public static List<CleanRecord> Load()
    {
        try
        {
            if (File.Exists(HistoryPath))
                return JsonSerializer.Deserialize<List<CleanRecord>>(File.ReadAllText(HistoryPath)) ?? new();
        }
        catch { }
        return new();
    }

    public static void Add(long freed, string details)
    {
        var list = Load();
        list.Add(new CleanRecord(DateTime.Now, freed, details));
        try
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static long TotalFreed() => Load().Sum(r => r.FreedBytes);
}
