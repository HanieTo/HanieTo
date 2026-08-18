from contextlib import asynccontextmanager
from types import SimpleNamespace

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from sqlalchemy.exc import NoResultFound

import config
from enums.buy_status import BuyStatus
from internal_api.catalog_admin import internal_api_router

API_KEY = "test-internal-key"


@pytest.fixture
def client(monkeypatch):
    monkeypatch.setattr(config, "INTERNAL_API_KEY", API_KEY, raising=False)

    @asynccontextmanager
    async def _fake_get_db_session():
        yield SimpleNamespace()

    monkeypatch.setattr("internal_api.catalog_admin.get_db_session", _fake_get_db_session)
    monkeypatch.setattr("internal_api.catalog_admin.session_commit", _noop_async)

    app = FastAPI()
    app.include_router(internal_api_router)
    return TestClient(app)


async def _noop_async(*args, **kwargs):
    return None


def _headers(key: str | None = API_KEY):
    return {"X-Internal-Api-Key": key} if key is not None else {}


def test_rejects_missing_api_key(client):
    response = client.post("/internal/products", json={
        "item_type": "PHYSICAL", "category": "c", "subcategory": "s",
        "description": "d", "price": 1.0, "quantity": 1,
    }, headers=_headers(None))
    assert response.status_code == 401


def test_rejects_wrong_api_key(client):
    response = client.post("/internal/products", json={
        "item_type": "PHYSICAL", "category": "c", "subcategory": "s",
        "description": "d", "price": 1.0, "quantity": 1,
    }, headers=_headers("wrong-key"))
    assert response.status_code == 401


def test_create_product_rejects_digital_without_matching_codes(client):
    response = client.post("/internal/products", json={
        "item_type": "DIGITAL", "category": "c", "subcategory": "s",
        "description": "d", "price": 1.0, "quantity": 2, "digital_codes": ["only-one"],
    }, headers=_headers())
    assert response.status_code == 400
    assert "digital_codes" in response.json()["detail"]


def test_create_product_rejects_non_positive_price(client):
    response = client.post("/internal/products", json={
        "item_type": "PHYSICAL", "category": "c", "subcategory": "s",
        "description": "d", "price": 0, "quantity": 1,
    }, headers=_headers())
    assert response.status_code == 400
    assert "price" in response.json()["detail"]


def test_create_product_success_adds_items_and_drafts_content(client, monkeypatch):
    added_items = []

    async def _fake_get_or_create_category(name, session):
        return SimpleNamespace(id=1, name=name)

    async def _fake_get_or_create_subcategory(name, session):
        return SimpleNamespace(id=2, name=name)

    async def _fake_add_many(items, session):
        added_items.extend(items)

    drafted = []

    async def _fake_draft(items):
        drafted.extend(items)

    monkeypatch.setattr("internal_api.catalog_admin.CategoryRepository.get_or_create", _fake_get_or_create_category)
    monkeypatch.setattr("internal_api.catalog_admin.SubcategoryRepository.get_or_create", _fake_get_or_create_subcategory)
    monkeypatch.setattr("internal_api.catalog_admin.ItemRepository.add_many", _fake_add_many)
    monkeypatch.setattr("internal_api.catalog_admin.draft_content_for_items", _fake_draft)

    response = client.post("/internal/products", json={
        "item_type": "PHYSICAL", "category": "Shoes", "subcategory": "Sneakers",
        "description": "Cool sneakers", "price": 49.99, "quantity": 3,
    }, headers=_headers())

    assert response.status_code == 200
    assert response.json() == {"added": 3}
    assert len(added_items) == 3
    assert all(item.description == "Cool sneakers" for item in added_items)
    assert len(drafted) == 3


def test_update_order_status_rejects_refund_via_status_endpoint(client):
    response = client.post("/internal/buys/1/status", json={"status": "REFUNDED"}, headers=_headers())
    assert response.status_code == 400


def test_update_order_status_returns_404_for_missing_order(client, monkeypatch):
    async def _fake_get_by_id(buy_id, session):
        raise NoResultFound()

    monkeypatch.setattr("internal_api.catalog_admin.BuyRepository.get_by_id", _fake_get_by_id)

    response = client.post("/internal/buys/999/status", json={"status": "SHIPPED"}, headers=_headers())
    assert response.status_code == 404


def test_update_order_status_success(client, monkeypatch):
    updated = SimpleNamespace(status=None, track_number=None)

    async def _fake_get_by_id(buy_id, session):
        buy = SimpleNamespace(id=buy_id, status=BuyStatus.PAID, track_number=None)
        return buy

    async def _fake_update(buy_dto, session):
        updated.status = buy_dto.status
        updated.track_number = buy_dto.track_number

    monkeypatch.setattr("internal_api.catalog_admin.BuyRepository.get_by_id", _fake_get_by_id)
    monkeypatch.setattr("internal_api.catalog_admin.BuyRepository.update", _fake_update)

    response = client.post("/internal/buys/42/status", json={
        "status": "SHIPPED", "track_number": "TRACK123",
    }, headers=_headers())

    assert response.status_code == 200
    assert response.json() == {"id": 42, "status": "SHIPPED", "message": None}
    assert updated.status == BuyStatus.SHIPPED
    assert updated.track_number == "TRACK123"


def test_refund_order_success(client, monkeypatch):
    async def _fake_refund(buy_dto, session, language):
        assert buy_dto.id == 7
        return "refunded ok"

    monkeypatch.setattr("internal_api.catalog_admin.BuyService.refund", _fake_refund)

    response = client.post("/internal/buys/7/refund", headers=_headers())

    assert response.status_code == 200
    assert response.json() == {"id": 7, "status": "REFUNDED", "message": "refunded ok"}


def test_refund_order_returns_404_when_not_refundable(client, monkeypatch):
    async def _fake_refund(buy_dto, session, language):
        raise NoResultFound()

    monkeypatch.setattr("internal_api.catalog_admin.BuyService.refund", _fake_refund)

    response = client.post("/internal/buys/7/refund", headers=_headers())

    assert response.status_code == 404
