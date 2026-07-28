"""Handlers: the conversational shop flow.

Keyboard model (matches what the user asked for):
- A persistent bottom bar (reply keyboard) is the always-visible main menu.
- Inline keyboards drive drill-down (categories -> products -> quantity -> cart),
  edited in place so menus never stack.
- Typing a non-menu message shows one short hint that never stacks - it does not
  spawn a new menu.
"""
from aiogram import Bot, F, Router
from aiogram.filters import CommandStart
from aiogram.exceptions import TelegramBadRequest
from aiogram.types import CallbackQuery, Message

import database as db
import keyboards as kb
from config import ADMIN_CHAT_ID
from locales import bar_label_to_lang_and_action, t

router = Router()

# The single live inline menu / hint message per chat, so nothing stacks.
_last_menu: dict[int, int] = {}
_last_hint: dict[int, int] = {}


# --- entry points ---

@router.message(CommandStart())
async def on_start(message: Message, bot: Bot) -> None:
    lang = await db.get_language(message.chat.id)
    await show_welcome(bot, message.chat.id, lang)


@router.message(F.text)
async def on_text(message: Message, bot: Bot) -> None:
    chat_id = message.chat.id
    text = (message.text or "").strip()

    # A tap on the bottom bar arrives as text matching a button label.
    action = bar_label_to_lang_and_action(text)
    if action:
        lang = await db.get_language(chat_id)
        await _clear_hint(bot, chat_id)
        await do_action(bot, chat_id, None, action, lang)
        return

    # Anything else that isn't a command: one non-stacking hint.
    if not text.startswith("/"):
        lang = await db.get_language(chat_id)
        await show_hint(bot, chat_id, lang)
    else:
        lang = await db.get_language(chat_id)
        await show_welcome(bot, chat_id, lang)


@router.callback_query()
async def on_callback(cb: CallbackQuery, bot: Bot) -> None:
    await cb.answer()
    if cb.message is None:
        return

    chat_id = cb.message.chat.id
    message_id = cb.message.message_id
    data = cb.data or ""
    parts = data.split(":")
    action = parts[0]

    if action == "lang":
        lang = parts[1] if len(parts) > 1 else "en"
        await db.set_language(chat_id, lang)
        await show_welcome(bot, chat_id, lang)
        return

    lang = await db.get_language(chat_id)
    await do_action(bot, chat_id, message_id, action, lang, parts)


# --- action dispatch (shared by bottom-bar taps and inline callbacks) ---

async def do_action(bot: Bot, chat_id: int, message_id: int | None, action: str, lang: str, parts: list[str] | None = None) -> None:
    parts = parts or [action]

    if action == "home":
        await show_welcome(bot, chat_id, lang)
    elif action == "language":
        await render(bot, chat_id, message_id, t(lang, "choose_language"), kb.language_menu())
    elif action == "products":
        await show_categories(bot, chat_id, message_id, lang)
    elif action == "cat":
        await show_products(bot, chat_id, message_id, lang, parts[1])
    elif action == "prod":
        await show_product(bot, chat_id, message_id, lang, parts[1])
    elif action == "qty":
        await show_quantity(bot, chat_id, message_id, lang, parts[1])
    elif action == "add":
        await add_to_cart(bot, chat_id, message_id, lang, parts[1], int(parts[2]))
    elif action == "cart":
        await show_cart(bot, chat_id, message_id, lang)
    elif action == "checkout":
        await do_checkout(bot, chat_id, message_id, lang)
    elif action == "clearcart":
        await db.clear_cart(chat_id)
        await render(bot, chat_id, message_id, t(lang, "cart_empty"), kb.back_home(lang))
    elif action == "orders":
        await show_orders(bot, chat_id, message_id, lang)
    elif action == "help":
        await render(bot, chat_id, message_id, t(lang, "help"), kb.back_home(lang))
    else:
        await show_welcome(bot, chat_id, lang)


# --- screens ---

async def show_categories(bot: Bot, chat_id: int, message_id: int | None, lang: str) -> None:
    categories = await db.get_categories()
    if not categories:
        await show_products(bot, chat_id, message_id, lang, "")
        return
    await render(bot, chat_id, message_id, t(lang, "categories"), kb.categories_menu(lang, categories))


async def show_products(bot: Bot, chat_id: int, message_id: int | None, lang: str, category: str) -> None:
    products = await db.get_products(category or None)
    if not products:
        await render(bot, chat_id, message_id, t(lang, "no_products"), kb.back_home(lang, "products"))
        return
    title = t(lang, "pick_product", category or t(lang, "all_products"))
    await render(bot, chat_id, message_id, title, kb.products_menu(lang, products, "products"))


async def show_product(bot: Bot, chat_id: int, message_id: int | None, lang: str, product_id: str) -> None:
    product = await db.get_product(product_id)
    if not product:
        await render(bot, chat_id, message_id, t(lang, "product_gone"), kb.back_home(lang, "products"))
        return

    desc = f"{product['description']}\n\n" if product["description"] else ""
    text = (
        f"🛍 *{product['name']}*\n\n{desc}"
        f"💰 {t(lang, 'price')}: *{kb.money(product['price'])}*\n"
        f"📦 {t(lang, 'stock')}: {product['stock']}"
    )
    back = f"cat:{product['category']}" if product["category"] else "products"
    await render(bot, chat_id, message_id, text, kb.product_menu(lang, product, back))


async def show_quantity(bot: Bot, chat_id: int, message_id: int | None, lang: str, product_id: str) -> None:
    product = await db.get_product(product_id)
    if not product or product["stock"] <= 0:
        await render(bot, chat_id, message_id, t(lang, "out_of_stock"), kb.back_home(lang, "products"))
        return
    text = f"🛍 *{product['name']}*\n\n{t(lang, 'choose_quantity')}"
    await render(bot, chat_id, message_id, text, kb.quantity_menu(lang, product))


async def add_to_cart(bot: Bot, chat_id: int, message_id: int | None, lang: str, product_id: str, quantity: int) -> None:
    product = await db.get_product(product_id)
    if not product or product["stock"] < quantity:
        await render(bot, chat_id, message_id, t(lang, "out_of_stock"), kb.back_home(lang, "products"))
        return
    await db.add_to_cart(chat_id, product_id, quantity)
    text = f"{t(lang, 'added_to_cart')}\n\n🛍 {product['name']} × {quantity}"
    await render(bot, chat_id, message_id, text, kb.added_menu(lang))


async def show_cart(bot: Bot, chat_id: int, message_id: int | None, lang: str) -> None:
    cart = await db.get_cart(chat_id)
    if not cart:
        await render(bot, chat_id, message_id, t(lang, "cart_empty"), kb.back_home(lang))
        return
    lines = "\n".join(f"• {c['name']} × {c['quantity']} — {kb.money(c['price'] * c['quantity'])}" for c in cart)
    total = sum(c["price"] * c["quantity"] for c in cart)
    text = f"{t(lang, 'cart_title')}\n\n{lines}\n\n*{t(lang, 'order_total')}: {kb.money(total)}*"
    await render(bot, chat_id, message_id, text, kb.cart_menu(lang))


async def do_checkout(bot: Bot, chat_id: int, message_id: int | None, lang: str) -> None:
    order = await db.checkout(chat_id)
    if not order:
        await render(bot, chat_id, message_id, t(lang, "cart_empty"), kb.back_home(lang))
        return
    summary = "\n".join(f"• {i['name']} × {i['quantity']} — {kb.money(i['unit_price'] * i['quantity'])}" for i in order["items"])
    text = t(lang, "order_placed", summary, t(lang, "order_total"), kb.money(order["total"]), order["id"])
    await render(bot, chat_id, message_id, text, kb.back_home(lang))
    await notify_admin(bot, chat_id, order)


async def show_orders(bot: Bot, chat_id: int, message_id: int | None, lang: str) -> None:
    orders = await db.get_orders(chat_id)
    if not orders:
        await render(bot, chat_id, message_id, t(lang, "no_orders"), kb.back_home(lang))
        return
    import datetime
    blocks = []
    for o in orders:
        when = datetime.datetime.utcfromtimestamp(o["created_at"]).strftime("%Y-%m-%d %H:%M")
        items = "\n".join(f"• {i['name']} × {i['quantity']} — {kb.money(i['unit_price'] * i['quantity'])}" for i in o["items"])
        blocks.append(f"🗓 {when}\n{items}\n*{t(lang, 'order_total')}: {kb.money(o['total'])}*")
    text = t(lang, "your_orders") + "\n\n" + "\n\n".join(blocks)
    await render(bot, chat_id, message_id, text, kb.back_home(lang))


async def notify_admin(bot: Bot, buyer_chat_id: int, order: dict) -> None:
    if not ADMIN_CHAT_ID:
        return
    summary = "\n".join(f"• {i['name']} × {i['quantity']} — {kb.money(i['unit_price'] * i['quantity'])}" for i in order["items"])
    text = f"🔔 *New order*\nFrom chat: `{buyer_chat_id}`\n\n{summary}\n\n*Total: {kb.money(order['total'])}*\nOrder ID: `{order['id']}`"
    try:
        await bot.send_message(int(ADMIN_CHAT_ID), text)
    except Exception:
        pass


# --- welcome / hint / render ---

async def show_welcome(bot: Bot, chat_id: int, lang: str) -> None:
    """Docks the bottom bar (reply keyboard persists) and greets. Replaces the live menu."""
    await _clear_hint(bot, chat_id)
    old = _last_menu.pop(chat_id, None)
    if old is not None:
        await _try_delete(bot, chat_id, old)
    msg = await bot.send_message(chat_id, t(lang, "welcome"), reply_markup=kb.main_bar(lang))
    _last_menu[chat_id] = msg.message_id


async def render(bot: Bot, chat_id: int, message_id: int | None, text: str, keyboard) -> None:
    """Edit the live menu in place (button tap) or move it to the bottom (bar tap)."""
    if message_id is not None:
        try:
            await bot.edit_message_text(text, chat_id=chat_id, message_id=message_id, reply_markup=keyboard)
            _last_menu[chat_id] = message_id
            return
        except TelegramBadRequest as e:
            if "message is not modified" in str(e):
                _last_menu[chat_id] = message_id
                return
            # otherwise fall through and send a fresh message

    old = _last_menu.pop(chat_id, None)
    if old is not None and old != message_id:
        await _try_delete(bot, chat_id, old)
    msg = await bot.send_message(chat_id, text, reply_markup=keyboard)
    _last_menu[chat_id] = msg.message_id


async def show_hint(bot: Bot, chat_id: int, lang: str) -> None:
    text = t(lang, "use_menu")
    hid = _last_hint.get(chat_id)
    if hid is not None:
        try:
            await bot.edit_message_text(text, chat_id=chat_id, message_id=hid)
            return
        except TelegramBadRequest as e:
            if "message is not modified" in str(e):
                return
    msg = await bot.send_message(chat_id, text)
    _last_hint[chat_id] = msg.message_id


async def _clear_hint(bot: Bot, chat_id: int) -> None:
    hid = _last_hint.pop(chat_id, None)
    if hid is not None:
        await _try_delete(bot, chat_id, hid)


async def _try_delete(bot: Bot, chat_id: int, message_id: int) -> None:
    try:
        await bot.delete_message(chat_id, message_id)
    except Exception:
        pass
