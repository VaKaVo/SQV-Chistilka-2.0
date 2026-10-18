using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace SqvChistilka.Services;

public sealed record StartupItem(string Name, string Command, string Kind, string? Hive, string? Subkey, string? Path, bool Enabled);

public static class StartupService
{
    private static string DisabledStore =>
        Path.Combine(PlatformHelper.AppDataDir, "disabled_startup.json");

    public static List<StartupItem> List()
    {
        if (PlatformHelper.IsWindows) return ListWindows();
        if (PlatformHelper.IsMac) return ListMac();
        return ListLinux();
    }

    // ---------------- Windows ----------------
    private static List<StartupItem> ListWindows()
    {
        var items = new List<StartupItem>();
        foreach (var (hiveName, subkey) in new[] { ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run"), ("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run") })
        {
            try
            {
                var hive = hiveName == "HKCU" ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;
                using var key = hive.OpenSubKey(subkey);
                if (key == null) continue;
                foreach (var name in key.GetValueNames())
                    items.Add(new StartupItem(name, key.GetValue(name)?.ToString() ?? "", "registry", hiveName, subkey, null, true));
            }
            catch { }
        }
        foreach (var d in LoadDisabled())
            items.Add(new StartupItem(d.Name, d.Command, "registry", d.Hive, d.Subkey, null, false));
        foreach (var folder in StartupFoldersWindows())
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var f in Directory.GetFiles(folder))
            {
                string fn = Path.GetFileName(f);
                bool enabled = !fn.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                items.Add(new StartupItem(enabled ? fn : fn[..^9], f, "folder", null, null, f, enabled));
            }
        }
        return items;
    }

    private static List<string> StartupFoldersWindows()
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
        Directory.CreateDirectory(PlatformHelper.AppDataDir);
        File.WriteAllText(DisabledStore, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---------------- Linux: ~/.config/autostart/*.desktop ----------------
    private static string LinuxAutostartDir =>
        Path.Combine(PlatformHelper.Home, ".config", "autostart");

    private static List<StartupItem> ListLinux()
    {
        var items = new List<StartupItem>();
        if (!Directory.Exists(LinuxAutostartDir)) return items;
        foreach (var f in Directory.GetFiles(LinuxAutostartDir, "*.desktop"))
        {
            try
            {
                var lines = File.ReadAllLines(f);
                string name = Path.GetFileNameWithoutExtension(f);
                string exec = "";
                bool hidden = false;
                foreach (var ln in lines)
                {
                    if (ln.StartsWith("Name=") && name == Path.GetFileNameWithoutExtension(f)) name = ln[5..];
                    else if (ln.StartsWith("Exec=")) exec = ln[5..];
                    else if (ln.StartsWith("Hidden=")) hidden = ln[7..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                items.Add(new StartupItem(name, exec, "desktop", null, null, f, !hidden));
            }
            catch { }
        }
        return items;
    }

    // ---------------- macOS: ~/Library/LaunchAgents/*.plist ----------------
    private static string MacAgentsDir =>
        Path.Combine(PlatformHelper.Home, "Library", "LaunchAgents");

    private static List<StartupItem> ListMac()
    {
        var items = new List<StartupItem>();
        if (!Directory.Exists(MacAgentsDir)) return items;
        foreach (var f in Directory.GetFiles(MacAgentsDir, "*.plist"))
        {
            try
            {
                var doc = XDocument.Load(f);
                string label = doc.Descendants("key").FirstOrDefault(k => k.Value == "Label")
                    ?.ElementsAfterSelf("string").FirstOrDefault()?.Value ?? Path.GetFileNameWithoutExtension(f);
                string prog = doc.Descendants("key").FirstOrDefault(k => k.Value == "ProgramArguments")
                    ?.ElementsAfterSelf("array").FirstOrDefault()
                    ?.Elements("string").Select(s => s.Value).FirstOrDefault() ?? "";
                items.Add(new StartupItem(label, prog, "plist", null, null, f, true));
            }
            catch { }
        }
        foreach (var f in Directory.GetFiles(MacAgentsDir, "*.disabled"))
        {
            try { items.Add(new StartupItem(Path.GetFileNameWithoutExtension(f), "", "plist", null, null, f, false)); }
            catch { }
        }
        return items;
    }

    // ---------------- enable / disable ----------------
    public static void Disable(StartupItem item)
    {
        if (PlatformHelper.IsWindows)
        {
            if (item.Kind == "registry" && item.Hive != null && item.Subkey != null)
            {
                var hive = item.Hive == "HKCU" ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;
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
            return;
        }
        if (PlatformHelper.IsLinux && item.Kind == "desktop" && item.Path != null)
        {
            SetDesktopHidden(item.Path, true);
            return;
        }
        if (PlatformHelper.IsMac && item.Kind == "plist" && item.Path != null && !item.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            PlatformHelper.Run("launchctl", $"unload \"{item.Path}\"");
            File.Move(item.Path, item.Path + ".disabled");
        }
    }

    public static void Enable(StartupItem item)
    {
        if (PlatformHelper.IsWindows)
        {
            if (item.Kind == "registry" && item.Hive != null && item.Subkey != null)
            {
                var hive = item.Hive == "HKCU" ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;
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
            return;
        }
        if (PlatformHelper.IsLinux && item.Kind == "desktop" && item.Path != null)
        {
            SetDesktopHidden(item.Path, false);
            return;
        }
        if (PlatformHelper.IsMac && item.Kind == "plist" && item.Path != null && item.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            string target = item.Path[..^9];
            File.Move(item.Path, target);
            PlatformHelper.Run("launchctl", $"load \"{target}\"");
        }
    }

    private static void SetDesktopHidden(string file, bool hidden)
    {
        var lines = File.ReadAllLines(file).ToList();
        lines.RemoveAll(l => l.StartsWith("Hidden="));
        lines.Add(hidden ? "Hidden=true" : "Hidden=false");
        File.WriteAllLines(file, lines);
    }

    // ---------------- self autostart ----------------
    private const string AppName = "SQV Chistilka";

    public static void SetSelfAutostart(bool enable)
    {
        if (PlatformHelper.IsWindows)
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (enable) key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
            else try { key.DeleteValue(AppName); } catch { }
            return;
        }
        if (PlatformHelper.IsLinux)
        {
            string file = Path.Combine(LinuxAutostartDir, "sqv-chistilka.desktop");
            if (enable)
            {
                Directory.CreateDirectory(LinuxAutostartDir);
                File.WriteAllText(file,
                    "[Desktop Entry]\nType=Application\nName=SQV Chistilka\n" +
                    $"Exec=\"{Environment.ProcessPath}\"\nHidden=false\nX-GNOME-Autostart-enabled=true\n");
            }
            else try { if (File.Exists(file)) File.Delete(file); } catch { }
            return;
        }
        string plist = Path.Combine(MacAgentsDir, "com.sqv.chistilka.plist");
        if (enable)
        {
            Directory.CreateDirectory(MacAgentsDir);
            File.WriteAllText(plist,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<plist version=\"1.0\"><dict>\n" +
                "<key>Label</key><string>com.sqv.chistilka</string>\n" +
                "<key>ProgramArguments</key><array>\n" +
                $"  <string>{System.Security.SecurityElement.Escape(Environment.ProcessPath ?? "")}</string>\n" +
                "</array>\n<key>RunAtLoad</key><true/>\n</dict></plist>\n");
            PlatformHelper.Run("launchctl", $"load \"{plist}\"");
        }
        else
        {
            PlatformHelper.Run("launchctl", $"unload \"{plist}\"");
            try { if (File.Exists(plist)) File.Delete(plist); } catch { }
        }
    }

    public static bool IsSelfAutostart()
    {
        if (PlatformHelper.IsWindows)
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue(AppName) != null;
        }
        if (PlatformHelper.IsLinux)
            return File.Exists(Path.Combine(LinuxAutostartDir, "sqv-chistilka.desktop"));
        return File.Exists(Path.Combine(MacAgentsDir, "com.sqv.chistilka.plist"));
    }
}
