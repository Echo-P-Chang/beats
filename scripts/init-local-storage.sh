#!/usr/bin/env bash
set -euo pipefail

CONTAINER_NAME="${AZURE_STORAGE_CONTAINER_NAME:-artifacts}"
CONNECTION_STRING="${AZURE_STORAGE_CONNECTION_STRING:-DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;}"

az storage container create \
  --name "$CONTAINER_NAME" \
  --connection-string "$CONNECTION_STRING" \
  --public-access off \
  --output none

echo "Container '$CONTAINER_NAME' is ready."
