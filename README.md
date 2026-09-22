# Beats Production Choreography Demo

This repository is a local Docker demo for an event-driven AI production pipeline.

The demo uses RabbitMQ as the event bus, Azurite as local Azure Blob Storage, MySQL as the local database, and six .NET services:

```text
Production.Api
Agents.Storyteller
Agents.Illustrator
Agents.Animator
Agents.Editor
Agents.Reviewer
```

The workflow is choreography-based. Each service subscribes to the event it cares about, produces its own result, writes records to MySQL, writes text artifacts to Azurite, and publishes the next event.

## Requirements

Only Docker is required.

You do not need to install:

```text
.NET SDK
Azure CLI
MySQL client
RabbitMQ
Azurite
```

## Start The Demo

From the repository root:

```bash
docker compose up -d --build
```

Check all containers:

```bash
docker compose ps
```

Expected long-running services:

```text
beats-production-api
beats-storyteller-agent
beats-illustrator-agent
beats-animator-agent
beats-editor-agent
beats-reviewer-agent
beats-rabbitmq
beats-azurite
beats-mysql
```

The `beats-storage-init` and `beats-mysql-init` containers are one-time initialization jobs. It is normal for them to exit with status `0`.

## Test The API

Create a production:

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

If your host does not have `curl`, run the request from a temporary Docker container:

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

The response contains a `productionId`:

```json
{
  "productionId": "00000000-0000-0000-0000-000000000000",
  "eventId": "00000000-0000-0000-0000-000000000000",
  "status": "Requested"
}
```

Use that `productionId` to check status:

```bash
curl -s http://127.0.0.1:5088/productions/{productionId}
```

After a few seconds, the expected final status is:

```text
ReviewPassed
```

Expected event chain:

```text
ProductionRequested
  -> StoryCreated
  -> SceneImagesCreated
  -> SceneAnimationsCreated
  -> FinalVideoCreated
  -> ReviewPassed
```

## Watch Logs

Follow all .NET service logs:

```bash
docker compose logs -f production-api storyteller-agent illustrator-agent animator-agent editor-agent reviewer-agent
```

Follow one service:

```bash
docker compose logs -f storyteller-agent
```

## Check MySQL Records

Open a MySQL shell inside the container:

```bash
docker exec -it beats-mysql mysql -uroot -pbeats-root-dev beats
```

Useful SQL queries:

```sql
SELECT ProductionId, Status, CreatedAt, UpdatedAt
FROM Productions
ORDER BY CreatedAt DESC
LIMIT 5;

SELECT EventType, Producer, CreatedAt
FROM ProductionEvents
WHERE ProductionId = '{productionId}'
ORDER BY CreatedAt;

SELECT AgentRole, ArtifactUri, MediaType, CreatedAt
FROM Artifacts
WHERE ProductionId = '{productionId}'
ORDER BY CreatedAt;

SELECT AgentRole, Status, InputArtifactUri, OutputArtifactUri, StartedAt, CompletedAt
FROM AgentRuns
WHERE ProductionId = '{productionId}'
ORDER BY StartedAt;
```

Or run a one-shot query from your host:

```bash
docker exec beats-mysql mysql -uroot -pbeats-root-dev beats \
  -e "SELECT ProductionId, Status, CreatedAt FROM Productions ORDER BY CreatedAt DESC LIMIT 5;"
```

You can also connect with a GUI database client such as DBeaver, TablePlus, or MySQL Workbench:

```text
Host:     127.0.0.1
Port:     3306
Database: beats
User:     root
Password: beats-root-dev
```

## Check Azurite Artifacts

Each agent writes a new text artifact version:

```text
productions/{productionId}/01-storyteller/manuscript.txt
productions/{productionId}/02-illustrator/manuscript.txt
productions/{productionId}/03-animator/manuscript.txt
productions/{productionId}/04-editor/manuscript.txt
productions/{productionId}/05-reviewer/manuscript.txt
```

List artifacts with Docker only:

```bash
docker compose run --rm --no-deps storage-init \
  sh -c 'az storage blob list \
    --container-name artifacts \
    --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
    --prefix productions/{productionId} \
    --query "[].name" \
    -o tsv'
```

Download the final reviewer artifact:

```bash
docker compose run --rm --no-deps -v "$PWD:/workspace" storage-init \
  sh -c 'az storage blob download \
    --container-name artifacts \
    --name productions/{productionId}/05-reviewer/manuscript.txt \
    --file /workspace/final-manuscript.txt \
    --connection-string "$AZURE_STORAGE_CONNECTION_STRING" \
    --overwrite'
```

You can also inspect Azurite with Azure Storage Explorer.

Use the local emulator connection option, or copy the Azurite connection string from `compose.yaml` and replace the internal `azurite` host with `127.0.0.1`. Open the `artifacts` blob container and browse under:

```text
productions/{productionId}/
```

## RabbitMQ Management UI

Open:

```text
http://localhost:15672
```

Credentials:

```text
username: beats
password: beats-dev
vhost: beats
```

## Service Ports

```text
Production API: http://localhost:5088
RabbitMQ AMQP: localhost:5672
RabbitMQ UI:   http://localhost:15672
Azurite Blob:  http://localhost:10000
Azurite Queue: http://localhost:10001
Azurite Table: http://localhost:10002
MySQL:         localhost:3306
```

## Stop Or Reset

Stop containers but keep persisted data:

```bash
docker compose down
```

Stop containers and remove persisted RabbitMQ, Azurite, and MySQL data:

```bash
docker compose down -v
```

Rebuild after code changes:

```bash
docker compose up -d --build
```

## Project Layout

```text
src/Production.Api          API for starting and checking productions
src/Production.Contracts    Shared event and payload contracts
src/Production.Middleware   Shared event bus, artifact, and persistence adapters
src/Agents.Storyteller      Consumes ProductionRequested, publishes StoryCreated
src/Agents.Illustrator      Consumes StoryCreated, publishes SceneImagesCreated
src/Agents.Animator         Consumes SceneImagesCreated, publishes SceneAnimationsCreated
src/Agents.Editor           Consumes SceneAnimationsCreated, publishes FinalVideoCreated
src/Agents.Reviewer         Consumes FinalVideoCreated, publishes ReviewPassed
sql/                        Database schema
docs/                       Extra local operation notes
```

## Notes

This is a dummy pipeline. The current agents enrich a text artifact so the choreography, pub-sub integration, persistence, and artifact flow can be tested end to end.

Real AI generation can later be added inside each agent without changing the integration shape.

All credentials in `compose.yaml`, `.env.example`, and this README are local demo defaults only. Do not reuse them for cloud resources or shared environments.
