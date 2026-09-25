#!/usr/bin/env bash
# docs/schema-tests/run-all.sh — load schema once, run every domain test on its own fresh DB.
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S='(localdb)\MSSQLLocalDB'; DB='CompanionSchemaTest'
for t in "$DIR"/domain*.sql; do
  echo "=== $t ==="
  bash "$DIR/reset-and-load.sh" >/dev/null
  sqlcmd -S "$S" -d "$DB" -I -b -i "$(cygpath -w "$t")"
done
echo "ALL DOMAIN TESTS PASSED"
