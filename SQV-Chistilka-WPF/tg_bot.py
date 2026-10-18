#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Telegram-бот продажи Pro-ключей SQV Chistilka за Telegram Stars.
Логика «бесконечный, но одноразовый»:
  - ключ бессрочный (exp 2100 год);
  - одноразовость = один ключ на одного юзера + привязка к HWID его ПК.

Флоу юзера:
  кнопка «🎫 Купить Pro» → бот просит ID ПК (программа → «🔑 Лицензия» → «Копировать»)
  → счёт на N Stars → оплата → бот генерирует ключ и присылает его.
Админы (ADMIN_IDS) получают ключ бесплатно, без оплаты.

Настройка (опенсорс-версия, без секретов в коде):
  1. Скопируй `.env.example` в `.env` и заполни.
  2. @BotFather → токен → SQV_BOT_TOKEN. Для Stars ничего дополнительно включать не надо
     (цифровые товары через XTR работают из коробки).
  3. Свой ID через @userinfobot → SQV_ADMIN_IDS (через запятую).
  4. Цену в звёздах → PRO_PRICE_STARS или env SQV_PRO_PRICE_STARS.
  5. Сгенерируй RSA-2048: openssl genrsa -out private.pem 2048
     Публичный Modulus вставь в Services/LicenseService.cs (ModulusB64).
     private.pem НЕ коммить! pip install aiogram cryptography. python tg_bot.py.
"""
import base64
import json
import logging
import os
import secrets
import sqlite3
import time

from aiogram import Bot, Dispatcher, types, F
from aiogram.filters import CommandStart
from aiogram.fsm.context import FSMContext
from aiogram.fsm.state import State, StatesGroup
from aiogram.types import LabeledPrice, ReplyKeyboardMarkup, KeyboardButton
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding

# ---------------- НАСТРОЙКИ (через env, секретов в коде нет) ----------------
def _parse_admin_ids(raw: str) -> set[int]:
    out: set[int] = set()
    for part in (raw or "").replace(";", ",").split(","):
        part = part.strip()
        if part.isdigit():
            out.add(int(part))
    return out


BOT_TOKEN = os.getenv("SQV_BOT_TOKEN", "")
ADMIN_IDS: set[int] = _parse_admin_ids(os.getenv("SQV_ADMIN_IDS", ""))
PRO_PRICE_STARS = int(os.getenv("SQV_PRO_PRICE_STARS", "15"))
PRIVATE_KEY_FILE = os.getenv("SQV_PRIVATE_KEY", "private.pem")
DB_FILE = os.getenv("SQV_DB_FILE", "bot_keys.db")
# -------------------------------------------

FOREVER_EXP = 4102444800  # 01.01.2100

logging.basicConfig(level=logging.INFO)

kb_main = ReplyKeyboardMarkup(
    keyboard=[[KeyboardButton(text="🎫 Купить Pro")]],
    resize_keyboard=True,
)


class GenState(StatesGroup):
    waiting_hwid = State()


class AdminState(StatesGroup):
    waiting_winner_hwid = State()


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode().rstrip("=")


def load_private():
    with open(PRIVATE_KEY_FILE, "rb") as f:
        return serialization.load_pem_private_key(f.read(), password=None)


def make_key(priv, hwid: str) -> tuple[str, str]:
    payload = {
        "p": "PRO",
        "exp": FOREVER_EXP,
        "hwid": hwid.upper(),
        "serial": secrets.token_hex(3).upper(),
    }
    payload_b64 = b64url(json.dumps(payload, separators=(",", ":")).encode())
    sig = priv.sign(payload_b64.encode("ascii"), padding.PKCS1v15(), hashes.SHA256())
    return f"SQV1.{payload_b64}.{b64url(sig)}", payload["serial"]


def db() -> sqlite3.Connection:
    con = sqlite3.connect(DB_FILE)
    con.execute(
        "CREATE TABLE IF NOT EXISTS keys("
        "user_id INTEGER PRIMARY KEY, key TEXT, serial TEXT, hwid TEXT, created INTEGER, "
        "stars_tx TEXT DEFAULT '', amount INTEGER DEFAULT 0)"
    )
    cols = {r[1] for r in con.execute("PRAGMA table_info(keys)").fetchall()}
    if "stars_tx" not in cols:
        con.execute("ALTER TABLE keys ADD COLUMN stars_tx TEXT DEFAULT ''")
    if "amount" not in cols:
        con.execute("ALTER TABLE keys ADD COLUMN amount INTEGER DEFAULT 0")
    con.execute(
        "CREATE TABLE IF NOT EXISTS giveaway("
        "id INTEGER PRIMARY KEY AUTOINCREMENT, key TEXT, serial TEXT, hwid TEXT, "
        "created INTEGER, note TEXT DEFAULT '')"
    )
    return con


def already_issued(user_id: int) -> str | None:
    con = db()
    row = con.execute("SELECT key FROM keys WHERE user_id=?", (user_id,)).fetchone()
    con.close()
    return row[0] if row else None


def save_key(user_id: int, key: str, serial: str, hwid: str, stars_tx: str = "", amount: int = 0) -> None:
    con = db()
    con.execute(
        "INSERT OR REPLACE INTO keys(user_id, key, serial, hwid, created, stars_tx, amount)"
        " VALUES(?,?,?,?,?,?,?)",
        (user_id, key, serial, hwid, int(time.time()), stars_tx, amount),
    )
    con.commit()
    con.close()


def valid_hwid(s: str) -> bool:
    s = (s or "").strip().upper()
    return len(s) == 16 and all(c in "0123456789ABCDEF" for c in s)


async def main() -> None:
    bot = Bot(token=BOT_TOKEN)
    dp = Dispatcher()

    @dp.message(CommandStart())
    async def start(m: types.Message):
        await m.answer(
            "👋 SQV Chistilka Pro — бессрочная лицензия.\n"
            f"💰 Цена: {PRO_PRICE_STARS} ⭐ (Telegram Stars).\n"
            "Ключ выдаётся один раз и привязывается к твоему ПК.\n\n"
            "Нажми «🎫 Купить Pro».",
            reply_markup=kb_main,
        )

    @dp.message(F.text.in_({"/id", "/myid"}))
    async def show_id(m: types.Message):
        admin = " ✅ ты админ (бесплатно)" if m.from_user.id in ADMIN_IDS else ""
        await m.answer(f"Твой Telegram ID: `{m.from_user.id}`{admin}", parse_mode="Markdown")

    kb_admin = ReplyKeyboardMarkup(
        keyboard=[
            [KeyboardButton(text="🎁 Халявный ключ")],
            [KeyboardButton(text="📊 Статистика")],
            [KeyboardButton(text="◀️ Назад")],
        ],
        resize_keyboard=True,
    )

    @dp.message(F.text.in_({"/admin", "⚙️ Админка"}))
    async def admin_open(m: types.Message, state: FSMContext):
        if m.from_user.id not in ADMIN_IDS:
            await m.answer("⛔ Только для администратора.")
            return
        await state.clear()
        await m.answer("⚙️ Админ-панель:", reply_markup=kb_admin)

    @dp.message(F.text == "◀️ Назад")
    async def admin_back(m: types.Message, state: FSMContext):
        await state.clear()
        await m.answer("Главное меню:", reply_markup=kb_main)

    @dp.message(F.text == "📊 Статистика")
    async def admin_stats(m: types.Message):
        if m.from_user.id not in ADMIN_IDS:
            return
        con = db()
        n_paid, stars = con.execute(
            "SELECT COUNT(*), COALESCE(SUM(amount),0) FROM keys WHERE amount > 0").fetchone()
        n_free = con.execute("SELECT COUNT(*) FROM keys WHERE amount = 0").fetchone()[0]
        n_give = con.execute("SELECT COUNT(*) FROM giveaway").fetchone()[0]
        con.close()
        await m.answer(
            "📊 Статистика:\n"
            f"• Продано Pro: {n_paid} (заработано {stars} ⭐)\n"
            f"• Выдано бесплатно: {n_free}\n"
            f"• Халявных для розыгрышей: {n_give}",
        )

    @dp.message(F.text == "🎁 Халявный ключ")
    async def give_start(m: types.Message, state: FSMContext):
        if m.from_user.id not in ADMIN_IDS:
            return
        await state.set_state(AdminState.waiting_winner_hwid)
        await m.answer(
            "🎁 Пришли **ID ПК победителя** (пусть скопирует из программы: «🔑 Лицензия» → «Копировать»).\n"
            "Ключ будет бессрочным и сработает только на его компьютере.",
            parse_mode="Markdown",
        )

    @dp.message(AdminState.waiting_winner_hwid)
    async def give_make(m: types.Message, state: FSMContext):
        if m.from_user.id not in ADMIN_IDS:
            await state.clear()
            return
        hwid = (m.text or "").strip().upper()
        if not valid_hwid(hwid):
            await m.answer("❌ Не похоже на ID ПК (нужно 16 символов 0-9/A-F). Пришли ещё раз или нажми «◀️ Назад».")
            return
        try:
            key, serial = make_key(load_private(), hwid)
        except FileNotFoundError:
            await state.clear()
            await m.answer("🔥 Нет private.pem рядом с ботом.")
            return
        con = db()
        con.execute(
            "INSERT INTO giveaway(key, serial, hwid, created) VALUES(?,?,?,?)",
            (key, serial, hwid, int(time.time())),
        )
        con.commit()
        con.close()
        await state.clear()
        await m.answer(
            "✅ Халявный ключ для победителя (перешли ему):\n"
            f"`{key}`\n\nПусть вставит в программе: «🔑 Лицензия» → «Активировать Pro».",
            parse_mode="Markdown",
            reply_markup=kb_admin,
        )

    @dp.message(F.text == "🎫 Купить Pro")
    async def buy_press(m: types.Message, state: FSMContext):
        existing = already_issued(m.from_user.id)
        if existing:
            await m.answer(
                "⚠️ Тебе уже выдавался ключ (выдача одноразовая):\n"
                f"`{existing}`\n\nНовый купить нельзя. Потерял — пиши разработчику.",
                parse_mode="Markdown",
            )
            return
        await state.set_state(GenState.waiting_hwid)
        await m.answer(
            "💻 Пришли **ID твоего ПК** из программы:\n"
            "Открой SQV Chistilka → вкладка «🔑 Лицензия» → кнопка «Копировать» → вставь сюда.\n\n"
            "Ключ будет работать только на этом компьютере.",
            parse_mode="Markdown",
        )

    @dp.message(GenState.waiting_hwid)
    async def got_hwid(m: types.Message, state: FSMContext):
        hwid = (m.text or "").strip().upper()
        if not valid_hwid(hwid):
            await m.answer("❌ Это не похоже на ID ПК (нужно 16 символов 0-9/A-F). Вставь ID из вкладки «Лицензия».")
            return
        if already_issued(m.from_user.id):
            await state.clear()
            await m.answer("⚠️ Ключ тебе уже выдавался. Дубли запрещены.")
            return
        await state.clear()
        # Админы — бесплатно, сразу ключ
        if m.from_user.id in ADMIN_IDS:
            try:
                key, serial = make_key(load_private(), hwid)
            except FileNotFoundError:
                await m.answer("🔥 Ошибка сервера: нет private.pem. Напиши разработчику.")
                return
            save_key(m.from_user.id, key, serial, hwid)
            await m.answer(
                "✅ Админ-выдача, бесплатно. Твой бессрочный Pro-ключ:\n"
                f"`{key}`\n\nВставь в программе: «🔑 Лицензия» → «Активировать Pro».",
                parse_mode="Markdown",
            )
            return
        # Остальные — счёт в звёздах, HWID едет в payload
        await m.answer_invoice(
            title="SQV Chistilka Pro — навсегда",
            description="Бессрочная Pro-лицензия, привязывается к твоему ПК. Выдача одноразовая.",
            payload=f"pro:{hwid}",
            currency="XTR",
            prices=[LabeledPrice(label="Pro навсегда", amount=PRO_PRICE_STARS)],
        )

    @dp.pre_checkout_query()
    async def pre_checkout(q: types.PreCheckoutQuery):
        # payload вида pro:<HWID>
        try:
            _, hwid = q.invoice_payload.split(":")
            ok = valid_hwid(hwid)
        except ValueError:
            ok = False
        if not ok:
            await q.answer(ok=False, error_message="Битый счёт, запроси заново через «🎫 Купить Pro».")
            return
        if already_issued(q.from_user.id):
            await q.answer(ok=False, error_message="Тебе уже выдавался ключ, повторная покупка запрещена.")
            return
        await q.answer(ok=True)

    @dp.message(F.successful_payment)
    async def paid(m: types.Message):
        pay = m.successful_payment
        try:
            _, hwid = pay.invoice_payload.split(":")
        except ValueError:
            await m.answer("🔥 Ошибка: не смог привязать ключ. Напиши разработчику с этим сообщением.")
            return
        existing = already_issued(m.from_user.id)
        if existing:
            await m.answer(
                "Оплата прошла, но ключ тебе уже выдавался — вот он:\n"
                f"`{existing}`",
                parse_mode="Markdown",
            )
            return
        try:
            key, serial = make_key(load_private(), hwid)
        except FileNotFoundError:
            await m.answer("🔥 Оплата прошла, но нет private.pem на сервере! СРОЧНО напиши разработчику, ключ выдадут вручную.")
            return
        save_key(m.from_user.id, key, serial, hwid,
                 stars_tx=pay.telegram_payment_charge_id, amount=pay.total_amount)
        await m.answer(
            "✅ Оплата прошла! Твой бессрочный Pro-ключ:\n"
            f"`{key}`\n\nВставь его в программе: «🔑 Лицензия» → «Активировать Pro».",
            parse_mode="Markdown",
        )

    await dp.start_polling(bot)


if __name__ == "__main__":
    import asyncio
    import sys

    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    # Опционально: подхватить .env рядом с ботом
    try:
        _env_path = os.path.join(os.path.dirname(__file__), ".env")
        if os.path.exists(_env_path):
            with open(_env_path, encoding="utf-8") as _f:
                for _line in _f:
                    _line = _line.strip()
                    if not _line or _line.startswith("#") or "=" not in _line:
                        continue
                    _k, _v = _line.split("=", 1)
                    os.environ.setdefault(_k.strip(), _v.strip().strip('"').strip("'"))
            # перечитать после .env
            BOT_TOKEN = os.getenv("SQV_BOT_TOKEN", "")
            ADMIN_IDS = _parse_admin_ids(os.getenv("SQV_ADMIN_IDS", ""))
            PRO_PRICE_STARS = int(os.getenv("SQV_PRO_PRICE_STARS", "15") or "15")
            PRIVATE_KEY_FILE = os.getenv("SQV_PRIVATE_KEY", "private.pem")
            DB_FILE = os.getenv("SQV_DB_FILE", "bot_keys.db")
    except Exception:
        pass

    if not BOT_TOKEN or ":" not in BOT_TOKEN:
        print("❌ SQV_BOT_TOKEN не заполнен!")
        print("   1. Скопируй .env.example в .env.")
        print("   2. Открой @BotFather → /newbot → вставь токен в SQV_BOT_TOKEN.")
        sys.exit(1)
    if not os.path.exists(PRIVATE_KEY_FILE):
        print(f"❌ Нет файла {PRIVATE_KEY_FILE} рядом с ботом — без него ключи не подписать.")
        sys.exit(1)

    asyncio.run(main())
