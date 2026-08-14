from types import SimpleNamespace

import pytest

from callbacks import AllCategoriesCallback
from enums.item_type import ItemType
from enums.language import Language
from models.cartItem import CartItemDTO
from services.cart import CartService


def _callback_data(quantity: int) -> AllCategoriesCallback:
    return AllCategoriesCallback.create(
        level=5,
        item_type=ItemType.PHYSICAL,
        category_id=1,
        subcategory_id=1,
        quantity=quantity,
    )


@pytest.mark.asyncio
async def test_add_to_cart_caps_first_time_quantity_at_available_stock(monkeypatch):
    """Regression test: stock=2 but a customer could add 3 to a fresh cart line
    (the quantity picker offered buttons up to 10 regardless of stock, and
    add_to_cart only capped the quantity when *incrementing* an existing cart
    line, not on the first add)."""
    created: list[CartItemDTO] = []

    async def _fake_get_by_tgid(tg_id, session):
        return SimpleNamespace(id=1)

    async def _fake_get_or_create(user_id, session):
        return SimpleNamespace(id=1)

    async def _fake_get_available_qty(item_type, category_id, subcategory_id, session):
        return 2

    async def _fake_get_current_cart_content(cart_item, cart, session):
        return None

    async def _fake_create(cart_item, session):
        created.append(cart_item)

    async def _fake_commit(session):
        return None

    monkeypatch.setattr("services.cart.UserRepository.get_by_tgid", _fake_get_by_tgid)
    monkeypatch.setattr("services.cart.CartRepository.get_or_create", _fake_get_or_create)
    monkeypatch.setattr("services.cart.ItemRepository.get_available_qty", _fake_get_available_qty)
    monkeypatch.setattr("services.cart.CartItemRepository.get_current_cart_content", _fake_get_current_cart_content)
    monkeypatch.setattr("services.cart.CartItemRepository.create", _fake_create)
    monkeypatch.setattr("services.cart.session_commit", _fake_commit)
    monkeypatch.setattr("services.cart.get_bot_photo_id", lambda: "photo-id")

    callback = SimpleNamespace(from_user=SimpleNamespace(id=123))
    await CartService.add_to_cart(callback, _callback_data(quantity=3), session=None, language=Language.EN)

    assert len(created) == 1
    assert created[0].quantity == 2, (
        f"expected quantity capped at available stock (2), got {created[0].quantity} - overselling bug"
    )


@pytest.mark.asyncio
async def test_add_to_cart_caps_incremented_quantity_at_available_stock(monkeypatch):
    """Same cap, but for the pre-existing branch: adding more of an item
    already in the cart must not push it past available stock either."""
    existing = CartItemDTO(id=1, cart_id=1, item_type=ItemType.PHYSICAL, category_id=1, subcategory_id=1, quantity=1)
    updated: list[CartItemDTO] = []

    async def _fake_get_by_tgid(tg_id, session):
        return SimpleNamespace(id=1)

    async def _fake_get_or_create(user_id, session):
        return SimpleNamespace(id=1)

    async def _fake_get_available_qty(item_type, category_id, subcategory_id, session):
        return 2

    async def _fake_get_current_cart_content(cart_item, cart, session):
        return existing

    async def _fake_update(cart_item, session):
        updated.append(cart_item)

    async def _fake_commit(session):
        return None

    monkeypatch.setattr("services.cart.UserRepository.get_by_tgid", _fake_get_by_tgid)
    monkeypatch.setattr("services.cart.CartRepository.get_or_create", _fake_get_or_create)
    monkeypatch.setattr("services.cart.ItemRepository.get_available_qty", _fake_get_available_qty)
    monkeypatch.setattr("services.cart.CartItemRepository.get_current_cart_content", _fake_get_current_cart_content)
    monkeypatch.setattr("services.cart.CartItemRepository.update", _fake_update)
    monkeypatch.setattr("services.cart.session_commit", _fake_commit)
    monkeypatch.setattr("services.cart.get_bot_photo_id", lambda: "photo-id")

    callback = SimpleNamespace(from_user=SimpleNamespace(id=123))
    await CartService.add_to_cart(callback, _callback_data(quantity=3), session=None, language=Language.EN)

    assert len(updated) == 1
    assert updated[0].quantity == 2
