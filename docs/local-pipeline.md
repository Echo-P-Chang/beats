# Local Pipeline Smoke Test

## Docker Quick Start

Start the complete demo stack:

```bash
docker compose up -d --build
```

This starts:

```text
production-api
storyteller-agent
illustrator-agent
animator-agent
editor-agent
reviewer-agent
rabbitmq
azurite
mysql
storage-init
mysql-init
```

Trigger a production from your host:

```bash
curl -s -X POST http://127.0.0.1:5088/productions \
  -H 'Content-Type: application/json' \
  -d '{
    "prompt": "請說一則800字的故事，並產出影片。",
    "style": "水彩繪本風",
    "targetWordCount": 800,
    "durationSeconds": 90
  }'
```

If you want to use Docker only, run the same request from a temporary curl container:

```bash
docker run --rm --network beats_default curlimages/curl:8.10.1 \
  -s -X POST http://production-api:8080/productions \
  -H 'Content-Type: application/json' \
  -d '{
    "prompt": "請說一則800字的故事，並產出影片。",
    "style": "水彩繪本風",
    "targetWordCount": 800,
    "durationSeconds": 90
  }'
```

Check container status:

```bash
docker compose ps
```

Tail logs:

```bash
docker compose logs -f production-api storyteller-agent illustrator-agent animator-agent editor-agent reviewer-agent
```

Stop the demo:

```bash
docker compose down
```

Remove persisted RabbitMQ, Azurite, and MySQL data:

```bash
docker compose down -v
```

## Manual Local Run

If you want to run the .NET projects directly on your host, start only the local infrastructure:

```bash
docker compose up -d rabbitmq azurite mysql
./scripts/init-local-storage.sh
./scripts/init-local-database.sh
```

Then run the API:

```bash
dotnet run --project src/Production.Api/Beats.Production.Api.csproj --urls http://127.0.0.1:5088
```

Run each agent in a separate terminal:

```bash
dotnet run --project src/Agents.Storyteller/Beats.Agents.Storyteller.csproj
dotnet run --project src/Agents.Illustrator/Beats.Agents.Illustrator.csproj
dotnet run --project src/Agents.Animator/Beats.Agents.Animator.csproj
dotnet run --project src/Agents.Editor/Beats.Agents.Editor.csproj
dotnet run --project src/Agents.Reviewer/Beats.Agents.Reviewer.csproj
```

Trigger a production:

```bash
curl -s -X POST http://127.0.0.1:5088/productions \
  -H 'Content-Type: application/json' \
  -d '{
    "prompt": "請說一則800字的故事，並產出影片。",
    "style": "水彩繪本風",
    "targetWordCount": 800,
    "durationSeconds": 90
  }'
```

## Expected Event Chain

```text
ProductionRequested
  -> StoryCreated
  -> SceneImagesCreated
  -> SceneAnimationsCreated
  -> FinalVideoCreated
  -> ReviewPassed
```

Each agent writes a new text artifact version to local Azurite blob storage:

```text
productions/{productionId}/01-storyteller/manuscript.txt
productions/{productionId}/02-illustrator/manuscript.txt
productions/{productionId}/03-animator/manuscript.txt
productions/{productionId}/04-editor/manuscript.txt
productions/{productionId}/05-reviewer/manuscript.txt
```

The pipeline also writes database records to local MySQL:

```text
Productions
ProductionEvents
Artifacts
AgentRuns
```

RabbitMQ management UI:

```text
http://localhost:15672
username: beats
password: beats-dev
vhost: beats
```

Docker Compose provides local development settings through service environment variables.

When running the .NET projects directly on your host, local application secrets are loaded from `.env.local`.

Azurite and MySQL local defaults are also documented in `.env.example`.

To use real Azure Blob Storage instead, set `AZURE_STORAGE_CONNECTION_STRING` or update the `AzureStorage` section in each appsettings file.

To use Azure SQL instead, set `Database:Provider` to `SqlServer` and point the relevant `BEATS_DB_*_CONNECTION_STRING` value at SQL Server.
