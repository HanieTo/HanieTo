"""Handlers: the conversational shop flow.

Keyboard model: everything is an inline keyboard attached to the message
itself (home menu, categories -> products -> quantity -> cart), edited in
place so menus never stack. There is no reply-keyboard bottom bar - some
Telegram clients (Desktop/Web) don't dock those automatically, which made
them invisible. Stray text that isn't a command is ignored rather than
answered - the menu is a single persisted message, so replying to it would
mean a second, disconnected bot message next to it, not an update to it.
"""
import logging

from aiogram import Bot, F, Router
from aiogram.filters import CommandStart
from aiogram.exceptions import TelegramBadRequest
from aiogram.fsm.context import FSMContext
from aiogram.fsm.state import State, StatesGroup
from aiogram.types import CallbackQuery, Message, ReplyKeyboardRemove

import database as db
import keyboards as kb
from config import ADMIN_CHAT_ID
from locales import t

router = Router()
log = logging.getLogger("shopbot")

# The single live inline menu message per chat, so nothing stacks.
_last_menu: dict[int, int] = {}


def _is_admin(chat_id: int) -> bool:
    return bool(ADMIN_CHAT_ID) and str(chat_id) == str(ADMIN_CHAT_ID)


class AddProduct(StatesGroup):
    details = State()


class EditProduct(StatesGroup):
    stock = State()
    price = State()


class Broadcast(StatesGroup):
    message = State()


ADMIN_ACTIONS = {
    "admin", "aadd", "aproducts", "amanage", "arestock", "asetprice",
    "atoggle", "astats", "abroadcast", "abroadcastsend", "abroadcastcancel", "acancel",
}


# --- entry points ---

@router.message(CommandStart())
async def on_start(message: Message, bot: Bot, state: FSMContext) -> None:
    log.info("START from chat_id=%s (%s)", message.chat.id, message.chat.type)
    chat_id = message.chat.id
    await state.clear()
    from_user = message.from_user
    await db.record_user(chat_id, from_user.username if from_user else None, from_user.first_name if from_user else None)
    # One-time cleanup: clears any stale reply-keyboard state from older bot
    # versions client-side, since a message can't carry both a reply-keyboard
    # removal and an inline keyboard at once.
    await _reset_reply_keyboard(bot, chat_id)
    lang = await db.get_language(chat_id)
    await show_welcome(bot, chat_id, lang)


# --- admin: text flows (add product / restock / reprice / broadcast) ---
#
# Add-product asks for everything in ONE message (pipe-separated) rather than
# a multi-step Q&A: each back-and-forth turn in a text wizard reads as its own
# "page" flipping by, since the admin's reply and the bot's next question are
# each a distinct message bubble. One message in, one confirmation out keeps
# that to a minimum.

@router.message(AddProduct.details)
async def admin_add_details(message: Message, bot: Bot, state: FSMContext) -> None:
    if not _is_admin(message.chat.id):
        return
    parts = [p.strip() for p in (message.text or "").split("|")]
    if len(parts) < 4 or not parts[0] or not parts[1] or not parts[2].isdigit() or not parts[3].isdigit():
        await _admin_prompt(
            bot,
            message.chat.id,
            "⚠️ Please send: `Name | Category | Price | Stock | Description (optional)`\n"
            "Price and stock must be whole numbers.",
        )
        return
    name, category, price_str, stock_str = parts[0], parts[1], parts[2], parts[3]
    description = parts[4] if len(parts) > 4 else ""
    await db.add_product(name, description, int(price_str), int(stock_str), category)
    await state.clear()
    text = f"✅ Added *{name}* — {kb.money(int(price_str))}, stock {stock_str}."
    await _admin_prompt(bot, message.chat.id, text, kb.admin_menu())


@router.message(EditProduct.stock)
async def admin_edit_stock(message: Message, bot: Bot, state: FSMContext) -> None:
    if not _is_admin(message.chat.id):
        return
    try:
        stock = int((message.text or "").strip())
        if stock < 0:
            raise ValueError
    except ValueError:
        await _admin_prompt(bot, message.chat.id, "⚠️ Please send a valid non-negative number.")
        return
    data = await state.get_data()
    product_id = data["product_id"]
    await db.set_product_stock(product_id, stock)
    await state.clear()
    await show_admin_product_detail(bot, message.chat.id, None, product_id, note=f"✅ Stock updated to {stock}.", fresh=True)


@router.message(EditProduct.price)
async def admin_edit_price(message: Message, bot: Bot, state: FSMContext) -> None:
    if not _is_admin(message.chat.id):
        return
    try:
        price = int((message.text or "").strip())
        if price < 0:
            raise ValueError
    except ValueError:
        await _admin_prompt(bot, message.chat.id, "⚠️ Please send a valid non-negative number.")
        return
    data = await state.get_data()
    product_id = data["product_id"]
    await db.set_product_price(product_id, price)
    await state.clear()
    await show_admin_product_detail(bot, message.chat.id, None, product_id, note=f"✅ Price updated to {kb.money(price)}.", fresh=True)


@router.message(Broadcast.message)
async def admin_broadcast_text(message: Message, bot: Bot, state: FSMContext) -> None:
    if not _is_admin(message.chat.id):
        return
    text = (message.text or "").strip()
    if not text:
        return
    await state.update_data(broadcast_text=text)
    preview = f"📢 *Preview*\n\n{text}\n\nSend this to all customers?"
    await _admin_prompt(bot, message.chat.id, preview, kb.admin_broadcast_confirm())


@router.message(F.text)
async def on_text(message: Message, bot: Bot) -> None:
    """Anything not caught by an earlier handler: a stray command re-opens the
    menu; anything else is ignored. The menu is already on screen (it's a
    persisted single message), so replying to random text would only create a
    second, disconnected bot message alongside it - not worth it."""
    chat_id = message.chat.id
    text = (message.text or "").strip()
    if text.startswith("/"):
        lang = await db.get_language(chat_id)
        await show_welcome(bot, chat_id, lang)


@router.callback_query()
async def on_callback(cb: CallbackQuery, bot: Bot, state: FSMContext) -> None:
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
        await show_welcome(bot, chat_id, lang, message_id)
        return

    lang = await db.get_language(chat_id)
    await do_action(bot, chat_id, message_id, action, lang, parts, state)


# --- action dispatch (inline callbacks) ---

async def do_action(
    bot: Bot,
    chat_id: int,
    message_id: int | None,
    action: str,
    lang: str,
    parts: list[str] | None = None,
    state: FSMContext | None = None,
) -> None:
    parts = parts or [action]

    if action in ADMIN_ACTIONS and not _is_admin(chat_id):
        return

    if action == "home":
        await show_welcome(bot, chat_id, lang, message_id)
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
    elif action == "admin":
        await show_admin_panel(bot, chat_id, message_id)
    elif action == "aadd":
        await state.set_state(AddProduct.details)
        prompt = (
            "➕ *Add product*\n\n"
            "Send one message in this format:\n"
            "`Name | Category | Price | Stock | Description (optional)`\n\n"
            "Example:\n`Wireless Mouse | Electronics | 450000 | 10 | Ergonomic wireless mouse`"
        )
        await render(bot, chat_id, message_id, prompt, kb.admin_cancel())
    elif action == "aproducts":
        await show_admin_products(bot, chat_id, message_id)
    elif action == "amanage":
        await show_admin_product_detail(bot, chat_id, message_id, parts[1])
    elif action == "arestock":
        await state.update_data(product_id=parts[1])
        await state.set_state(EditProduct.stock)
        await render(bot, chat_id, message_id, "📦 Send the new stock quantity (number).", kb.admin_cancel())
    elif action == "asetprice":
        await state.update_data(product_id=parts[1])
        await state.set_state(EditProduct.price)
        await render(bot, chat_id, message_id, "💰 Send the new price (number, no currency symbol).", kb.admin_cancel())
    elif action == "atoggle":
        await toggle_product_active(bot, chat_id, message_id, parts[1])
    elif action == "astats":
        await show_admin_stats(bot, chat_id, message_id)
    elif action == "abroadcast":
        await state.set_state(Broadcast.message)
        await render(bot, chat_id, message_id, "📢 Send the message to broadcast to all customers.", kb.admin_cancel())
    elif action == "abroadcastsend":
        await do_broadcast(bot, chat_id, message_id, state)
    elif action in ("acancel", "abroadcastcancel"):
        await state.clear()
        await show_admin_panel(bot, chat_id, message_id)
    else:
        await show_welcome(bot, chat_id, lang, message_id)


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


# --- welcome / render ---

async def show_welcome(bot: Bot, chat_id: int, lang: str, message_id: int | None = None) -> None:
    """The home screen: greeting + inline menu. Edits in place when possible."""
    await render(bot, chat_id, message_id, t(lang, "welcome"), kb.home_menu(lang, _is_admin(chat_id)))


# --- admin screens ---

async def _admin_prompt(bot: Bot, chat_id: int, text: str, keyboard=None) -> None:
    """Used when replying to text the admin just typed. Sends a fresh message
    at the bottom instead of editing the old prompt in place - an edit doesn't
    move the message, but Telegram scrolls the view down to the admin's new
    text, so an in-place edit would sit invisibly above the fold."""
    await _fresh(bot, chat_id, text, keyboard or kb.admin_cancel())


async def _fresh(bot: Bot, chat_id: int, text: str, keyboard) -> None:
    old = _last_menu.pop(chat_id, None)
    if old is not None:
        await _try_delete(bot, chat_id, old)
    msg = await bot.send_message(chat_id, text, reply_markup=keyboard)
    _last_menu[chat_id] = msg.message_id


async def show_admin_panel(bot: Bot, chat_id: int, message_id: int | None) -> None:
    if not _is_admin(chat_id):
        return
    mid = message_id if message_id is not None else _last_menu.get(chat_id)
    await render(bot, chat_id, mid, "🛠 *Admin panel*", kb.admin_menu())


async def show_admin_products(bot: Bot, chat_id: int, message_id: int | None) -> None:
    if not _is_admin(chat_id):
        return
    mid = message_id if message_id is not None else _last_menu.get(chat_id)
    products = await db.get_all_products()
    if not products:
        await render(bot, chat_id, mid, "😔 No products yet.", kb.admin_menu())
        return
    await render(bot, chat_id, mid, "📦 *Products*\n\nTap one to manage it.", kb.admin_products_list(products))


async def show_admin_product_detail(
    bot: Bot, chat_id: int, message_id: int | None, product_id: str, note: str = "", fresh: bool = False
) -> None:
    if not _is_admin(chat_id):
        return
    product = await db.get_product_any(product_id)
    if not product:
        text, keyboard = "This product no longer exists.", kb.admin_menu()
    else:
        status = "✅ Active" if product["is_active"] else "🚫 Inactive"
        desc_line = f"{product['description']}\n" if product["description"] else ""
        text = (
            f"{note + chr(10) + chr(10) if note else ''}"
            f"🛍 *{product['name']}*\n{desc_line}"
            f"💰 {kb.money(product['price'])}   📦 {product['stock']}   {status}\n"
            f"📁 {product['category'] or '-'}"
        )
        keyboard = kb.admin_product_detail(product)

    if fresh:
        await _fresh(bot, chat_id, text, keyboard)
    else:
        mid = message_id if message_id is not None else _last_menu.get(chat_id)
        await render(bot, chat_id, mid, text, keyboard)


async def toggle_product_active(bot: Bot, chat_id: int, message_id: int | None, product_id: str) -> None:
    if not _is_admin(chat_id):
        return
    product = await db.get_product_any(product_id)
    if product:
        await db.set_product_active(product_id, not product["is_active"])
    await show_admin_product_detail(bot, chat_id, message_id, product_id)


async def show_admin_stats(bot: Bot, chat_id: int, message_id: int | None) -> None:
    if not _is_admin(chat_id):
        return
    mid = message_id if message_id is not None else _last_menu.get(chat_id)
    stats = await db.get_stats()
    text = (
        "📊 *Stats*\n\n"
        f"Orders: {stats['order_count']}\n"
        f"Revenue: {kb.money(stats['revenue'])}\n"
        f"Active products: {stats['active_products']}\n"
        f"Buyers: {stats['buyers']}\n"
        f"Known chats: {stats['users_count']}"
    )
    await render(bot, chat_id, mid, text, kb.admin_menu())


async def do_broadcast(bot: Bot, chat_id: int, message_id: int | None, state: FSMContext) -> None:
    if not _is_admin(chat_id):
        return
    data = await state.get_data()
    text = data.get("broadcast_text", "")
    await state.clear()
    chat_ids = await db.get_all_user_chat_ids()
    sent = 0
    for uid in chat_ids:
        try:
            await bot.send_message(uid, text)
            sent += 1
        except Exception:
            pass
    mid = message_id if message_id is not None else _last_menu.get(chat_id)
    await render(bot, chat_id, mid, f"✅ Broadcast sent to {sent}/{len(chat_ids)} chats.", kb.admin_menu())


async def _reset_reply_keyboard(bot: Bot, chat_id: int) -> None:
    try:
        msg = await bot.send_message(chat_id, ".", reply_markup=ReplyKeyboardRemove())
        await _try_delete(bot, chat_id, msg.message_id)
    except Exception:
        pass


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


async def _try_delete(bot: Bot, chat_id: int, message_id: int) -> None:
    try:
        await bot.delete_message(chat_id, message_id)
    except Exception:
        pass
