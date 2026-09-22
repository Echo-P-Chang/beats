# Database

The default local database is MySQL 8.4 running in Docker.

Start and initialize it:

```bash
docker compose up -d mysql
./scripts/init-local-database.sh
```

The initialization script creates the `beats` database, applies `sql/mysql-schema.sql`, and creates one runtime user per service:

```text
beats_api
beats_storyteller
beats_illustrator
beats_animator
beats_editor
beats_reviewer
```

Records written by the choreography pipeline:

```text
Productions
ProductionEvents
Artifacts
AgentRuns
```

Local connection strings are stored in `.env.local`, which is intentionally ignored by git. Shared defaults live in `.env.example`.

Azure SQL can still be used later by changing each appsettings `Database.Provider` to `SqlServer` and supplying SQL Server connection strings through the configured `BEATS_DB_*_CONNECTION_STRING` environment variables.
