#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Генератор Pro-ключей SQV Chistilka для Telegram-бота.
Использование:
    pip install cryptography
    python bot_gen_key.py --days 365            # ключ на год, без привязки к ПК
    python bot_gen_key.py --days 30 --hwid ABC123...  # ключ на 30 дней с привязкой к ПК
    python bot_gen_key.py --forever             # бессрочный
Рядом должен лежать private.pem (НЕ публикуй его, НЕ клади в репозиторий!).
"""
import argparse
import base64
import json
import secrets
import time

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode().rstrip("=")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--days", type=int, default=365)
    ap.add_argument("--forever", action="store_true")
    ap.add_argument("--hwid", default="*")
    ap.add_argument("--key", default="private.pem")
    args = ap.parse_args()

    with open(args.key, "rb") as f:
        priv = serialization.load_pem_private_key(f.read(), password=None)

    exp = 4102444800 if args.forever else int(time.time()) + args.days * 86400
    payload = {
        "p": "PRO",
        "exp": exp,
        "hwid": args.hwid,
        "serial": secrets.token_hex(3).upper(),
    }
    payload_b64 = b64url(json.dumps(payload, separators=(",", ":")).encode())
    sig = priv.sign(
        payload_b64.encode("ascii"),
        padding.PKCS1v15(),
        hashes.SHA256(),
    )
    key = f"SQV1.{payload_b64}.{b64url(sig)}"
    print(key)
    print(f"# exp={exp} hwid={args.hwid} serial={payload['serial']}", flush=True)

    # TODO для бота: после генерации занеси key в свою базу (sqlite/postgres),
    # чтобы можно было отзывать ключи (поле revoked). Онлайн-проверка отзывов — этап v2.


if __name__ == "__main__":
    main()
