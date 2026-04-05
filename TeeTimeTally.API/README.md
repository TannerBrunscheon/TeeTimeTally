# TeeTimeTally.API

Backend API for TeeTimeTally.

Tech: .NET 8, FastEndpoints, Dapper, Npgsql (Postgres).

Running locally:

```bash
cd TeeTimeTally.API
dotnet build
dotnet run
```

Notes:
- Reports are implemented in `ReportService.cs` and exposed through endpoints under `Endpoints/Reports`.
- Materialized views for report performance are in `TeeTimeTally.Database/migrations` and a background refresher exists in `Services/MaterializedViewRefresher.cs`.
- Auth uses Auth0 and scope-based policies located in `Identity/`.
