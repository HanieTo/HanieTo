"""Automation hook: when new catalog items are added, auto-draft a
publish-ready post in the OmniCommerce publishing module (the C# API). This
is the walking-skeleton demo of the "workflow automation" pillar - new
product -> draft content, ready for a human to review and publish across
channels. Best-effort: the publishing module being unreachable must never
block adding stock.
"""
import logging

import aiohttp

from config import PUBLISHING_API_URL

log = logging.getLogger("shopbot")


def _draft_caption(description: str, category: str, price: float) -> str:
    """Composes a short announcement text. This is a template placeholder for
    the "AI as a core capability" pillar - swap the body of this function for
    a real LLM call once a provider API key is configured (via an env var /
    secret store, never hardcoded); the call site below doesn't need to
    change."""
    category_part = f" in {category}" if category else ""
    return (
        f"New{category_part}: {description}!\n\n"
        f"Now available for {price:.2f}. Message us to grab yours before it's gone."
    )


async def draft_content_for_items(items) -> None:
    """items: the list of ItemDTO objects just added (see ItemService.add_items)."""
    async with aiohttp.ClientSession(timeout=aiohttp.ClientTimeout(total=5)) as session:
        for item in items:
            payload = {
                "title": item.description,
                "body": _draft_caption(item.description, item.category_name or "", item.price),
            }
            try:
                async with session.post(f"{PUBLISHING_API_URL}/api/content", json=payload) as resp:
                    if resp.status >= 300:
                        log.warning("Publishing draft failed for %r: HTTP %s", item.description, resp.status)
            except Exception as e:
                log.warning("Publishing draft failed for %r: %s", item.description, e)
