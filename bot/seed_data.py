"""Seed a few sample products so the catalog isn't empty. Run once: python seed_data.py"""
import asyncio

import database as db


SAMPLE_PRODUCTS = [
    ("Wireless Headphones", "Noise-cancelling over-ear, 30h battery", 1_200_000, 5, "Electronics"),
    ("Smart Watch", "Fitness tracking, 7-day battery", 2_500_000, 3, "Electronics"),
    ("Cotton T-Shirt", "100% organic cotton, unisex", 350_000, 20, "Clothing"),
    ("Leather Wallet", "Handmade genuine leather", 800_000, 8, "Clothing"),
]


async def main() -> None:
    await db.init_db()
    if await db.product_count() > 0:
        print("Products already exist, skipping seed.")
        return
    for name, desc, price, stock, category in SAMPLE_PRODUCTS:
        await db.add_product(name, desc, price, stock, category)
    print(f"Seeded {len(SAMPLE_PRODUCTS)} products.")


if __name__ == "__main__":
    asyncio.run(main())
