"""Keyboard builders: everything is an inline keyboard attached to the message
itself, so buttons always render the same way on every Telegram client (no
reply-keyboard bottom bar, which some clients hide behind an icon instead of
docking automatically).
"""
from aiogram.types import InlineKeyboardButton, InlineKeyboardMarkup

from locales import t


def home_menu(lang: str, is_admin: bool = False) -> InlineKeyboardMarkup:
    """The main menu, shown inline on the welcome/home screen."""
    rows = [
        [InlineKeyboardButton(text=t(lang, "btn_products"), callback_data="products")],
        [
            InlineKeyboardButton(text=t(lang, "btn_cart"), callback_data="cart"),
            InlineKeyboardButton(text=t(lang, "btn_orders"), callback_data="orders"),
        ],
        [
            InlineKeyboardButton(text=t(lang, "btn_help"), callback_data="help"),
            InlineKeyboardButton(text=t(lang, "btn_language"), callback_data="language"),
        ],
    ]
    if is_admin:
        rows.append([InlineKeyboardButton(text="🛠 Admin panel", callback_data="admin")])
    return InlineKeyboardMarkup(inline_keyboard=rows)


def language_menu() -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(
        inline_keyboard=[
            [InlineKeyboardButton(text=t("en", "language_name"), callback_data="lang:en")],
            [InlineKeyboardButton(text=t("ru", "language_name"), callback_data="lang:ru")],
            [InlineKeyboardButton(text=t("fa", "language_name"), callback_data="lang:fa")],
        ]
    )


def categories_menu(lang: str, categories: list[str]) -> InlineKeyboardMarkup:
    rows = [[InlineKeyboardButton(text=f"📁 {c}", callback_data=f"cat:{c}")] for c in categories]
    rows.append([InlineKeyboardButton(text=t(lang, "home"), callback_data="home")])
    return InlineKeyboardMarkup(inline_keyboard=rows)


def products_menu(lang: str, products: list[dict], back: str) -> InlineKeyboardMarkup:
    rows = []
    for p in products:
        if p["stock"] > 0:
            label = f"{p['name']} — {money(p['price'])}"
        else:
            label = f"{p['name']} — {t(lang, 'out_of_stock_short')}"
        rows.append([InlineKeyboardButton(text=label, callback_data=f"prod:{p['id']}")])
    rows.append([
        InlineKeyboardButton(text=t(lang, "back"), callback_data=back),
        InlineKeyboardButton(text=t(lang, "home"), callback_data="home"),
    ])
    return InlineKeyboardMarkup(inline_keyboard=rows)


def product_menu(lang: str, product: dict, back: str) -> InlineKeyboardMarkup:
    rows = []
    if product["stock"] > 0:
        rows.append([InlineKeyboardButton(text=t(lang, "buy"), callback_data=f"qty:{product['id']}")])
    rows.append([
        InlineKeyboardButton(text=t(lang, "back"), callback_data=back),
        InlineKeyboardButton(text=t(lang, "home"), callback_data="home"),
    ])
    return InlineKeyboardMarkup(inline_keyboard=rows)


def quantity_menu(lang: str, product: dict) -> InlineKeyboardMarkup:
    max_qty = min(product["stock"], 5)
    qty_row = [
        InlineKeyboardButton(text=str(n), callback_data=f"add:{product['id']}:{n}")
        for n in range(1, max_qty + 1)
    ]
    return InlineKeyboardMarkup(inline_keyboard=[
        qty_row,
        [
            InlineKeyboardButton(text=t(lang, "back"), callback_data=f"prod:{product['id']}"),
            InlineKeyboardButton(text=t(lang, "home"), callback_data="home"),
        ],
    ])


def added_menu(lang: str) -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text=t(lang, "view_cart"), callback_data="cart")],
        [
            InlineKeyboardButton(text=t(lang, "continue_shopping"), callback_data="products"),
            InlineKeyboardButton(text=t(lang, "home"), callback_data="home"),
        ],
    ])


def cart_menu(lang: str) -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text=t(lang, "checkout"), callback_data="checkout")],
        [
            InlineKeyboardButton(text=t(lang, "clear_cart"), callback_data="clearcart"),
            InlineKeyboardButton(text=t(lang, "continue_shopping"), callback_data="products"),
        ],
        [InlineKeyboardButton(text=t(lang, "home"), callback_data="home")],
    ])


def back_home(lang: str, back: str = "home") -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[[
        InlineKeyboardButton(text=t(lang, "back"), callback_data=back),
        InlineKeyboardButton(text=t(lang, "home"), callback_data="home"),
    ]])


def admin_menu() -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="➕ Add product", callback_data="aadd")],
        [InlineKeyboardButton(text="📦 Manage products", callback_data="aproducts")],
        [InlineKeyboardButton(text="📊 Stats", callback_data="astats")],
        [InlineKeyboardButton(text="📢 Broadcast", callback_data="abroadcast")],
        [InlineKeyboardButton(text="🏠 Home", callback_data="home")],
    ])


def admin_cancel() -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[[InlineKeyboardButton(text="❌ Cancel", callback_data="acancel")]])


def admin_products_list(products: list[dict]) -> InlineKeyboardMarkup:
    rows = []
    for p in products:
        status = "✅" if p["is_active"] else "🚫"
        label = f"{status} {p['name']} — {money(p['price'])} ({p['stock']})"
        rows.append([InlineKeyboardButton(text=label, callback_data=f"amanage:{p['id']}")])
    rows.append([InlineKeyboardButton(text="⬅️ Back", callback_data="admin")])
    return InlineKeyboardMarkup(inline_keyboard=rows)


def admin_product_detail(product: dict) -> InlineKeyboardMarkup:
    toggle_label = "🚫 Deactivate" if product["is_active"] else "✅ Activate"
    return InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="📦 Restock", callback_data=f"arestock:{product['id']}")],
        [InlineKeyboardButton(text="💰 Set price", callback_data=f"asetprice:{product['id']}")],
        [InlineKeyboardButton(text=toggle_label, callback_data=f"atoggle:{product['id']}")],
        [InlineKeyboardButton(text="⬅️ Back", callback_data="aproducts")],
    ])


def admin_broadcast_confirm() -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[[
        InlineKeyboardButton(text="✅ Send", callback_data="abroadcastsend"),
        InlineKeyboardButton(text="❌ Cancel", callback_data="abroadcastcancel"),
    ]])


def money(amount: int) -> str:
    """Neutral thousands-grouped number (no currency symbol)."""
    return f"{amount:,}"
