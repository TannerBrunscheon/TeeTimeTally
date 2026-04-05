# TeeTimeTally.Database

Database migrations and seeds for TeeTimeTally.

Run with your preferred Flyway or psql command. Migrations live in the `migrations/` folder and seeds in `seeds/`.

Important:
- `V2__year_end_report_views.sql` adds materialized views used by the reporting service. These views need refreshing after new rounds are finalized; the API triggers refreshes asynchronously and a background refresher periodically refreshes them.
