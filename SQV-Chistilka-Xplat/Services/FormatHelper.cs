namespace SqvChistilka.Services;

public static class FormatHelper
{
    public static string HumanSize(double bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ", "ПБ" };
        int i = 0;
        while (bytes >= 1024 && i < units.Length - 1)
        {
            bytes /= 1024;
            i++;
        }
        return i == 0 ? $"{bytes:F0} {units[i]}" : $"{bytes:F1} {units[i]}";
    }
}
