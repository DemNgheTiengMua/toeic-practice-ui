#!/usr/bin/env bash
# docs/schema-tests/reset-and-load.sh — fresh DB, then load the schema and the shared test helpers.
set -euo pipefail
S='(localdb)\MSSQLLocalDB'
DB='CompanionSchemaTest'
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$DIR/../.." && pwd)"
ROOT_WIN="$(cygpath -w "$ROOT")"
sqlcmd -S "$S" -I -b -Q "IF DB_ID('$DB') IS NOT NULL BEGIN ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DB]; END; CREATE DATABASE [$DB];"
sqlcmd -S "$S" -d "$DB" -I -b -f 65001 -i "$ROOT_WIN/docs/database-schema.sql"
# lib.sql defines the helpers a domain file calls (ExpectFixtureRows). Without it every test that
# asserts on fixture row counts fails with "could not find stored procedure".
sqlcmd -S "$S" -d "$DB" -I -b -f 65001 -i "$(cygpath -w "$DIR/lib.sql")"
echo "LOADED OK"
