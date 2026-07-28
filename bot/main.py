"""Entry point: sets up the bot, dispatcher, and starts long-polling."""
import asyncio
import logging

from aiogram import Bot, Dispatcher
from aiogram.client.default import DefaultBotProperties
from aiogram.enums import ParseMode

import database as db
from config import BOT_TOKEN
from handlers import router


async def main() -> None:
    logging.basicConfig(level=logging.INFO)

    if not BOT_TOKEN:
        raise SystemExit("BOT_TOKEN environment variable is not set.")

    await db.init_db()

    bot = Bot(BOT_TOKEN, default=DefaultBotProperties(parse_mode=ParseMode.MARKDOWN))
    dp = Dispatcher()
    dp.include_router(router)

    # Drop any backlog so a restart doesn't replay old taps.
    await bot.delete_webhook(drop_pending_updates=True)
    logging.info("Shop bot started, polling for updates.")
    await dp.start_polling(bot)


if __name__ == "__main__":
    asyncio.run(main())
