"""Keyboard builders: a persistent bottom bar (reply keyboard) + inline menus."""
from aiogram.types import (
    InlineKeyboardButton,
    InlineKeyboardMarkup,
    KeyboardButton,
    ReplyKeyboardMarkup,
)

from locales import t


def main_bar(lang: str) -> ReplyKeyboardMarkup:
    """The persistent menu docked at the bottom of the screen."""
    return ReplyKeyboardMarkup(
        keyboard=[
            [KeyboardButton(text=t(lang, "btn_products"))],
            [KeyboardButton(text=t(lang, "btn_cart")), KeyboardButton(text=t(lang, "btn_orders"))],
            [KeyboardButton(text=t(lang, "btn_help")), KeyboardButton(text=t(lang, "btn_language"))],
        ],
        resize_keyboard=True,
        is_persistent=True,
    )


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


def money(amount: int) -> str:
    """Neutral thousands-grouped number (no currency symbol)."""
    return f"{amount:,}"
