"""Configuration for the OmniCommerce Telegram shop bot.

Reads secrets from environment variables so nothing sensitive is committed:
  BOT_TOKEN       - the token from @BotFather (required)
  ADMIN_CHAT_ID   - chat id that receives a notification on each order (optional)
  DB_PATH         - SQLite file path (optional, defaults to shop.db next to the bot)
"""
import os

BOT_TOKEN = os.getenv("BOT_TOKEN", "")
ADMIN_CHAT_ID = os.getenv("ADMIN_CHAT_ID", "")
DB_PATH = os.getenv("DB_PATH", os.path.join(os.path.dirname(__file__), "shop.db"))

DEFAULT_LANGUAGE = "en"
SUPPORTED_LANGUAGES = ("en", "ru", "fa")
