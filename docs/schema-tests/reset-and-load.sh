#!/usr/bin/env bash
# docs/schema-tests/reset-and-load.sh — fresh DB, then load the schema and the shared test helpers.
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$DIR/../.." && pwd)"
. "$DIR/sqlcmd-env.sh"   # SQL_SERVER, SQL_DB, SQLCMD_ARGS, sqlcmd_path
sqlcmd -S "$SQL_SERVER" "${SQLCMD_ARGS[@]}" -I -b -Q "IF DB_ID('$SQL_DB') IS NOT NULL BEGIN ALTER DATABASE [$SQL_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$SQL_DB]; END; CREATE DATABASE [$SQL_DB];"
sqlcmd -S "$SQL_SERVER" "${SQLCMD_ARGS[@]}" -d "$SQL_DB" -I -b -f 65001 -i "$(sqlcmd_path "$ROOT/docs/database-schema.sql")"
# Pending schema deltas: a ticket that needs a table while #250 owns database-schema.sql writes its SQL
# to docs/schema-changes-<n>.sql instead of editing that file. Every one of them is guarded (CREATE TABLE
# IF NOT EXISTS / UPDATE ... WHERE), so applying the set twice is a no-op and folding one into the schema
# later needs only deleting the file. Sorted, so two deltas that touch the same object apply in a stable
# order rather than whichever the glob happens to return.
for delta in "$ROOT"/docs/schema-changes-*.sql; do
  [ -e "$delta" ] || continue
  sqlcmd -S "$SQL_SERVER" "${SQLCMD_ARGS[@]}" -d "$SQL_DB" -I -b -f 65001 -i "$(sqlcmd_path "$delta")"
done
# #201: this catalog holds disposable test data only, so an unattributed fixture write is allowed.
# The triage rule itself is NOT relaxed - domain35 proves a triage account is still refused here, and
# turns this flag off to prove the strict refusals a production catalog gives.
sqlcmd -S "$SQL_SERVER" "${SQLCMD_ARGS[@]}" -d "$SQL_DB" -I -b -Q "INSERT INTO CatalogMode (ModeId, IsFixtureCatalog) VALUES (1, 1);"
# lib.sql defines the helpers a domain file calls (ExpectFixtureRows). Without it every test that
# asserts on fixture row counts fails with "could not find stored procedure".
sqlcmd -S "$SQL_SERVER" "${SQLCMD_ARGS[@]}" -d "$SQL_DB" -I -b -f 65001 -i "$(sqlcmd_path "$DIR/lib.sql")"
echo "LOADED OK"
