using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SqvChistilka.Services;

// Тот же формат ключей, что в WPF-версии: SQV1.<payloadB64url>.<sigB64url> (RSA-2048/SHA256).
public static class LicenseService
{
    private const string ModulusB64 = "0DrMaUjs8vikarhmUAOOSgW08jmek2J+VVSgTvgb2/ZuXPL4KKjUgHGeGNCyOswjnEBid2CPNxK7wgE33/sF1aIQ39OCa9yy+sYu3MekfNh2vpC1MDO+FGHlVXzXYaLiwN2GZchmsgCaKOGWwbRf1pbJrD6Na/dwDfdgSURzQUX4rXVHPEZgLfMlEQp8VTIpWi9aBiGnz2M8nS3oSSlh/PL9m+GIAKXY/fjATVumDr1Ig3s8fytfJSUqzSBJrhLpjMvxWL2V7uWpc8KUjUQQHWkY0u54xsZFOSZ3+gJdf7HnPQjXO116APAxoL3U/Xs11uHkFv/ba07u2l9oO6pRVw==";

    private static string LicensePath => Path.Combine(PlatformHelper.AppDataDir, "license.json");

    public sealed record LicenseState(bool IsPro, DateTime? Expiry, string? Error);

    public static string GetHwid()
    {
        try
        {
            string seed;
            if (PlatformHelper.IsWindows)
            {
                string guid = "";
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    guid = key?.GetValue("MachineGuid")?.ToString() ?? "";
                seed = Environment.MachineName + "|" + guid;
            }
            else if (PlatformHelper.IsLinux)
            {
                seed = File.Exists("/etc/machine-id") ? File.ReadAllText("/etc/machine-id").Trim() : Environment.MachineName;
            }
            else
            {
                var (o, code) = PlatformHelper.Run("ioreg", "-rd1 -c IOPlatformExpertDevice");
                seed = code == 0 ? o : Environment.MachineName;
                int i = seed.IndexOf("IOPlatformUUID", StringComparison.Ordinal);
                if (i >= 0) seed = seed.Substring(i, Math.Min(200, seed.Length - i));
            }
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(seed)))[..16];
        }
        catch { return "UNKNOWN-HWID----".Substring(0, 16); }
    }

    public static LicenseState Validate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return new LicenseState(false, null, "Ключ не введён");
        key = key.Trim().Replace(" ", "").Replace("\n", "").Replace("\r", "");
        var parts = key.Split('.');
        if (parts.Length != 3 || parts[0] != "SQV1")
            return new LicenseState(false, null, "Неверный формат ключа (нужен вид SQV1.… . …)");
        try
        {
            byte[] payloadBytes = B64UrlDecode(parts[1]);
            byte[] sig = B64UrlDecode(parts[2]);
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Convert.FromBase64String(ModulusB64),
                Exponent = new byte[] { 1, 0, 1 },
            });
            if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[1]), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return new LicenseState(false, null, "Подпись неверна — ключ поддельный или повреждён");
            using var doc = JsonDocument.Parse(payloadBytes);
            var root = doc.RootElement;
            if (root.GetProperty("p").GetString() != "PRO")
                return new LicenseState(false, null, "Неизвестный тариф в ключе");
            long exp = root.GetProperty("exp").GetInt64();
            var expiry = DateTimeOffset.FromUnixTimeSeconds(exp).LocalDateTime;
            if (DateTime.Now > expiry)
                return new LicenseState(false, expiry, $"Ключ истёк {expiry:dd.MM.yyyy}");
            string hwid = root.TryGetProperty("hwid", out var h) ? h.GetString() ?? "*" : "*";
            if (hwid != "*" && !hwid.Equals(GetHwid(), StringComparison.OrdinalIgnoreCase))
                return new LicenseState(false, expiry, "Ключ привязан к другому ПК");
            return new LicenseState(true, expiry, null);
        }
        catch (Exception ex) { return new LicenseState(false, null, $"Ошибка проверки: {ex.Message}"); }
    }

    public static void Save(string key)
    {
        Directory.CreateDirectory(PlatformHelper.AppDataDir);
        File.WriteAllText(LicensePath, JsonSerializer.Serialize(new { key }));
    }

    public static string? Load()
    {
        try
        {
            if (!File.Exists(LicensePath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(LicensePath));
            return doc.RootElement.GetProperty("key").GetString();
        }
        catch { return null; }
    }

    public static void Remove()
    {
        try { if (File.Exists(LicensePath)) File.Delete(LicensePath); } catch { }
    }

    public static LicenseState Current() => Validate(Load());

    private static byte[] B64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
