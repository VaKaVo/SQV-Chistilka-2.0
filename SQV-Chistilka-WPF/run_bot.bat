@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo === SQV Chistilka Bot ===
echo Проверка зависимостей...
python -c "import aiogram, cryptography" 2>nul
if errorlevel 1 (
  echo Ставлю зависимости...
  pip install -r bot_requirements.txt
  if errorlevel 1 (
    echo ОШИБКА установки зависимостей
    pause
    exit /b 1
  )
)
echo Запуск бота... (для остановки закрой окно или нажми Ctrl+C)
python -u tg_bot.py
echo.
echo Бот остановлен с кодом %errorlevel%
pause
