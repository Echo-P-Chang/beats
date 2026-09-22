#!/usr/bin/env bash
set -euo pipefail

MYSQL_ROOT_PASSWORD="${MYSQL_ROOT_PASSWORD:-beats-root-dev}"
MYSQL_DATABASE="${MYSQL_DATABASE:-beats}"

echo "Waiting for MySQL..."
until docker exec beats-mysql mysqladmin ping -h 127.0.0.1 -uroot -p"$MYSQL_ROOT_PASSWORD" --silent; do
  sleep 2
done

echo "Creating database and runtime users..."
docker exec -i beats-mysql mysql -uroot -p"$MYSQL_ROOT_PASSWORD" <<SQL
CREATE DATABASE IF NOT EXISTS \`${MYSQL_DATABASE}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;

CREATE USER IF NOT EXISTS 'beats_api'@'%' IDENTIFIED BY 'beats-api-dev';
CREATE USER IF NOT EXISTS 'beats_storyteller'@'%' IDENTIFIED BY 'beats-storyteller-dev';
CREATE USER IF NOT EXISTS 'beats_illustrator'@'%' IDENTIFIED BY 'beats-illustrator-dev';
CREATE USER IF NOT EXISTS 'beats_animator'@'%' IDENTIFIED BY 'beats-animator-dev';
CREATE USER IF NOT EXISTS 'beats_editor'@'%' IDENTIFIED BY 'beats-editor-dev';
CREATE USER IF NOT EXISTS 'beats_reviewer'@'%' IDENTIFIED BY 'beats-reviewer-dev';

GRANT SELECT, INSERT, UPDATE, DELETE ON \`${MYSQL_DATABASE}\`.* TO 'beats_api'@'%';
GRANT SELECT, INSERT, UPDATE, DELETE ON \`${MYSQL_DATABASE}\`.* TO 'beats_storyteller'@'%';
GRANT SELECT, INSERT, UPDATE, DELETE ON \`${MYSQL_DATABASE}\`.* TO 'beats_illustrator'@'%';
GRANT SELECT, INSERT, UPDATE, DELETE ON \`${MYSQL_DATABASE}\`.* TO 'beats_animator'@'%';
GRANT SELECT, INSERT, UPDATE, DELETE ON \`${MYSQL_DATABASE}\`.* TO 'beats_editor'@'%';
GRANT SELECT, INSERT, UPDATE, DELETE ON \`${MYSQL_DATABASE}\`.* TO 'beats_reviewer'@'%';

FLUSH PRIVILEGES;
SQL

echo "Applying schema..."
docker exec -i beats-mysql mysql -uroot -p"$MYSQL_ROOT_PASSWORD" "$MYSQL_DATABASE" < sql/mysql-schema.sql

echo "MySQL database '$MYSQL_DATABASE' is ready."
