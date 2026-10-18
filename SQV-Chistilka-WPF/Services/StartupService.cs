using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace SqvChistilka.Services;

public sealed record StartupItem(string Name, string Command, string Kind, string? Hive, string? Subkey, string? Path, bool Enabled);

public static class StartupService
{
    private static readonly string DisabledStore =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SQV_Chistilka", "disabled_startup.json");

    public static List<StartupItem> List()
    {
        var items = new List<StartupItem>();
        foreach (var (hiveName, subkey) in new[] { ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run"), ("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run") })
        {
            try
            {
                var hive = hiveName == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using var key = hive.OpenSubKey(subkey);
                if (key == null) continue;
                foreach (var name in key.GetValueNames())
                    items.Add(new StartupItem(name, key.GetValue(name)?.ToString() ?? "", "registry", hiveName, subkey, null, true));
            }
            catch { }
        }
        foreach (var d in LoadDisabled())
            items.Add(new StartupItem(d.Name, d.Command, "registry", d.Hive, d.Subkey, null, false));

        foreach (var folder in StartupFolders())
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var f in Directory.GetFiles(folder))
            {
                string fn = Path.GetFileName(f);
                bool enabled = !fn.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                string disp = enabled ? fn : fn[..^9];
                items.Add(new StartupItem(disp, f, "folder", null, null, f, enabled));
            }
        }
        return items;
    }

    private static List<string> StartupFolders()
    {
        var l = new List<string>();
        string? appdata = Environment.GetEnvironmentVariable("APPDATA");
        string? programdata = Environment.GetEnvironmentVariable("PROGRAMDATA");
        if (appdata != null) l.Add(Path.Combine(appdata, "Microsoft", "Windows", "Start Menu", "Programs", "Startup"));
        if (programdata != null) l.Add(Path.Combine(programdata, "Microsoft", "Windows", "Start Menu", "Programs", "Startup"));
        return l;
    }

    private sealed record DisabledEntry(string Name, string Command, string Hive, string Subkey);

    private static List<DisabledEntry> LoadDisabled()
    {
        try
        {
            if (!File.Exists(DisabledStore)) return new();
            return JsonSerializer.Deserialize<List<DisabledEntry>>(File.ReadAllText(DisabledStore)) ?? new();
        }
        catch { return new(); }
    }

    private static void SaveDisabled(List<DisabledEntry> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DisabledStore)!);
        File.WriteAllText(DisabledStore, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Disable(StartupItem item)
    {
        if (item.Kind == "registry" && item.Hive != null && item.Subkey != null)
        {
            var hive = item.Hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
            using var key = hive.OpenSubKey(item.Subkey, true);
            try { key?.DeleteValue(item.Name); } catch { }
            var store = LoadDisabled();
            store.RemoveAll(s => s.Name == item.Name && s.Hive == item.Hive);
            store.Add(new DisabledEntry(item.Name, item.Command, item.Hive, item.Subkey));
            SaveDisabled(store);
        }
        else if (item.Kind == "folder" && item.Path != null && !item.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            File.Move(item.Path, item.Path + ".disabled");
        }
    }

    public static void Enable(StartupItem item)
    {
        if (item.Kind == "registry" && item.Hive != null && item.Subkey != null)
        {
            var hive = item.Hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
            using var key = hive.OpenSubKey(item.Subkey, true);
            key?.SetValue(item.Name, item.Command);
            var store = LoadDisabled();
            store.RemoveAll(s => s.Name == item.Name && s.Hive == item.Hive);
            SaveDisabled(store);
        }
        else if (item.Kind == "folder" && item.Path != null && item.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            File.Move(item.Path, item.Path[..^9]);
        }
    }

    private const string AppName = "SQV Chistilka 2";

    public static void SetSelfAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key == null) return;
        if (enable)
            key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        else
            try { key.DeleteValue(AppName); } catch { }
    }

    public static bool IsSelfAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue(AppName) != null;
    }
}
