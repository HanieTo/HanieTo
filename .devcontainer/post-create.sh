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
Codespace is set up. Two secrets still need to be filled in by
hand, inside this Codespace - never paste them into chat:

  1. shopbot/.env
     Fill in TOKEN, POSTGRES_PASSWORD, REDIS_PASSWORD, etc.
     Then start the bot stack:
       cd shopbot && docker compose up -d --build

  2. The API's connection to the bot's Postgres catalog:
       cd src/HanieTo.Api
       dotnet user-secrets set ConnectionStrings:ShopCatalog \
         "Host=localhost;Port=5432;Database=aiogram-shop-bot;Username=postgres;Password=<the POSTGRES_PASSWORD from shopbot/.env>"

Then run the API:
  dotnet run --project src/HanieTo.Api
==================================================================
EOF
