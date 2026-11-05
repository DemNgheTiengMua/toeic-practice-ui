#!/usr/bin/env bash
# docs/schema-tests/run-one.sh <domain-file.sql> — run one domain file against a fresh database.
#
# run-all.sh runs everything; this runs the file you are working on without waiting for the other 24,
# and keeps sqlcmd's exit status (a trailing `echo` would make every run look green).
#
# Not wired into run-all.sh on purpose: it drops and recreates the database, exactly as the harness
# already does per file, so it proves nothing the harness does not.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$DIR/sqlcmd-env.sh"
file="$1"
bash "$DIR/reset-and-load.sh" >/dev/null 2>&1 || { echo "RESET FAILED"; exit 1; }
sqlcmd -S "$SQL_SERVER" "${SQLCMD_ARGS[@]}" -d "$SQL_DB" -I -b -f 65001 -i "$(sqlcmd_path "$file")" 2>&1
