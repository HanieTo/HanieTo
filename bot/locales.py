"""Trilingual strings (English / Russian / Persian) for the shop bot.

Telegram renders Persian right-to-left automatically from the text content, so
no special direction handling is needed here.
"""

TEXTS = {
    "en": {
        "language_name": "🇬🇧 English",
        "choose_language": "🌐 Please choose your language:",
        "welcome": "🏪 *Welcome to our shop!*\n\nUse the menu below 👇",
        "btn_products": "🛍 Products",
        "btn_cart": "🛒 Cart",
        "btn_orders": "📦 My Orders",
        "btn_help": "ℹ️ Help",
        "btn_language": "🌐 Language",
        "help": (
            "ℹ️ *Help*\n\nBrowse products by category, check live stock, and place an "
            "order in a few taps. Your order is created as soon as you check out.\n\n"
            "Send /start any time to reopen the menu, and use the ⬅️ / 🏠 buttons to move around."
        ),
        "categories": "🛍 *Categories*\n\nChoose a category:",
        "pick_product": "🗂 *{0}*\n\nChoose a product:",
        "all_products": "🛍",
        "stock": "In stock",
        "price": "Price",
        "out_of_stock_short": "out of stock",
        "choose_quantity": "🔢 How many would you like?",
        "buy": "🛒 Buy",
        "add_to_cart": "➕ Add to cart",
        "added_to_cart": "✅ Added to your cart.",
        "view_cart": "🛒 View cart",
        "continue_shopping": "🛍 Continue shopping",
        "cart_title": "🛒 *Your cart*",
        "cart_empty": "🛒 Your cart is empty.",
        "checkout": "✅ Checkout",
        "clear_cart": "🗑 Clear cart",
        "order_placed": "✅ *Order placed!*\n\n{0}\n\n*{1}: {2}*\nOrder ID: `{3}`",
        "order_total": "Total",
        "out_of_stock": "❌ Sorry, this item is out of stock.",
        "no_orders": "🗂 You haven't placed any orders yet.",
        "your_orders": "📦 *Your recent orders*",
        "no_products": "😔 No products are available right now.",
        "product_gone": "This product is no longer available.",
        "back": "⬅️ Back",
        "home": "🏠 Home",
    },
    "ru": {
        "language_name": "🇷🇺 Русский",
        "choose_language": "🌐 Пожалуйста, выберите язык:",
        "welcome": "🏪 *Добро пожаловать в наш магазин!*\n\nИспользуйте меню ниже 👇",
        "btn_products": "🛍 Товары",
        "btn_cart": "🛒 Корзина",
        "btn_orders": "📦 Мои заказы",
        "btn_help": "ℹ️ Помощь",
        "btn_language": "🌐 Язык",
        "help": (
            "ℹ️ *Помощь*\n\nПросматривайте товары по категориям, проверяйте наличие и "
            "оформляйте заказ в несколько нажатий. Заказ создаётся сразу после оформления.\n\n"
            "Отправьте /start в любой момент, чтобы снова открыть меню, а кнопки ⬅️ / 🏠 — для навигации."
        ),
        "categories": "🛍 *Категории*\n\nВыберите категорию:",
        "pick_product": "🗂 *{0}*\n\nВыберите товар:",
        "all_products": "🛍",
        "stock": "В наличии",
        "price": "Цена",
        "out_of_stock_short": "нет в наличии",
        "choose_quantity": "🔢 Сколько штук вы хотите?",
        "buy": "🛒 Купить",
        "add_to_cart": "➕ В корзину",
        "added_to_cart": "✅ Добавлено в корзину.",
        "view_cart": "🛒 Открыть корзину",
        "continue_shopping": "🛍 Продолжить покупки",
        "cart_title": "🛒 *Ваша корзина*",
        "cart_empty": "🛒 Ваша корзина пуста.",
        "checkout": "✅ Оформить",
        "clear_cart": "🗑 Очистить",
        "order_placed": "✅ *Заказ оформлен!*\n\n{0}\n\n*{1}: {2}*\nНомер заказа: `{3}`",
        "order_total": "Итого",
        "out_of_stock": "❌ К сожалению, товара нет в наличии.",
        "no_orders": "🗂 У вас пока нет заказов.",
        "your_orders": "📦 *Ваши последние заказы*",
        "no_products": "😔 Сейчас нет доступных товаров.",
        "product_gone": "Этот товар больше недоступен.",
        "back": "⬅️ Назад",
        "home": "🏠 Домой",
    },
    "fa": {
        "language_name": "🇮🇷 فارسی",
        "choose_language": "🌐 لطفاً زبان خود را انتخاب کنید:",
        "welcome": "🏪 *به فروشگاه ما خوش آمدید!*\n\nاز منوی زیر استفاده کنید 👇",
        "btn_products": "🛍 محصولات",
        "btn_cart": "🛒 سبد خرید",
        "btn_orders": "📦 سفارش‌های من",
        "btn_help": "ℹ️ راهنما",
        "btn_language": "🌐 زبان",
        "help": (
            "ℹ️ *راهنما*\n\nمحصولات را بر اساس دسته‌بندی ببینید، موجودی را بررسی کنید و با "
            "چند لمس سفارش دهید. سفارش شما بلافاصله پس از تسویه ثبت می‌شود.\n\n"
            "هر زمان دستور /start را بفرستید تا منو دوباره باز شود، و از دکمه‌های ⬅️ / 🏠 برای جابجایی استفاده کنید."
        ),
        "categories": "🛍 *دسته‌بندی‌ها*\n\nیک دسته را انتخاب کنید:",
        "pick_product": "🗂 *{0}*\n\nیک محصول را انتخاب کنید:",
        "all_products": "🛍",
        "stock": "موجودی",
        "price": "قیمت",
        "out_of_stock_short": "ناموجود",
        "choose_quantity": "🔢 چه تعداد می‌خواهید؟",
        "buy": "🛒 خرید",
        "add_to_cart": "➕ افزودن به سبد",
        "added_to_cart": "✅ به سبد خرید شما اضافه شد.",
        "view_cart": "🛒 مشاهده سبد",
        "continue_shopping": "🛍 ادامه خرید",
        "cart_title": "🛒 *سبد خرید شما*",
        "cart_empty": "🛒 سبد خرید شما خالی است.",
        "checkout": "✅ تسویه حساب",
        "clear_cart": "🗑 خالی کردن سبد",
        "order_placed": "✅ *سفارش ثبت شد!*\n\n{0}\n\n*{1}: {2}*\nشماره سفارش: `{3}`",
        "order_total": "مجموع",
        "out_of_stock": "❌ متأسفانه این محصول ناموجود است.",
        "no_orders": "🗂 شما هنوز سفارشی ثبت نکرده‌اید.",
        "your_orders": "📦 *سفارش‌های اخیر شما*",
        "no_products": "😔 در حال حاضر محصولی موجود نیست.",
        "product_gone": "این محصول دیگر در دسترس نیست.",
        "back": "⬅️ بازگشت",
        "home": "🏠 خانه",
    },
}


def t(lang: str, key: str, *args) -> str:
    """Look up a localized string, formatting with args if given."""
    table = TEXTS.get(lang, TEXTS["en"])
    text = table.get(key) or TEXTS["en"].get(key, key)
    return text.format(*args) if args else text
