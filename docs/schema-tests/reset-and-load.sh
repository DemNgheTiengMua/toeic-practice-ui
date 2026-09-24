#!/usr/bin/env bash
# docs/schema-tests/reset-and-load.sh — fresh DB, then load the schema.
set -euo pipefail
S='(localdb)\MSSQLLocalDB'
DB='CompanionSchemaTest'
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
sqlcmd -S "$S" -I -b -Q "IF DB_ID('$DB') IS NOT NULL BEGIN ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DB]; END; CREATE DATABASE [$DB];"
sqlcmd -S "$S" -d "$DB" -I -b -i "$ROOT/docs/database-schema.sql"
echo "LOADED OK"
