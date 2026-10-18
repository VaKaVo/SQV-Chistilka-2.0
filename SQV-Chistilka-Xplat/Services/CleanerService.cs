using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace SqvChistilka.Services;

public sealed record CleanCategory(string Id, string Name, string? Path, string Kind, string Desc, bool IsPro = false);

public static class CleanerService
{
    private static void AddIfExists(List<CleanCategory> cats, string id, string name, string path, string desc, bool isPro = false)
    {
        try { if (Directory.Exists(path)) cats.Add(new CleanCategory(id, name, path, "dir", desc, isPro)); } catch { }
    }

    public static List<CleanCategory> GetTempCategories()
    {
        if (PlatformHelper.IsWindows) return WindowsCategories();
        if (PlatformHelper.IsMac) return MacCategories();
        return LinuxCategories();
    }

    // ---------------- Windows (как в WPF-версии) ----------------
    private static List<CleanCategory> WindowsCategories()
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
            string discord = Path.Combine(appdata, "discord");
            AddIfExists(cats, "discord_cache", "Кэш Discord", Path.Combine(discord, "Cache"), "Кэш картинок и медиа из Discord", true);
            AddIfExists(cats, "discord_code", "Кэш кода Discord", Path.Combine(discord, "Code Cache"), "Временный кэш интерфейса Discord", true);
            AddIfExists(cats, "discord_gpu", "GPU-кэш Discord", Path.Combine(discord, "GPUCache"), "Графический кэш Discord", true);
            string tg = Path.Combine(appdata, "Telegram Desktop");
            AddIfExists(cats, "telegram_logs", "Логи Telegram", Path.Combine(tg, "logs"), "Служебные логи Telegram Desktop", true);
            AddIfExists(cats, "telegram_cache", "Кэш Telegram", Path.Combine(tg, "tdata", "cache"), "Временный кэш Telegram Desktop", true);
        }
        string? steam = FindSteamDirWindows();
        if (steam != null)
        {
            AddIfExists(cats, "steam_http", "Кэш Steam", Path.Combine(steam, "appcache", "httpcache"), "Веб-кэш клиента Steam (игры не затронуты)", true);
            AddIfExists(cats, "steam_logs", "Логи Steam", Path.Combine(steam, "logs"), "Служебные логи клиента Steam", true);
        }
        cats.Add(new CleanCategory("dns_cache", "Кэш DNS", null, "dns", "Сброс закэшированных DNS-записей", true));
        cats.Add(new CleanCategory("recycle_bin", "Корзина", null, "recycle_bin", "Удалённые файлы, ожидающие окончательного удаления"));
        return cats.Where(c => c.Kind is "recycle_bin" or "dns" || !string.IsNullOrEmpty(c.Path)).ToList();
    }

    private static string? FindSteamDirWindows()
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
        string guess = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(guess) ? guess : null;
    }

    // ---------------- Linux ----------------
    private static List<CleanCategory> LinuxCategories()
    {
        var cats = new List<CleanCategory>();
        string home = PlatformHelper.Home;
        cats.Add(new CleanCategory("tmp", "Временные файлы (/tmp)", "/tmp", "dir", "Общесистемная временная папка"));
        AddIfExists(cats, "user_cache", "Кэш пользователя (~/.cache)", Path.Combine(home, ".cache"), "Кэш приложений текущего пользователя");
        AddIfExists(cats, "chrome_cache", "Кэш Chrome/Chromium", Path.Combine(home, ".cache", "google-chrome", "Default", "Cache"), "Кэш сайтов из Chrome");
        AddIfExists(cats, "firefox_cache", "Кэш Firefox", Path.Combine(home, ".cache", "mozilla", "firefox"), "Кэш сайтов из Firefox");
        string discord = Path.Combine(home, ".config", "discord");
        AddIfExists(cats, "discord_cache", "Кэш Discord", Path.Combine(discord, "Cache"), "Кэш картинок и медиа из Discord", true);
        AddIfExists(cats, "discord_gpu", "GPU-кэш Discord", Path.Combine(discord, "GPUCache"), "Графический кэш Discord", true);
        foreach (var s in new[] { Path.Combine(home, ".steam", "steam"), Path.Combine(home, ".local", "share", "Steam") })
        {
            if (Directory.Exists(s))
            {
                AddIfExists(cats, "steam_http", "Кэш Steam", Path.Combine(s, "appcache", "httpcache"), "Веб-кэш клиента Steam (игры не затронуты)", true);
                AddIfExists(cats, "steam_logs", "Логи Steam", Path.Combine(s, "logs"), "Служебные логи клиента Steam", true);
                break;
            }
        }
        string tg = Path.Combine(home, ".local", "share", "TelegramDesktop");
        AddIfExists(cats, "telegram_logs", "Логи Telegram", Path.Combine(tg, "logs"), "Служебные логи Telegram Desktop", true);
        string trash = TrashDirLinux();
        if (Directory.Exists(trash))
            cats.Add(new CleanCategory("trash", "Корзина", trash, "dir", "Удалённые файлы в корзине"));
        else
            cats.Add(new CleanCategory("recycle_bin", "Корзина", null, "recycle_bin", "Удалённые файлы в корзине"));
        cats.Add(new CleanCategory("dns_cache", "Кэш DNS", null, "dns", "Сброс кэша DNS (resolvectl/systemd)", true));
        return cats;
    }

    // ---------------- macOS ----------------
    private static List<CleanCategory> MacCategories()
    {
        var cats = new List<CleanCategory>();
        string home = PlatformHelper.Home;
        AddIfExists(cats, "user_cache", "Кэш пользователя (~/Library/Caches)", Path.Combine(home, "Library", "Caches"), "Кэш приложений текущего пользователя");
        cats.Add(new CleanCategory("tmp", "Временные файлы (/tmp)", "/tmp", "dir", "Общесистемная временная папка"));
        string supp = Path.Combine(home, "Library", "Application Support");
        AddIfExists(cats, "discord_cache", "Кэш Discord", Path.Combine(supp, "discord", "Cache"), "Кэш картинок и медиа из Discord", true);
        AddIfExists(cats, "steam_http", "Кэш Steam", Path.Combine(supp, "Steam", "appcache", "httpcache"), "Веб-кэш клиента Steam (игры не затронуты)", true);
        AddIfExists(cats, "steam_logs", "Логи Steam", Path.Combine(supp, "Steam", "logs"), "Служебные логи клиента Steam", true);
        AddIfExists(cats, "telegram_logs", "Логи Telegram", Path.Combine(supp, "Telegram Desktop", "logs"), "Служебные логи Telegram Desktop", true);
        cats.Add(new CleanCategory("recycle_bin", "Корзина", null, "recycle_bin", "Удалённые файлы в корзине (~/.Trash)"));
        cats.Add(new CleanCategory("dns_cache", "Кэш DNS", null, "dns", "Сброс кэша DNS (dscacheutil + mDNSResponder)", true));
        return cats;
    }

    // ---------------- общие операции ----------------
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
                        if (di.LinkTarget != null) continue;
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
            catch (Exception ex) { errors++; log?.Invoke($"Не удалось удалить «{e}»: {ex.Message}"); }
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

    public static string FlushDns()
    {
        if (PlatformHelper.IsWindows)
        {
            var (o, code) = PlatformHelper.Run("ipconfig", "/flushdns");
            return code == 0 ? "Кэш DNS очищен." : ("Не удалось: " + o.Trim());
        }
        if (PlatformHelper.IsMac)
        {
            PlatformHelper.Run("dscacheutil", "-flushcache");
            var (o, code) = PlatformHelper.Run("killall", "-HUP mDNSResponder");
            return code == 0 ? "Кэш DNS очищен." : ("mDNSResponder: " + o.Trim());
        }
        var (o2, c2) = PlatformHelper.Run("resolvectl", "flush-caches");
        if (c2 == 0) return "Кэш DNS очищен (resolvectl).";
        var (o3, c3) = PlatformHelper.Run("systemd-resolve", "--flush-caches");
        return c3 == 0 ? "Кэш DNS очищен (systemd-resolve)." : ("Нет resolvectl/systemd-resolve: " + o2.Trim());
    }

    // ---------------- корзина ----------------
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, int flags);

    public static void EmptyRecycleBin()
    {
        if (PlatformHelper.IsWindows)
        {
            SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
            return;
        }
        if (PlatformHelper.IsMac)
        {
            ClearFolderContents(Path.Combine(PlatformHelper.Home, ".Trash"));
            return;
        }
        string files = Path.Combine(TrashDirLinux(), "files");
        string info = Path.Combine(TrashDirLinux(), "info");
        ClearFolderContents(files);
        ClearFolderContents(info);
    }

    private static string TrashDirLinux()
    {
        string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? "";
        return string.IsNullOrEmpty(xdg)
            ? Path.Combine(PlatformHelper.Home, ".local", "share", "Trash")
            : Path.Combine(xdg, "Trash");
    }

    public static void DeletePath(string path, bool useTrash = true)
    {
        if (SafetyService.IsProtected(path))
            throw new UnauthorizedAccessException($"Путь в белом списке, удаление запрещено: {path}");
        if (useTrash)
        {
            try { MoveToTrash(path); return; }
            catch { /* fallback ниже */ }
        }
        if (Directory.Exists(path) && !File.Exists(path)) Directory.Delete(path, true);
        else File.Delete(path);
    }

    private static void MoveToTrash(string path)
    {
        if (PlatformHelper.IsWindows)
        {
            if (File.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return;
        }
        if (PlatformHelper.IsMac)
        {
            string trash = Path.Combine(PlatformHelper.Home, ".Trash");
            Directory.CreateDirectory(trash);
            MoveIntoDir(path, trash);
            return;
        }
        // Linux: freedesktop Trash spec (files + info)
        string baseDir = TrashDirLinux();
        string files = Path.Combine(baseDir, "files");
        string info = Path.Combine(baseDir, "info");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(info);
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        string dest = Path.Combine(files, name);
        if (File.Exists(dest) || Directory.Exists(dest))
            dest = Path.Combine(files, $"{name}.{DateTime.Now:yyyyMMddHHmmss}");
        if (File.Exists(path)) File.Move(path, dest);
        else Directory.Move(path, dest);
        try
        {
            File.WriteAllText(Path.Combine(info, Path.GetFileName(dest) + ".trashinfo"),
                $"[Trash Info]\nPath={path}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
        }
        catch { }
    }

    private static void MoveIntoDir(string path, string dir)
    {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        string dest = Path.Combine(dir, name);
        if (File.Exists(dest) || Directory.Exists(dest))
            dest = Path.Combine(dir, $"{name}.{DateTime.Now:yyyyMMddHHmmss}");
        if (File.Exists(path)) File.Move(path, dest);
        else Directory.Move(path, dest);
    }

    public static void OpenInExplorer(string path) => PlatformHelper.OpenInFileManager(path);
}
