# OmniCommerce Telegram Shop Bot (Python / aiogram 3)

An interactive Telegram shop bot: customers browse products by category, check live
stock, add to a cart, and check out — all in chat. Modeled on the design of the
open-source [AiogramShopBot](https://github.com/ilyarolf/AiogramShopBot), but kept
light (SQLite, no external services) so it runs with just a bot token.

## Features

- Persistent bottom-bar menu + inline drill-down (edited in place, never stacks)
- Categories → products (live stock) → quantity → cart → checkout
- Cart with multiple items; orders with history
- Three languages: English / Russian / Persian (per-chat, persisted)
- Optional admin notification on each order

## Setup

```bash
cd bot
python -m venv .venv
.venv/Scripts/activate        # Windows;  source .venv/bin/activate on macOS/Linux
pip install -r requirements.txt

# configure (get the token from @BotFather)
set BOT_TOKEN=123456:ABC...   # PowerShell: $env:BOT_TOKEN="..."
set ADMIN_CHAT_ID=your_id     # optional, for order notifications

python seed_data.py           # add sample products (once)
python main.py                # run the bot
```

Only ever run **one** instance per bot token (two pollers cause Telegram 409 conflicts).

## Files

- `main.py` — bot setup + polling
- `handlers.py` — the conversational flow
- `keyboards.py` — bottom bar + inline menus
- `database.py` — SQLite (products, cart, orders, language prefs)
- `locales.py` — EN/RU/FA strings
- `seed_data.py` — sample products
