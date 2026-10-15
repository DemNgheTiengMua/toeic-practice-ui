#!/usr/bin/env bash
# docs/schema-tests/run-all.sh — load schema once, run every domain test on its own fresh DB.
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$DIR/../.." && pwd)"
ROOT_WIN="$(cygpath -w "$ROOT")"
S='(localdb)\MSSQLLocalDB'; DB='CompanionSchemaTest'
for t in "$DIR"/domain*.sql; do
  echo "=== $t ==="
  bash "$DIR/reset-and-load.sh" >/dev/null
  sqlcmd -S "$S" -d "$DB" -I -b -f 65001 -i "$(cygpath -w "$t")"
done

echo "=== docs/seed-demo.sql ==="
bash "$DIR/reset-and-load.sh" >/dev/null
sqlcmd -S "$S" -d "$DB" -I -b -f 65001 -i "$ROOT_WIN/docs/seed-demo.sql"
echo "=== docs/seed-demo.sql (idempotency check) ==="
sqlcmd -S "$S" -d "$DB" -I -b -f 65001 -i "$ROOT_WIN/docs/seed-demo.sql"

echo "ALL DOMAIN TESTS PASSED"
