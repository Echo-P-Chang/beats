# RabbitMQ Local Runtime

Start RabbitMQ:

```bash
docker compose up -d rabbitmq
```

Check health:

```bash
docker compose ps
docker compose logs rabbitmq
```

Management UI:

```text
http://localhost:15672
```

Default local credentials:

```text
username: beats
password: beats-dev
vhost: beats
```

AMQP endpoint for .NET workers:

```text
amqp://beats:beats-dev@localhost:5672/beats
```

Stop RabbitMQ:

```bash
docker compose down
```

Delete RabbitMQ data:

```bash
docker compose down -v
```
