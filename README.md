# SQV-Chistilka 2

Чистилка для ПК: очистка мусора, анализ диска, проводник, автозагрузка, Free/Pro-лицензия.

- `SQV-Chistilka-WPF/` — Windows-версия (WPF, `net8.0-windows`)
- `SQV-Chistilka-Xplat/` — кроссплатформенная версия (Avalonia, `net8.0`: Windows/Linux/macOS)
- Telegram-бот продажи Pro-ключей за Stars: `SQV-Chistilka-WPF/tg_bot.py`

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

## Бот (опционально)

```bash
cd SQV-Chistilka-WPF
copy .env.example .env
# заполни SQV_BOT_TOKEN и SQV_ADMIN_IDS
openssl genrsa -out private.pem 2048
pip install -r bot_requirements.txt
python tg_bot.py
```

Генератор ключей вручную:

```bash
python bot_gen_key.py --forever --hwid <HWID16>
```

## Безопасность

В репозитории нет секретов: токен бота задаётся через env/`.env`,
`private.pem` и `bot_keys.db` в git не коммитятся (см. `.gitignore`).
Публичный ключ для проверки лицензий вшит в `Services/LicenseService.cs`.

Лицензия: см. `SQV-Chistilka-WPF/LICENSE_RU.txt`.
