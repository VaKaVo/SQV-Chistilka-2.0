namespace SqvChistilka.Services;

// Единое место версии. Поднимай при каждом релизе (и в installer.iss через /DAppVersion).
public static class AppInfo
{
    public const string Version = "2.1.0";

    // !!! ЗАМЕНИ на свой GitHub-репозиторий, иначе проверка обновлений молча отключена.
    public const string RepoOwner = "VaKaVo";
    public const string RepoName = "SQV-Chistilka-2.0";

    public static bool UpdatesConfigured => RepoOwner != "ЗАМЕНИ";
}
