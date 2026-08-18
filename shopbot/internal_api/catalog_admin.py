"""Internal write API for the catalog and orders.

The bot's Postgres database is the single source of truth for products and
orders (see db.py / models/). The OmniCommerce dashboard (the C# API) only
has a *read-only* connection into that database - see
src/HanieTo.Api/Data/ShopCatalogDbContext.cs - so it re-implements none of
the bot's persistence rules (item-type validation, get-or-create categories,
refund bookkeeping, etc.). Instead the dashboard calls these endpoints, which
reuse the bot's own repositories/services, exactly like the Telegram admin
flow does.

Auth: every request must carry X-Internal-Api-Key matching config.INTERNAL_API_KEY.
This is not exposed publicly - only the dashboard's backend calls it, over the
docker network / host.docker.internal - so a static shared secret is enough.
"""
from fastapi import APIRouter, Depends, Header, HTTPException, status
from pydantic import BaseModel
from sqlalchemy import select, update
from sqlalchemy.exc import NoResultFound

import config
from db import get_db_session, session_commit, session_execute
from enums.buy_status import BuyStatus
from enums.item_type import ItemType
from enums.language import Language
from models.buy import BuyDTO
from models.category import Category
from models.item import Item, ItemDTO
from models.subcategory import Subcategory
from repositories.buy import BuyRepository
from repositories.category import CategoryRepository
from repositories.item import ItemRepository
from repositories.subcategory import SubcategoryRepository
from services.buy import BuyService
from services.publishing_hook import draft_content_for_items

internal_api_router = APIRouter(prefix="/internal")


def require_internal_api_key(x_internal_api_key: str | None = Header(default=None)) -> None:
    if not config.INTERNAL_API_KEY or x_internal_api_key != config.INTERNAL_API_KEY:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid or missing internal API key")


class CreateProductRequest(BaseModel):
    item_type: ItemType
    category: str
    subcategory: str
    description: str
    price: float
    quantity: int = 1
    # One entry per unit for DIGITAL products (e.g. license keys); must match
    # quantity exactly. Not used for PHYSICAL products - stock there is just
    # a count of identical units.
    digital_codes: list[str] | None = None


class CreateProductResponse(BaseModel):
    added: int


@internal_api_router.post(
    "/products",
    response_model=CreateProductResponse,
    dependencies=[Depends(require_internal_api_key)],
)
async def create_product(payload: CreateProductRequest) -> CreateProductResponse:
    if not payload.description.strip():
        raise HTTPException(status_code=400, detail="description is required")
    if payload.price <= 0:
        raise HTTPException(status_code=400, detail="price must be positive")
    if payload.quantity < 1:
        raise HTTPException(status_code=400, detail="quantity must be at least 1")
    if payload.item_type == ItemType.DIGITAL:
        if not payload.digital_codes or len(payload.digital_codes) != payload.quantity:
            raise HTTPException(
                status_code=400,
                detail="digital products need exactly one digital_codes entry per unit of quantity",
            )

    async with get_db_session() as session:
        category = await CategoryRepository.get_or_create(payload.category, session)
        subcategory = await SubcategoryRepository.get_or_create(payload.subcategory, session)
        items = [
            ItemDTO(
                item_type=payload.item_type,
                category_id=category.id,
                category_name=category.name,
                subcategory_id=subcategory.id,
                subcategory_name=subcategory.name,
                description=payload.description,
                price=payload.price,
                private_data=payload.digital_codes[i] if payload.item_type == ItemType.DIGITAL else None,
            )
            for i in range(payload.quantity)
        ]
        await ItemRepository.add_many(items, session)
        await session_commit(session)
        await draft_content_for_items(items)
        return CreateProductResponse(added=len(items))


class UpdateProductPriceRequest(BaseModel):
    category: str
    subcategory: str
    description: str
    new_price: float


class UpdateProductPriceResponse(BaseModel):
    updated: int


@internal_api_router.patch(
    "/products/price",
    response_model=UpdateProductPriceResponse,
    dependencies=[Depends(require_internal_api_key)],
)
async def update_product_price(payload: UpdateProductPriceRequest) -> UpdateProductPriceResponse:
    if payload.new_price <= 0:
        raise HTTPException(status_code=400, detail="new_price must be positive")

    async with get_db_session() as session:
        matching_ids_stmt = (
            select(Item.id)
            .join(Category, Item.category_id == Category.id)
            .join(Subcategory, Item.subcategory_id == Subcategory.id)
            .where(
                Category.name == payload.category,
                Subcategory.name == payload.subcategory,
                Item.description == payload.description,
                Item.is_sold == False,  # noqa: E712 - SQLAlchemy column comparison
            )
        )
        matching_ids = (await session_execute(matching_ids_stmt, session)).scalars().all()
        if not matching_ids:
            raise HTTPException(status_code=404, detail="No matching in-stock product found")

        await session_execute(
            update(Item).where(Item.id.in_(matching_ids)).values(price=payload.new_price),
            session,
        )
        await session_commit(session)
        return UpdateProductPriceResponse(updated=len(matching_ids))


class UpdateOrderStatusRequest(BaseModel):
    status: BuyStatus
    track_number: str | None = None


class OrderActionResponse(BaseModel):
    id: int
    status: str
    message: str | None = None


@internal_api_router.post(
    "/buys/{buy_id}/status",
    response_model=OrderActionResponse,
    dependencies=[Depends(require_internal_api_key)],
)
async def update_order_status(buy_id: int, payload: UpdateOrderStatusRequest) -> OrderActionResponse:
    if payload.status == BuyStatus.REFUNDED:
        raise HTTPException(status_code=400, detail="Use POST /internal/buys/{id}/refund to refund an order")

    async with get_db_session() as session:
        try:
            buy = await BuyRepository.get_by_id(buy_id, session)
        except NoResultFound:
            raise HTTPException(status_code=404, detail="Order not found")

        buy.status = payload.status
        if payload.track_number is not None:
            buy.track_number = payload.track_number
        await BuyRepository.update(buy, session)
        await session_commit(session)
        return OrderActionResponse(id=buy_id, status=payload.status.value)


@internal_api_router.post(
    "/buys/{buy_id}/refund",
    response_model=OrderActionResponse,
    dependencies=[Depends(require_internal_api_key)],
)
async def refund_order(buy_id: int) -> OrderActionResponse:
    async with get_db_session() as session:
        try:
            message = await BuyService.refund(BuyDTO(id=buy_id, shipping_option_id=None), session, Language.EN)
        except NoResultFound:
            raise HTTPException(status_code=404, detail="Order not found, or already refunded")
        return OrderActionResponse(id=buy_id, status=BuyStatus.REFUNDED.value, message=message)
