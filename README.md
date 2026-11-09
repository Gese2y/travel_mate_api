# Abro API

ASP.NET Core 8 + PostgreSQL/PostGIS. Contract: `../travel_mate/docs/api-contract.md`.

## Run locally

```bash
docker compose up -d                       # Postgres + PostGIS on :5432
export DOTNET_ROOT=/snap/dotnet-sdk/256    # only if dotnet was installed via snap
cd src/AbroApi
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://0.0.0.0:5080
```

Development uses OTP code `123456` and applies migrations on startup. Swagger: `/swagger`.
Flutter emulator: `--dart-define=API_BASE_URL=http://10.0.2.2:5080`.

## Migrations

```bash
cd src/AbroApi && dotnet ef migrations add <Name>
```

## Deploy to Render

1. Create a Render **PostgreSQL** in the same region; enable PostGIS (`CREATE EXTENSION postgis;`).
2. Create a **Web Service** from this repo (Docker, root `Dockerfile`).
3. Environment variables:
   - `DATABASE_URL` = the database's **Internal Database URL**
   - `Jwt__Key` = a random secret, at least 32 characters
   - `Database__MigrateOnStartup` = `true`
   - Do **not** set `Otp__FixedCode` (that is dev-only). Plug in a real SMS sender first.
4. Health check path: `/health`.
