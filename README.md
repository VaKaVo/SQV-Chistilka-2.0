# SQV-Chistilka 2

Чистилка для ПК: очистка мусора, анализ диска, проводник, автозагрузка, Free/Pro-лицензия.

- `SQV-Chistilka-WPF/` — Windows-версия (WPF, `net8.0-windows`)
- `SQV-Chistilka-Xplat/` — кроссплатформенная версия (Avalonia, `net8.0`: Windows/Linux/macOS)

## Сборка

```bash
dotnet build SQV-Chistilka-WPF/SqvChistilka.csproj -c Release
dotnet build SQV-Chistilka-Xplat/SqvChistilka.csproj -c Release
```

Инсталлер Windows (Inno Setup):

```bash
dotnet publish SQV-Chistilka-WPF/SqvChistilka.csproj -c Release -o SQV-Chistilka-WPF/publish-single
ISCC.exe /DAppVersion=2.1.0 SQV-Chistilka-WPF/installer.iss
```