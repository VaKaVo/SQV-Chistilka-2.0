using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SqvChistilka.Services;

public sealed record CleanCategory(string Id, string Name, string? Path, string Kind, string Desc, bool IsPro = false);

public static class CleanerService
{
    private static void AddIfExists(List<CleanCategory> cats, string id, string name, string path, string desc, bool isPro = false)
    {
        try { if (Directory.Exists(path)) cats.Add(new CleanCategory(id, name, path, "dir", desc, isPro)); } catch { }
    }

    private static string? FindSteamDir()
    {
        try
        {
            foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey(@"Software\Valve\Steam");
                var p = key?.GetValue("SteamPath")?.ToString();
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
            }
        }
        catch { }
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string guess = Path.Combine(pf86, "Steam");
        return Directory.Exists(guess) ? guess : null;
    }

    public static List<CleanCategory> GetTempCategories()
    {
        var cats = new List<CleanCategory>();
        string? temp = Environment.GetEnvironmentVariable("TEMP") ?? Environment.GetEnvironmentVariable("TMP");
        string local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        string windir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        string appdata = Environment.GetEnvironmentVariable("APPDATA") ?? "";

        if (!string.IsNullOrEmpty(temp))
            cats.Add(new CleanCategory("win_temp", "Временные файлы пользователя", temp, "dir", "Файлы, которые программы создают во время работы"));
        cats.Add(new CleanCategory("sys_temp", "Временные файлы системы", Path.Combine(windir, "Temp"), "dir", "Системная временная папка Windows"));
        if (!string.IsNullOrEmpty(local))
        {
            cats.Add(new CleanCategory("chrome_cache", "Кэш Google Chrome", Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"), "dir", "Кэш сайтов из Chrome (пароли и история не затронуты)"));
            cats.Add(new CleanCategory("edge_cache", "Кэш Microsoft Edge", Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"), "dir", "Кэш сайтов из Edge"));
            try
            {
                var ff = Directory.GetDirectories(Path.Combine(local, "Mozilla", "Firefox", "Profiles"), "*", SearchOption.TopDirectoryOnly)
                    .Select(p => Path.Combine(p, "cache2")).FirstOrDefault(Directory.Exists);
                if (ff != null)
                    cats.Add(new CleanCategory("firefox_cache", "Кэш Mozilla Firefox", ff, "dir", "Кэш сайтов из Firefox"));
            }
            catch { }
        }
        cats.Add(new CleanCategory("win_update", "Кэш обновлений Windows", Path.Combine(windir, "SoftwareDistribution", "Download"), "dir", "Уже установленные файлы обновлений"));
        cats.Add(new CleanCategory("win_logs", "Журналы Windows", Path.Combine(windir, "Logs"), "dir", "Служебные лог-файлы ОС"));
        if (!string.IsNullOrEmpty(appdata))
        {
            cats.Add(new CleanCategory("recent", "Список недавних файлов", Path.Combine(appdata, "Microsoft", "Windows", "Recent"), "dir", "Ярлыки недавно открытых файлов"));

            // --- Pro: кэш популярных программ (только если папки реально есть) ---
            string discord = Path.Combine(appdata, "discord");
            AddIfExists(cats, "discord_cache", "Кэш Discord", Path.Combine(discord, "Cache"), "Кэш картинок и медиа из Discord (сессия не пострадает)", true);
            AddIfExists(cats, "discord_code", "Кэш кода Discord", Path.Combine(discord, "Code Cache"), "Временный кэш интерфейса Discord", true);
            AddIfExists(cats, "discord_gpu", "GPU-кэш Discord", Path.Combine(discord, "GPUCache"), "Графический кэш Discord", true);

            string tg = Path.Combine(appdata, "Telegram Desktop");
            AddIfExists(cats, "telegram_logs", "Логи Telegram", Path.Combine(tg, "logs"), "Служебные логи Telegram Desktop (переписка не затронута)", true);
            AddIfExists(cats, "telegram_cache", "Кэш Telegram", Path.Combine(tg, "tdata", "cache"), "Временный кэш Telegram Desktop", true);
        }

        string? steam = FindSteamDir();
        if (steam != null)
        {
            AddIfExists(cats, "steam_http", "Кэш Steam", Path.Combine(steam, "appcache", "httpcache"), "Веб-кэш клиента Steam (игры не затронуты)", true);
            AddIfExists(cats, "steam_logs", "Логи Steam", Path.Combine(steam, "logs"), "Служебные логи клиента Steam", true);
        }

        cats.Add(new CleanCategory("dns_cache", "Кэш DNS", null, "dns", "Сброс закэшированных DNS-записей (чинит «сайт не открывается»)", true));
        cats.Add(new CleanCategory("recycle_bin", "Корзина", null, "recycle_bin", "Удалённые файлы, ожидающие окончательного удаления"));

        return cats.Where(c => c.Kind == "recycle_bin" || c.Kind == "dns" || (!string.IsNullOrEmpty(c.Path))).ToList();
    }

    public static string FlushDns()
    {
        try
        {
            var psi = new ProcessStartInfo("ipconfig", "/flushdns")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            string out_ = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return p.ExitCode == 0 ? "Кэш DNS очищен." : ("Не удалось: " + out_.Trim());
        }
        catch (Exception ex) { return "Не удалось сбросить DNS: " + ex.Message; }
    }

    public static long GetDirSize(string path, CancellationToken ct = default)
    {
        long total = 0;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) break;
            string cur;
            try { cur = stack.Pop(); } catch { break; }
            try
            {
                foreach (var f in Directory.EnumerateFiles(cur))
                {
                    if (ct.IsCancellationRequested) break;
                    try { total += new FileInfo(f).Length; } catch { }
                }
                foreach (var d in Directory.EnumerateDirectories(cur))
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var di = new DirectoryInfo(d);
                        if (di.LinkTarget != null) continue; // skip junctions/symlinks
                        stack.Push(d);
                    }
                    catch { }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return total;
    }

    public static (long Freed, int Errors) ClearFolderContents(string? path, CancellationToken ct = default, Action<string>? log = null)
    {
        long freed = 0;
        int errors = 0;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return (0, 0);
        if (SafetyService.IsProtected(path)) { log?.Invoke($"Пропущено (белый список): {path}"); return (0, 0); }
        string[] entries;
        try { entries = Directory.GetFileSystemEntries(path); }
        catch { return (0, 0); }

        foreach (var e in entries)
        {
            if (ct.IsCancellationRequested) break;
            if (SafetyService.IsProtected(e)) { log?.Invoke($"Пропущено (белый список): {e}"); continue; }
            try
            {
                if (File.Exists(e))
                {
                    long sz = 0;
                    try { sz = new FileInfo(e).Length; } catch { }
                    File.Delete(e);
                    freed += sz;
                }
                else if (Directory.Exists(e))
                {
                    long sz = GetDirSize(e, ct);
                    Directory.Delete(e, true);
                    freed += sz;
                }
            }
            catch (Exception ex)
            {
                errors++;
                log?.Invoke($"Не удалось удалить «{e}»: {ex.Message}");
            }
        }
        return (freed, errors);
    }

    public static List<(string Path, long Size, bool IsDir)> PreviewFolder(string? path, int maxItems = 50)
    {
        var res = new List<(string, long, bool)>();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return res;
        try
        {
            foreach (var e in Directory.GetFileSystemEntries(path))
            {
                if (res.Count >= maxItems) break;
                if (SafetyService.IsProtected(e)) continue;
                try
                {
                    if (File.Exists(e)) res.Add((e, new FileInfo(e).Length, false));
                    else res.Add((e, GetDirSize(e), true));
                }
                catch { }
            }
        }
        catch { }
        return res.OrderByDescending(x => x.Item2).ToList();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, int flags);

    public static void EmptyRecycleBin()
    {
        const int NOCONFIRM = 0x1, NOPROGRESS = 0x2, NOSOUND = 0x4;
        SHEmptyRecycleBin(IntPtr.Zero, null, NOCONFIRM | NOPROGRESS | NOSOUND);
    }

    public static void DeletePath(string path, bool useTrash = true)
    {
        if (SafetyService.IsProtected(path))
            throw new UnauthorizedAccessException($"Путь в белом списке, удаление запрещено: {path}");
        if (useTrash)
        {
            try
            {
                // Microsoft.VisualBasic доступен в .NET без NuGet
                if (File.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else if (Directory.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return;
            }
            catch { /* fallback ниже */ }
        }
        if (Directory.Exists(path) && !File.Exists(path))
            Directory.Delete(path, true);
        else
            File.Delete(path);
    }

    public static void OpenInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start("explorer.exe", $"\"{path}\"");
            else
                Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch { }
    }
}
