# HanieTo

An ASP.NET Core Web API project.

## Structure

- `src/HanieTo.Api` — the Web API project (controllers, `Program.cs`, configuration)
- `tests/HanieTo.Api.Tests` — xUnit test project
- `HanieTo.sln` — solution file tying both projects together

## Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Getting started

```bash
# restore & build
dotnet build

# run the API
dotnet run --project src/HanieTo.Api

# run tests
dotnet test
```

Once running, try the sample endpoint (check the console output for the actual port):

```bash
curl http://localhost:5299/weatherforecast
```

## IDE

- **VS Code** + [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) extension
- **JetBrains Rider**
- **Visual Studio** (Windows)

## Working from anywhere (GitHub Codespaces)

The repo includes a `.devcontainer/` config, so you can work on this project from a browser tab (or any machine) without installing anything locally:

1. On GitHub, open this repo → **Code** → **Codespaces** → **Create codespace on main**.
2. Wait for `postCreateCommand` to finish (.NET restore + Python deps + a `shopbot/.env` stub).
3. Fill in two secrets **inside the Codespace terminal/editor** - never in chat, same rule as local dev:
   - `shopbot/.env` — bot token, Postgres/Redis passwords, etc. (copied from `.env.template`)
   - `dotnet user-secrets set ConnectionStrings:ShopCatalog "Host=localhost;Port=5432;Database=aiogram-shop-bot;Username=postgres;Password=<...>"` (run from `src/HanieTo.Api`)
4. `cd shopbot && docker compose up -d --build` to start the bot stack, then `dotnet run --project src/HanieTo.Api` for the dashboard.

Forwarded ports: `5169` (dashboard/API, opens automatically), `5000` (bot webapp), `5432`/`6379` (Postgres/Redis, forwarded silently for direct DB access if you need it).
