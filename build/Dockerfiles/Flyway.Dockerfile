FROM flyway/flyway:10.15.0

ARG RUN_PSQL_SEED_ON_BUILD=false

COPY TeeTimeTally.Database/migrations /flyway/sql/
# CMD is usually inherited from the base flyway image to run migrate
# or you can override it if needed.