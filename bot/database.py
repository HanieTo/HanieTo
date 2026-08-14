"""SQLite persistence for the shop bot (async, via aiosqlite).

Zero external infrastructure - a single file DB. Mirrors the data a shop bot
needs: products with stock, per-chat language, a cart, and orders.
"""
import aiosqlite
import time
import uuid

from config import DB_PATH, DEFAULT_LANGUAGE


async def init_db() -> None:
    async with aiosqlite.connect(DB_PATH) as db:
        await db.executescript(
            """
            CREATE TABLE IF NOT EXISTS products (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT,
                price INTEGER NOT NULL,
                stock INTEGER NOT NULL,
                category TEXT,
                is_active INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS chat_prefs (
                chat_id TEXT PRIMARY KEY,
                language TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS users (
                chat_id TEXT PRIMARY KEY,
                username TEXT,
                first_name TEXT,
                first_seen INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS cart (
                chat_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                quantity INTEGER NOT NULL,
                PRIMARY KEY (chat_id, product_id)
            );
            CREATE TABLE IF NOT EXISTS orders (
                id TEXT PRIMARY KEY,
                chat_id TEXT NOT NULL,
                created_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS order_items (
                order_id TEXT NOT NULL,
                product_name TEXT NOT NULL,
                unit_price INTEGER NOT NULL,
                quantity INTEGER NOT NULL
            );
            """
        )
        await db.commit()


# --- language preference ---

async def get_language(chat_id: int) -> str:
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute("SELECT language FROM chat_prefs WHERE chat_id = ?", (str(chat_id),)) as cur:
            row = await cur.fetchone()
            return row[0] if row else DEFAULT_LANGUAGE


async def has_language(chat_id: int) -> bool:
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute("SELECT 1 FROM chat_prefs WHERE chat_id = ?", (str(chat_id),)) as cur:
            return await cur.fetchone() is not None


async def set_language(chat_id: int, language: str) -> None:
    async with aiosqlite.connect(DB_PATH) as db:
        await db.execute(
            "INSERT INTO chat_prefs (chat_id, language) VALUES (?, ?) "
            "ON CONFLICT(chat_id) DO UPDATE SET language = excluded.language",
            (str(chat_id), language),
        )
        await db.commit()


# --- known users (for admin broadcast/stats) ---

async def record_user(chat_id: int, username: str | None, first_name: str | None) -> None:
    async with aiosqlite.connect(DB_PATH) as db:
        await db.execute(
            "INSERT INTO users (chat_id, username, first_name, first_seen) VALUES (?, ?, ?, ?) "
            "ON CONFLICT(chat_id) DO UPDATE SET username = excluded.username, first_name = excluded.first_name",
            (str(chat_id), username, first_name, int(time.time())),
        )
        await db.commit()


async def get_all_user_chat_ids() -> list[int]:
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute("SELECT chat_id FROM users") as cur:
            return [int(r[0]) for r in await cur.fetchall()]


# --- products ---

async def get_categories() -> list[str]:
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute(
            "SELECT DISTINCT category FROM products WHERE is_active = 1 AND category IS NOT NULL ORDER BY category"
        ) as cur:
            return [row[0] for row in await cur.fetchall()]


async def get_products(category: str | None = None) -> list[dict]:
    query = "SELECT id, name, description, price, stock, category FROM products WHERE is_active = 1"
    params: tuple = ()
    if category:
        query += " AND category = ?"
        params = (category,)
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute(query, params) as cur:
            return [_product_row(r) for r in await cur.fetchall()]


async def get_product(product_id: str) -> dict | None:
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute(
            "SELECT id, name, description, price, stock, category FROM products WHERE id = ?", (product_id,)
        ) as cur:
            row = await cur.fetchone()
            return _product_row(row) if row else None


def _product_row(row) -> dict:
    return {
        "id": row[0],
        "name": row[1],
        "description": row[2],
        "price": row[3],
        "stock": row[4],
        "category": row[5],
    }


async def add_product(name: str, description: str, price: int, stock: int, category: str) -> None:
    async with aiosqlite.connect(DB_PATH) as db:
        await db.execute(
            "INSERT INTO products (id, name, description, price, stock, category) VALUES (?, ?, ?, ?, ?, ?)",
            (uuid.uuid4().hex, name, description, price, stock, category),
        )
        await db.commit()


async def product_count() -> int:
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute("SELECT COUNT(*) FROM products") as cur:
            row = await cur.fetchone()
            return row[0]


# --- admin: product management (includes inactive products, unlike the shop-facing queries above) ---

async def get_all_products() -> list[dict]:
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute(
            "SELECT id, name, description, price, stock, category, is_active FROM products ORDER BY name"
        ) as cur:
            rows = await cur.fetchall()
    return [_admin_product_row(r) for r in rows]


async def get_product_any(product_id: str) -> dict | None:
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute(
            "SELECT id, name, description, price, stock, category, is_active FROM products WHERE id = ?",
            (product_id,),
        ) as cur:
            row = await cur.fetchone()
            return _admin_product_row(row) if row else None


def _admin_product_row(row) -> dict:
    return {
        "id": row[0],
        "name": row[1],
        "description": row[2],
        "price": row[3],
        "stock": row[4],
        "category": row[5],
        "is_active": bool(row[6]),
    }


async def set_product_stock(product_id: str, stock: int) -> None:
    async with aiosqlite.connect(DB_PATH) as db:
        await db.execute("UPDATE products SET stock = ? WHERE id = ?", (stock, product_id))
        await db.commit()


async def set_product_price(product_id: str, price: int) -> None:
    async with aiosqlite.connect(DB_PATH) as db:
        await db.execute("UPDATE products SET price = ? WHERE id = ?", (price, product_id))
        await db.commit()


async def set_product_active(product_id: str, is_active: bool) -> None:
    async with aiosqlite.connect(DB_PATH) as db:
        await db.execute("UPDATE products SET is_active = ? WHERE id = ?", (1 if is_active else 0, product_id))
        await db.commit()


# --- admin: stats ---

async def get_stats() -> dict:
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute(
            "SELECT COUNT(DISTINCT o.id), COALESCE(SUM(oi.unit_price * oi.quantity), 0) "
            "FROM orders o JOIN order_items oi ON oi.order_id = o.id"
        ) as cur:
            order_count, revenue = await cur.fetchone()
        async with db.execute("SELECT COUNT(*) FROM products WHERE is_active = 1") as cur:
            (active_products,) = await cur.fetchone()
        async with db.execute("SELECT COUNT(DISTINCT chat_id) FROM orders") as cur:
            (buyers,) = await cur.fetchone()
        async with db.execute("SELECT COUNT(*) FROM users") as cur:
            (users_count,) = await cur.fetchone()
    return {
        "order_count": order_count,
        "revenue": revenue,
        "active_products": active_products,
        "buyers": buyers,
        "users_count": users_count,
    }


# --- cart ---

async def add_to_cart(chat_id: int, product_id: str, quantity: int) -> None:
    async with aiosqlite.connect(DB_PATH) as db:
        await db.execute(
            "INSERT INTO cart (chat_id, product_id, quantity) VALUES (?, ?, ?) "
            "ON CONFLICT(chat_id, product_id) DO UPDATE SET quantity = quantity + excluded.quantity",
            (str(chat_id), product_id, quantity),
        )
        await db.commit()


async def get_cart(chat_id: int) -> list[dict]:
    """Returns cart lines joined with current product name/price/stock."""
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute(
            "SELECT c.product_id, c.quantity, p.name, p.price, p.stock "
            "FROM cart c JOIN products p ON p.id = c.product_id WHERE c.chat_id = ?",
            (str(chat_id),),
        ) as cur:
            return [
                {"product_id": r[0], "quantity": r[1], "name": r[2], "price": r[3], "stock": r[4]}
                for r in await cur.fetchall()
            ]


async def clear_cart(chat_id: int) -> None:
    async with aiosqlite.connect(DB_PATH) as db:
        await db.execute("DELETE FROM cart WHERE chat_id = ?", (str(chat_id),))
        await db.commit()


# --- orders ---

async def checkout(chat_id: int) -> dict | None:
    """Turns the cart into an order, decrementing stock. Returns the order or None."""
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute(
            "SELECT c.product_id, c.quantity, p.name, p.price, p.stock "
            "FROM cart c JOIN products p ON p.id = c.product_id WHERE c.chat_id = ?",
            (str(chat_id),),
        ) as cur:
            cart = await cur.fetchall()

        if not cart:
            return None

        order_id = uuid.uuid4().hex
        items = []
        total = 0
        for product_id, quantity, name, price, stock in cart:
            qty = min(quantity, stock)
            if qty <= 0:
                continue
            await db.execute("UPDATE products SET stock = stock - ? WHERE id = ?", (qty, product_id))
            await db.execute(
                "INSERT INTO order_items (order_id, product_name, unit_price, quantity) VALUES (?, ?, ?, ?)",
                (order_id, name, price, qty),
            )
            items.append({"name": name, "unit_price": price, "quantity": qty})
            total += price * qty

        if not items:
            await db.execute("DELETE FROM cart WHERE chat_id = ?", (str(chat_id),))
            await db.commit()
            return None

        await db.execute(
            "INSERT INTO orders (id, chat_id, created_at) VALUES (?, ?, ?)",
            (order_id, str(chat_id), int(time.time())),
        )
        await db.execute("DELETE FROM cart WHERE chat_id = ?", (str(chat_id),))
        await db.commit()
        return {"id": order_id, "items": items, "total": total}


async def get_orders(chat_id: int, limit: int = 10) -> list[dict]:
    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute(
            "SELECT id, created_at FROM orders WHERE chat_id = ? ORDER BY created_at DESC LIMIT ?",
            (str(chat_id), limit),
        ) as cur:
            orders = await cur.fetchall()

        result = []
        for order_id, created_at in orders:
            async with db.execute(
                "SELECT product_name, unit_price, quantity FROM order_items WHERE order_id = ?", (order_id,)
            ) as icur:
                items = [{"name": r[0], "unit_price": r[1], "quantity": r[2]} for r in await icur.fetchall()]
            total = sum(i["unit_price"] * i["quantity"] for i in items)
            result.append({"id": order_id, "created_at": created_at, "items": items, "total": total})
        return result
