# TeeTimeTally

Monorepo for TeeTimeTally application. Contains backend API, database migrations/seeds, shared DTOs, and a Vue 3 + TypeScript client.

Quick overview:
- `TeeTimeTally.API/` - .NET 8 Web API (FastEndpoints + Dapper + Npgsql)
- `TeeTimeTally.Database/` - SQL migrations and seeds (Flyway style)
- `TeeTimeTally.Shared/` - Shared DTOs and models used by API and client codegen/typings
- `TeeTimeTally.UI/` - Frontend app (Vue 3 + TypeScript + Pinia + Vite). Client lives in `TeeTimeTally.UI/Client`.
- `build/` - docker-compose / Dockerfiles and deployment helpers

Developer notes:
- Backend: `dotnet build` / `dotnet run` from the solution root or within the `TeeTimeTally.API` folder.
- rontend: run the client under WSL (recommended) to avoid devtools/plugin issues on some Windows hosts. From the repo root

```bash
# from WSL (recommended)
cd TeeTimeTally.UI/Client
npm ci
npm run build
```

- Materialized views: The API uses materialized views for year-end reports for speed. See `TeeTimeTally.Database/migrations/V2__year_end_report_views.sql` and `TeeTimeTally.API/Services/MaterializedViewRefresher.cs` for refresh strategy.

If you need a project-specific README, see the subfolder READMEs.