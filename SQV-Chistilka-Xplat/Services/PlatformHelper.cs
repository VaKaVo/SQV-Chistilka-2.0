using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SqvChistilka.Services;

public static class PlatformHelper
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsLinux => OperatingSystem.IsLinux();
    public static bool IsMac => OperatingSystem.IsMacOS();

    public static string OsName => IsWindows ? "Windows" : IsLinux ? "Linux" : IsMac ? "macOS" : "Unknown";

    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SQV_Chistilka");

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static (string Out, int Code) Run(string exe, string args, int timeoutMs = 30000)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(timeoutMs);
            return (o, p.ExitCode);
        }
        catch (Exception ex) { return (ex.Message, -1); }
    }

    public static void OpenInFileManager(string path)
    {
        try
        {
            if (IsWindows)
            {
                if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
                else Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (IsMac)
            {
                if (Directory.Exists(path)) Process.Start("open", $"\"{path}\"");
                else Process.Start("open", $"-R \"{path}\"");
            }
            else
            {
                string target = Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? Home);
                Process.Start("xdg-open", $"\"{target}\"");
            }
        }
        catch { }
    }
}
