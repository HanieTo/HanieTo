from types import SimpleNamespace

import pytest

from callbacks import AllCategoriesCallback
from enums.item_type import ItemType
from enums.language import Language
from services.subcategory import SubcategoryService


@pytest.mark.asyncio
async def test_select_quantity_buttons_never_exceed_available_stock(monkeypatch):
    """Regression test: stock=2 but the quantity picker always offered buttons
    1-10 regardless of actual stock, letting a customer pick more than
    existed."""

    async def _fake_get_single(item_type, category_id, subcategory_id, session):
        return SimpleNamespace(price=10.0, description="Wireless Headphones",
                               item_type=item_type, category_id=category_id, subcategory_id=subcategory_id)

    async def _fake_get_by_id_subcategory(subcategory_id, session):
        return SimpleNamespace(id=subcategory_id, name="Headphones", media_id="media-id")

    async def _fake_get_by_id_category(category_id, session):
        return SimpleNamespace(id=category_id, name="Electronics")

    async def _fake_get_available_qty(item_type, category_id, subcategory_id, session):
        return 2

    monkeypatch.setattr("services.subcategory.ItemRepository.get_single", _fake_get_single)
    monkeypatch.setattr("services.subcategory.SubcategoryRepository.get_by_id", _fake_get_by_id_subcategory)
    monkeypatch.setattr("services.subcategory.CategoryRepository.get_by_id", _fake_get_by_id_category)
    monkeypatch.setattr("services.subcategory.ItemRepository.get_available_qty", _fake_get_available_qty)
    monkeypatch.setattr("services.subcategory.MediaService.convert_to_media",
                        lambda media_id, caption: SimpleNamespace(caption=caption))

    callback_data = AllCategoriesCallback.create(
        level=3, item_type=ItemType.PHYSICAL, category_id=1, subcategory_id=1,
    )

    _, kb_builder = await SubcategoryService.get_select_quantity_buttons(callback_data, session=None, language=Language.EN)

    quantity_buttons = [
        button for row in kb_builder.as_markup().inline_keyboard for button in row
        if button.text.isdigit()
    ]
    offered_quantities = sorted(int(b.text) for b in quantity_buttons)

    assert offered_quantities == [1, 2], (
        f"expected buttons capped at available stock (2), got {offered_quantities} - overselling bug"
    )
