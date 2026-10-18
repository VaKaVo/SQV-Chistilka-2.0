using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace SqvChistilka.Services;

public sealed record UpdateInfo(string Version, string PageUrl, string? SetupUrl);

public static class UpdateService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("SQV-Chistilka-Updater");
    }

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (!AppInfo.UpdatesConfigured) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/latest");
            using var res = await Http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            string tag = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');
            if (!System.Version.TryParse(tag, out var latest))
            {
                // Запасной вариант: версия в начале названия релиза ("2.1.0 - Restart")
                string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var m = System.Text.RegularExpressions.Regex.Match(name, @"^v?(\d+(?:\.\d+){1,3})");
                if (!m.Success || !System.Version.TryParse(m.Groups[1].Value, out latest)) return null;
            }
            if (!System.Version.TryParse(AppInfo.Version, out var current)) return null;
            if (latest <= current) return null;
            string? setup = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        setup = a.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }
            string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            return new UpdateInfo(tag, page, setup);
        }
        catch { return null; }
    }

    public static async Task<string> DownloadSetupAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(info.SetupUrl))
            throw new InvalidOperationException("В релизе нет Setup.exe");
        string dest = Path.Combine(Path.GetTempPath(), "SQV-Chistilka-Setup.exe");
        using var res = await Http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        long? total = res.Content.Headers.ContentLength;
        await using var src = await res.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buf = new byte[81920];
        long done = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            done += n;
            if (total > 0) progress?.Report(done / (double)total.Value);
        }
        return dest;
    }

    public static void RunInstallerAndExit(string setupPath)
    {
        Process.Start(new ProcessStartInfo(setupPath, "/SP- /SILENT")
        {
            UseShellExecute = true,
            Verb = "runas", // Inno пишет в Program Files — нужны права
        });
        System.Windows.Application.Current.Shutdown();
    }
}
