using System;
using System.Collections.Generic;
using System.IO;

namespace SqvChistilka.Services;

public sealed record DiskInfo(string Device, long Total, long Used, long Free, double Percent);

public static class DiskService
{
    public static List<DiskInfo> ListDisks()
    {
        var list = new List<DiskInfo>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (!d.IsReady) continue;
                if (d.DriveType == DriveType.CDRom) continue;
                if (PlatformHelper.IsWindows && (d.DriveType == DriveType.Removable && d.TotalSize == 0)) continue;
                // На Linux прячем служебные tmpfs/squashfs
                if (PlatformHelper.IsLinux)
                {
                    string m = d.Name;
                    if (m.StartsWith("/snap") || m.StartsWith("/boot/efi") && d.TotalSize < 600_000_000) continue;
                    if (d.DriveType == DriveType.Ram) continue;
                }
                try
                {
                    long total = d.TotalSize;
                    long free = d.TotalFreeSpace;
                    long used = total - free;
                    double pct = total > 0 ? used * 100.0 / total : 0;
                    list.Add(new DiskInfo(d.Name, total, used, free, pct));
                }
                catch { }
            }
        }
        catch { }

        if (list.Count == 0)
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "/";
                var di = new DriveInfo(root);
                if (di.IsReady)
                    list.Add(new DiskInfo(root, di.TotalSize, di.TotalSize - di.TotalFreeSpace, di.TotalFreeSpace,
                        di.TotalSize > 0 ? (di.TotalSize - di.TotalFreeSpace) * 100.0 / di.TotalSize : 0));
            }
            catch { }
        }
        return list;
    }

    public static List<(string Icon, string Text)> GenerateTips(List<DiskInfo> disks)
    {
        var tips = new List<(string, string)>();
        foreach (var d in disks)
        {
            if (d.Percent >= 90)
                tips.Add(("⚠️", $"Диск {d.Device} заполнен на {d.Percent:F0}% — освободите место как можно скорее."));
            else if (d.Percent >= 75)
                tips.Add(("🟡", $"Диск {d.Device} заполнен на {d.Percent:F0}% — стоит почистить в ближайшее время."));
            else
                tips.Add(("✅", $"Диск {d.Device} заполнен на {d.Percent:F0}% — всё в порядке."));
        }
        tips.Add(("🧹", "Регулярно очищайте временные файлы и кэш браузера — это безопасно и не удаляет пароли/историю."));
        tips.Add(("🚀", "Проверьте вкладку «Автозагрузка»: отключение ненужных программ ускорит запуск системы."));
        tips.Add(("📊", "Загляните в «Анализ диска», чтобы найти самые большие файлы и папки."));
        tips.Add(("🗑", "Проверьте папки «Загрузки» и «Рабочий стол» — там копятся большие ненужные файлы."));
        tips.Add(("📦", "Удаление неиспользуемых программ освобождает больше места, чем чистка кэша."));
        tips.Add(("♻️", "Не забывайте очищать корзину — файлы занимают место, пока корзина не пуста."));
        return tips;
    }
}
