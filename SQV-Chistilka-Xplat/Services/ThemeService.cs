using System;

namespace SqvChistilka.Services;

public static class ThemeService
{
    public const string FallbackAccent = "#Eaff00";

    public static string GetAccentHex() => FallbackAccent; // акцент Windows-версии; на Linux/mac берём фирменный жёлтый
}
