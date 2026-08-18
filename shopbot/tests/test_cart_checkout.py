from types import SimpleNamespace

import pytest

from callbacks import CartCallback
from enums.buy_status import BuyStatus
from enums.coupon_type import CouponType
from enums.item_type import ItemType
from enums.language import Language
from models.cartItem import CartItemDTO
from models.item import ItemAvailabilityDTO
from services.cart import CartService


def _user(top_up_amount=100.0, consume_records=0.0):
    return SimpleNamespace(id=1, top_up_amount=top_up_amount, consume_records=consume_records)


def _cart_item(quantity=1, item_type=ItemType.PHYSICAL, category_id=1, subcategory_id=1):
    return CartItemDTO(id=1, cart_id=1, item_type=item_type, category_id=category_id,
                       subcategory_id=subcategory_id, quantity=quantity)


def _availability(price=10.0, available_qty=5, item_type=ItemType.PHYSICAL, category_id=1, subcategory_id=1):
    return {
        (item_type, category_id, subcategory_id): ItemAvailabilityDTO(
            item_type=item_type, category_id=category_id, subcategory_id=subcategory_id,
            price=price, description="Item", available_qty=available_qty,
        )
    }


def _patch_common(monkeypatch, user, cart_items, availability_map, state_data=None):
    async def _fake_get_by_tgid(tg_id, session):
        return user

    async def _fake_get_all_by_user_id(user_id, session):
        return cart_items

    async def _fake_get_availability_map(items, session):
        return availability_map

    async def _fake_get_data():
        return state_data or {}

    async def _fake_commit(session):
        return None

    async def _fake_new_buy(*args, **kwargs):
        return None

    monkeypatch.setattr("services.cart.UserRepository.get_by_tgid", _fake_get_by_tgid)
    monkeypatch.setattr("services.cart.CartItemRepository.get_all_by_user_id", _fake_get_all_by_user_id)
    monkeypatch.setattr("services.cart.CartService._get_cart_availability_map", _fake_get_availability_map)
    monkeypatch.setattr("services.cart.session_commit", _fake_commit)
    monkeypatch.setattr("services.cart.NotificationService.new_buy", _fake_new_buy)

    state = SimpleNamespace(get_data=_fake_get_data)
    return state


@pytest.mark.asyncio
async def test_buy_processing_completes_purchase_and_deducts_balance(monkeypatch):
    user = _user(top_up_amount=100.0, consume_records=0.0)
    cart_items = [_cart_item(quantity=2)]
    availability_map = _availability(price=10.0, available_qty=5)
    state = _patch_common(monkeypatch, user, cart_items, availability_map)

    purchased_items = [SimpleNamespace(id=1, is_sold=False), SimpleNamespace(id=2, is_sold=False)]

    async def _fake_get_purchased_items(item_type, category_id, subcategory_id, quantity, session):
        return purchased_items[:quantity]

    async def _fake_update_items(items, session):
        return None

    async def _fake_remove_from_cart(cart_item_id, session):
        return None

    async def _fake_create_buy(buy_dto, session):
        buy_dto.id = 99
        return buy_dto

    async def _fake_create_buy_item(buy_item_dto, session):
        return None

    async def _fake_update_user(user_dto, session):
        return None

    monkeypatch.setattr("services.cart.ItemRepository.get_purchased_items", _fake_get_purchased_items)
    monkeypatch.setattr("services.cart.ItemRepository.update", _fake_update_items)
    monkeypatch.setattr("services.cart.CartItemRepository.remove_from_cart", _fake_remove_from_cart)
    monkeypatch.setattr("services.cart.BuyRepository.create", _fake_create_buy)
    monkeypatch.setattr("services.cart.BuyItemRepository.create_single", _fake_create_buy_item)
    monkeypatch.setattr("services.cart.UserRepository.update", _fake_update_user)

    callback = SimpleNamespace(from_user=SimpleNamespace(id=123))
    callback_data = CartCallback.create(level=4, confirmation=True)

    msg, kb_builder = await CartService.buy_processing(callback, callback_data, state, session=None, language=Language.EN)

    assert user.consume_records == 20.0
    assert all(item.is_sold for item in purchased_items)


@pytest.mark.asyncio
async def test_buy_processing_rejects_when_out_of_stock(monkeypatch):
    user = _user(top_up_amount=100.0)
    cart_items = [_cart_item(quantity=10)]
    availability_map = _availability(price=10.0, available_qty=2)
    state = _patch_common(monkeypatch, user, cart_items, availability_map)

    async def _fake_get_by_ids(ids, session):
        return [SimpleNamespace(id=1, name="Headphones")]

    monkeypatch.setattr("services.cart.SubcategoryRepository.get_by_ids", _fake_get_by_ids)

    create_called = False

    async def _fake_create_buy(buy_dto, session):
        nonlocal create_called
        create_called = True
        return buy_dto

    monkeypatch.setattr("services.cart.BuyRepository.create", _fake_create_buy)

    callback = SimpleNamespace(from_user=SimpleNamespace(id=123))
    callback_data = CartCallback.create(level=4, confirmation=True)

    msg, kb_builder = await CartService.buy_processing(callback, callback_data, state, session=None, language=Language.EN)

    assert create_called is False, "purchase must not be created when cart has out-of-stock items"
    assert "Headphones" in msg


@pytest.mark.asyncio
async def test_buy_processing_rejects_when_insufficient_funds(monkeypatch):
    user = _user(top_up_amount=5.0, consume_records=0.0)
    cart_items = [_cart_item(quantity=2)]
    availability_map = _availability(price=10.0, available_qty=5)
    state = _patch_common(monkeypatch, user, cart_items, availability_map)

    create_called = False

    async def _fake_create_buy(buy_dto, session):
        nonlocal create_called
        create_called = True
        return buy_dto

    monkeypatch.setattr("services.cart.BuyRepository.create", _fake_create_buy)

    callback = SimpleNamespace(from_user=SimpleNamespace(id=123))
    callback_data = CartCallback.create(level=4, confirmation=True)

    msg, kb_builder = await CartService.buy_processing(callback, callback_data, state, session=None, language=Language.EN)

    assert create_called is False, "purchase must not be created without enough balance"


@pytest.mark.asyncio
async def test_buy_processing_applies_percentage_coupon_before_funds_check(monkeypatch):
    """Cart total is 100 (10 qty x 10 price), user only has 60. A 50% coupon
    should bring the total to 50, which the user CAN afford - this must go
    through, not get rejected as insufficient funds."""
    user = _user(top_up_amount=60.0, consume_records=0.0)
    cart_items = [_cart_item(quantity=10)]
    availability_map = _availability(price=10.0, available_qty=20)
    state = _patch_common(monkeypatch, user, cart_items, availability_map, state_data={"coupon_id": 5})

    coupon = SimpleNamespace(id=5, type=CouponType.PERCENTAGE, value=50, usage_limit=10, usage_count=0, is_active=True)

    async def _fake_get_coupon(coupon_id, session):
        return coupon

    async def _fake_update_coupon(coupon_dto, session):
        return None

    purchased_items = [SimpleNamespace(id=i, is_sold=False) for i in range(10)]

    async def _fake_get_purchased_items(item_type, category_id, subcategory_id, quantity, session):
        return purchased_items[:quantity]

    async def _fake_noop(*args, **kwargs):
        return None

    async def _fake_create_buy(buy_dto, session):
        buy_dto.id = 1
        return buy_dto

    monkeypatch.setattr("services.cart.CouponRepository.get_by_id", _fake_get_coupon)
    monkeypatch.setattr("services.cart.CouponRepository.update", _fake_update_coupon)
    monkeypatch.setattr("services.cart.ItemRepository.get_purchased_items", _fake_get_purchased_items)
    monkeypatch.setattr("services.cart.ItemRepository.update", _fake_noop)
    monkeypatch.setattr("services.cart.CartItemRepository.remove_from_cart", _fake_noop)
    monkeypatch.setattr("services.cart.BuyRepository.create", _fake_create_buy)
    monkeypatch.setattr("services.cart.BuyItemRepository.create_single", _fake_noop)
    monkeypatch.setattr("services.cart.UserRepository.update", _fake_noop)

    callback = SimpleNamespace(from_user=SimpleNamespace(id=123))
    callback_data = CartCallback.create(level=4, confirmation=True)

    msg, kb_builder = await CartService.buy_processing(callback, callback_data, state, session=None, language=Language.EN)

    assert user.consume_records == 50.0, f"expected discounted total 50.0, got {user.consume_records}"
