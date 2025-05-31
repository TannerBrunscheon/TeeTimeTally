FROM flyway/flyway:10.15.0
COPY TeeTimeTally.Database/migrations /flyway/sql/
# CMD is usually inherited from the base flyway image to run migrate
# or you can override it if needed.