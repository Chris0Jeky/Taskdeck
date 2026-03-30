#!/usr/bin/env bash
# Export the OpenAPI spec from a running Taskdeck API instance.
# Usage: ./scripts/export-openapi-spec.sh [output-path] [api-url]
#
# Defaults:
#   output-path: artifacts/openapi/taskdeck-api.json
#   api-url:     http://localhost:5000

set -euo pipefail

OUTPUT_PATH="${1:-artifacts/openapi/taskdeck-api.json}"
API_URL="${2:-http://localhost:5000}"
SWAGGER_URL="${API_URL}/swagger/v1/swagger.json"

mkdir -p "$(dirname "$OUTPUT_PATH")"

echo "Fetching OpenAPI spec from ${SWAGGER_URL}..."
curl -sf "$SWAGGER_URL" -o "$OUTPUT_PATH"

echo "OpenAPI spec saved to ${OUTPUT_PATH}"
echo ""
echo "To generate static HTML docs with Redoc:"
echo "  npx @redocly/cli build-docs ${OUTPUT_PATH} --output docs-output/index.html"
