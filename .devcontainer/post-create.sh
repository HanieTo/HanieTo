#!/usr/bin/env bash
set -e

cd "$(dirname "$0")/.."

echo "==> Restoring .NET packages"
dotnet restore HanieTo.sln

echo "==> Installing shop bot Python dependencies"
python3 -m pip install --quiet -r shopbot/requirements.txt

if [ ! -f shopbot/.env ]; then
  echo "==> Creating shopbot/.env from template"
  cp shopbot/.env.template shopbot/.env
fi

cat <<'EOF'

==================================================================
Codespace is set up. Three secrets still need to be filled in by
hand, inside this Codespace - never paste them into chat:

  1. shopbot/.env
     Fill in TOKEN, POSTGRES_PASSWORD, REDIS_PASSWORD, and a new
     INTERNAL_API_KEY (any strong value you make up - it's a shared
     secret between the bot and the dashboard, not a third-party key).
     Then start the bot stack:
       cd shopbot && docker compose up -d --build

  2. The API's connection to the bot's Postgres catalog:
       cd src/HanieTo.Api
       dotnet user-secrets set ConnectionStrings:ShopCatalog \
         "Host=localhost;Port=5432;Database=aiogram-shop-bot;Username=postgres;Password=<the POSTGRES_PASSWORD from shopbot/.env>"

  3. The API's copy of the same internal API key (must match #1 exactly):
       dotnet user-secrets set ShopBotInternalApi:ApiKey "<the INTERNAL_API_KEY from shopbot/.env>"

Without #1 and #3 matching, Catalog "Add product" and the Orders
ship/deliver/refund buttons will 503 with a "not configured" error -
everything else in the dashboard works without them.

Then run the API:
  dotnet run --project src/HanieTo.Api
==================================================================
EOF
