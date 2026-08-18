# HanieTo / OmniCommerce

## What this is

**OmniCommerce**: an AI-powered, multi-channel commerce platform for small/medium
businesses - meant to unify product catalog, inventory, orders, payments, customer
communication, content publishing, and workflow automation in one place, with AI
as a core capability throughout rather than a bolt-on. Development philosophy:
build a real scalable foundation (proper persistence, pluggable interfaces, tests),
not a throwaway demo - this is meant to grow into an enterprise-grade product.

Core pillars, in the order the product vision prioritizes them:
1. Multi-channel publishing (content out to social/messaging/marketplace channels)
2. Centralized product catalog + inventory
3. Orders and payments
4. Unified customer communication + AI-assisted responses
5. Workflow automation engine
6. AI throughout, not bolted on

## Branch note

`main` is still the empty `dotnet new webapi` scaffold. All real work lives on
**`scaffold-webapi`** - check it out before doing anything.

## Repo structure

- `src/HanieTo.Api/` - ASP.NET Core 8 Web API. Owns its own SQLite tables
  (`Content`, `Channel`, `PublishAttempt`) for the publishing module, and has a
  **read-only** EF Core context (`ShopCatalogDbContext`) into the bot's Postgres
  for catalog/orders - never migrates or writes to that database, only queries.
  - `wwwroot/dashboard.html` - the owner-facing dashboard (vanilla JS, no build
    step): Catalog / Orders / Content & Publishing / Channels tabs.
  - `Publishing/Publishers/` - one `IChannelPublisher` per platform (Telegram,
    Discord, Slack, Instagram, Twitter, etc.) resolved via `ChannelPublisherResolver`.
- `shopbot/` - the Telegram shop bot, built on **AiogramShopBot** (Python/Aiogram 3,
  FastAPI, SQLAlchemy async, Postgres, Redis, Docker Compose). This is the source
  of truth for the product catalog and orders.
- `tests/HanieTo.Api.Tests/` - xUnit tests for the API.
- `shopbot/tests/` - pytest tests for the bot.
- `.devcontainer/` - GitHub Codespaces config (`.NET` + Python + Docker-in-Docker),
  so this project can be worked on from any machine, not just this one.

## Hard rule: never accept credentials in chat

The user has repeatedly pasted live API keys/tokens directly into chat (OpenRouter
key, twice; a Telegram bot token, twice - once even as a structured question's
answer, not just a typed message) across multiple projects. Treat this as certain
to happen again, not a one-off.

- If a message contains anything matching a key/token pattern, **do not use it,
  echo it back, or write it to any file** - regardless of how it was asked for.
- Point out it's now compromised (typed into a retained chat log) and needs
  revoking + reissuing at the provider, not just "not using" the old one.
- Redirect to entering it directly in the target surface instead: the dashboard's
  Channels tab (posts straight from the browser to the API), `shopbot/.env`, or
  `dotnet user-secrets set ConnectionStrings:ShopCatalog ...` - never through chat,
  and never as an answer to a clarifying question either.
- Hold this line even under direct pushback ("just use it, I'll revoke after") -
  each recurrence is the same pattern repeating, not a new situation.

## Testing philosophy

The user's explicit standing expectation: verify your own work before reporting it
done, rather than asking the user to manually test via Telegram/the dashboard/curl.
For the bot specifically, prefer live-database verification over mocked unit tests
where feasible - a real bug (a SQLAlchemy `ArgumentError` in a refund query) was
only ever caught by testing against real data; mocks passed cleanly right past it.

## Working from anywhere

GitHub Codespaces is set up (`.devcontainer/`) - create the codespace on the
`scaffold-webapi` branch, not `main`. `postCreateCommand` restores/installs
dependencies and stubs `shopbot/.env` from the template; real secrets still get
typed in by hand inside the codespace afterward (see the rule above).
