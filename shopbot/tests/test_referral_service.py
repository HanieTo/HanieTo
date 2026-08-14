from types import SimpleNamespace

import pytest

from models.payment import ProcessingPaymentDTO
from models.user import UserDTO
from services.referral import ReferralService


def _payment(fiat_amount=100.0):
    return SimpleNamespace(fiatAmount=fiat_amount)


def _user(user_id=1, referred_by=None, referral_code=None, top_up_amount=0.0):
    return UserDTO(id=user_id, referred_by_user_id=referred_by,
                   referral_code=referral_code, top_up_amount=top_up_amount)


@pytest.mark.asyncio
async def test_apply_referral_logic_never_exceeds_combined_bonus_cap(monkeypatch):
    """referral_bonus + referrer_bonus must never exceed TOTAL_BONUS_CAP_PERCENT
    of the referred user's deposit sum, even when both raw percentages would
    individually be under the cap but sum over it."""
    import config
    config.REFERRAL_BONUS_PERCENT = 5
    config.REFERRER_BONUS_PERCENT = 3
    config.REFERRAL_BONUS_DEPOSIT_LIMIT = 3
    config.REFERRER_BONUS_DEPOSIT_LIMIT = 5
    config.TOTAL_BONUS_CAP_PERCENT = 7

    referred_user = _user(user_id=1, referred_by=2)
    referrer_user = _user(user_id=2)

    async def _fake_deposits_qty(user_id, session):
        return 0

    async def _fake_deposits_sum(user_id, session):
        return 1000.0

    async def _fake_get_user_entity(user_id, session):
        return referrer_user

    async def _fake_update(user_dto, session):
        return None

    async def _fake_create(referral_bonus_dto, session):
        return referral_bonus_dto

    monkeypatch.setattr("services.referral.DepositRepository.get_deposits_qty_by_user_id", _fake_deposits_qty)
    monkeypatch.setattr("services.referral.DepositRepository.get_sum", _fake_deposits_sum)
    monkeypatch.setattr("services.referral.UserRepository.get_user_entity", _fake_get_user_entity)
    monkeypatch.setattr("services.referral.UserRepository.update", _fake_update)
    monkeypatch.setattr("services.referral.ReferralRepository.create", _fake_create)

    result = await ReferralService.apply_referral_logic(_payment(100.0), referred_user, session=None)

    total_bonus_cap = 1000.0 * (7 / 100)  # 70.0
    assert result.applied_referral_bonus + result.applied_referrer_bonus <= total_bonus_cap + 1e-9
    # raw referral = 5.0, raw referrer = 3.0, both well under the 70.0 cap here
    assert result.applied_referral_bonus == pytest.approx(5.0)
    assert result.applied_referrer_bonus == pytest.approx(3.0)


@pytest.mark.asyncio
async def test_apply_referral_logic_caps_when_raw_bonuses_exceed_small_deposit_sum(monkeypatch):
    """With a tiny deposit sum, the cap is small - the combined bonus must be
    clamped down to it rather than paying out the full raw percentages."""
    import config
    config.REFERRAL_BONUS_PERCENT = 5
    config.REFERRER_BONUS_PERCENT = 3
    config.REFERRAL_BONUS_DEPOSIT_LIMIT = 3
    config.REFERRER_BONUS_DEPOSIT_LIMIT = 5
    config.TOTAL_BONUS_CAP_PERCENT = 7

    referred_user = _user(user_id=1, referred_by=2)
    referrer_user = _user(user_id=2)

    async def _fake_deposits_qty(user_id, session):
        return 0

    async def _fake_deposits_sum(user_id, session):
        return 10.0  # cap = 0.7

    async def _fake_get_user_entity(user_id, session):
        return referrer_user

    async def _fake_update(user_dto, session):
        return None

    async def _fake_create(referral_bonus_dto, session):
        return referral_bonus_dto

    monkeypatch.setattr("services.referral.DepositRepository.get_deposits_qty_by_user_id", _fake_deposits_qty)
    monkeypatch.setattr("services.referral.DepositRepository.get_sum", _fake_deposits_sum)
    monkeypatch.setattr("services.referral.UserRepository.get_user_entity", _fake_get_user_entity)
    monkeypatch.setattr("services.referral.UserRepository.update", _fake_update)
    monkeypatch.setattr("services.referral.ReferralRepository.create", _fake_create)

    result = await ReferralService.apply_referral_logic(_payment(100.0), referred_user, session=None)

    total_bonus_cap = 10.0 * (7 / 100)  # 0.7
    total_paid = result.applied_referral_bonus + result.applied_referrer_bonus
    assert total_paid <= total_bonus_cap + 1e-9, (
        f"combined bonus {total_paid} exceeded cap {total_bonus_cap}"
    )
