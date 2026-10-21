#!/usr/bin/env bash
# Administrative CLI tool to generate and ingest standardized exams from a blueprint JSON (Issue #88).
# Usage: ./scripts/generate-exam.sh <path-to-blueprint.json> [admin-id]

set -euo pipefail

if [ "$#" -lt 1 ]; then
    echo "Usage: ./scripts/generate-exam.sh <path-to-blueprint.json> [admin-id]"
    exit 1
fi

BLUEPRINT_PATH="$1"
ADMIN_ID="${2:-1}"

if [ ! -f "$BLUEPRINT_PATH" ]; then
    echo "Error: Blueprint file not found: $BLUEPRINT_PATH"
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

dotnet run --project src/WebAPI -- generate-exam "$(realpath "$BLUEPRINT_PATH")" --admin-id "$ADMIN_ID"
