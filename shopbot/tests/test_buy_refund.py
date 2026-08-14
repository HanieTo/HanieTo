from types import SimpleNamespace

import pytest

from enums.buy_status import BuyStatus
from enums.language import Language
from models.buy import BuyDTO
from services.buy import BuyService


@pytest.mark.asyncio
async def test_refund_restores_item_stock(monkeypatch):
    """Regression test for a real bug found via live DB testing: refund()
    credited the customer's balance and marked the order REFUNDED, but never
    reset the purchased items' is_sold flag - every refund permanently
    shrank the catalog with no way to resell the item."""
    buy = SimpleNamespace(id=1, status=BuyStatus.PAID)
    user = SimpleNamespace(id=1, telegram_id=555, telegram_username="buyer", consume_records=25.0)
    refund_data = SimpleNamespace(
        telegram_id=555, telegram_username="buyer", total_price=25.0,
        item_ids=[10, 11], subcategory_name="Headphones",
    )
    items = {
        10: SimpleNamespace(id=10, is_sold=True),
        11: SimpleNamespace(id=11, is_sold=True),
    }
    updated_items = []

    async def _fake_get_refund_data_single(buy_id, session):
        return refund_data

    async def _fake_get_by_id(buy_id, session):
        return buy

    async def _fake_update_buy(buy_dto, session):
        return None

    async def _fake_get_by_tgid(tg_id, session):
        return user

    async def _fake_update_user(user_dto, session):
        return None

    async def _fake_get_by_id_map(item_ids, session):
        return {item_id: items[item_id] for item_id in item_ids}

    async def _fake_update_items(item_list, session):
        updated_items.extend(item_list)

    async def _fake_commit(session):
        return None

    async def _fake_notify(*args, **kwargs):
        return None

    monkeypatch.setattr("services.buy.BuyRepository.get_refund_data_single", _fake_get_refund_data_single)
    monkeypatch.setattr("services.buy.BuyRepository.get_by_id", _fake_get_by_id)
    monkeypatch.setattr("services.buy.BuyRepository.update", _fake_update_buy)
    monkeypatch.setattr("services.buy.UserRepository.get_by_tgid", _fake_get_by_tgid)
    monkeypatch.setattr("services.buy.UserRepository.update", _fake_update_user)
    monkeypatch.setattr("services.buy.ItemRepository.get_by_id_map", _fake_get_by_id_map)
    monkeypatch.setattr("services.buy.ItemRepository.update", _fake_update_items)
    monkeypatch.setattr("services.buy.session_commit", _fake_commit)
    monkeypatch.setattr("services.buy.NotificationService.refund", _fake_notify)

    await BuyService.refund(BuyDTO(id=1, shipping_option_id=None), session=None, language=Language.EN)

    assert buy.status == BuyStatus.REFUNDED
    assert user.consume_records == 0.0
    assert len(updated_items) == 2
    assert all(item.is_sold is False for item in updated_items), (
        "refunded items must have is_sold reset to False so they can be resold"
    )
