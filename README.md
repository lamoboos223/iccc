# Infrastructure Sizing and Performance Certification

The platform sizes infrastructure from performance measurements. A recommendation is labeled measured, calculated, extrapolated, assumption, or insufficient. The capacity engine is a deterministic library. It does not call a language model, a database, or an HTTP API.

## Run the API

```powershell
dotnet run --project src/Iccc.Api
```

Development uses SQLite (`iccc-dev.db`) and a local identity header. The API seeds a labeled demonstration fixture for a .NET payments API, SQL Server, and Redis at a 10K requests-per-second target. Open `http://localhost:5173` after starting the web app, or call the API on `http://localhost:5000` (the launch profile may use another port; check the console).

Development requests can send:

- `X-Dev-User`: user id
- `X-Dev-Roles`: `Engineer` or `Approver`
- `X-Dev-Teams`: team id

The seeded engineer and approver ids are in `src/Iccc.Infrastructure/Demo/DemoCatalog.cs`. Production refuses to start while `Auth:Mode` is `Development`.

## Web

```powershell
cd web
npm install
npm run dev
```

The dev server proxies `/api` to `http://localhost:5080`. Start the API with that URL:

```powershell
$env:ASPNETCORE_URLS="http://localhost:5080"
dotnet run --project src/Iccc.Api
```

## Tests

```powershell
dotnet test Iccc.slnx
```

Capacity and pricing tests do not need a database. On this workstation, Smart App Control blocked newly built unsigned assemblies (`0x800711C7`), so the test host could not load them from the agent session. Run the same command in a developer terminal where local builds are allowed.

## Docker

```powershell
docker compose up --build
```

Compose runs PostgreSQL, the API, Prometheus, and the web app. Set `Database__Provider=Postgres` in the API service. The checked-in development settings use SQLite so the API can run without Docker.

## Product rule

A test that reaches its requested rate is not a success unless the service also meets its SLA. A saturated load generator invalidates the measurement. Targets far beyond the tested rate produce insufficient evidence instead of an instance count. Prices come from the catalog, not from the capacity engine.
