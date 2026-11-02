#!/usr/bin/env bash
# docs/schema-tests/run-all.sh — load the schema on a fresh DB per test, run every domain file,
# report each verdict, and exit non-zero if any failed.
#
# It does NOT stop at the first failure. Until 2026-11 this script used `set -e`, so domain04 failing
# hid domain05..19 and the seed load behind it. AGENTS.md §1.1 names this the way to validate a schema
# change, so the documented command could not be run to completion.
#
# scripts/schema-tests-baseline.sh runs the same domain files one at a time and must report the same
# failing set. Both print `PASS <file>` / `FAIL <file>`, so their output stays diffable.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$DIR/../.." && pwd)"
S='(localdb)\MSSQLLocalDB'; DB='CompanionSchemaTest'
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

results=(); fails=0
run_one () {  # $1 = label, $2 = .sql file, $3 = 1 to inject the fixture guard, $4 = 1 to keep the existing database
  local label="$1" sql="$2" out
  # The idempotency re-run must hit the database the first load left behind; resetting between them
  # would make it a second fresh load and would prove nothing.
  if [ "${4:-0}" = 1 ]; then
    :
  elif ! bash "$DIR/reset-and-load.sh" >"$TMP/reset.log" 2>&1; then
    echo "FAIL $label"
    echo "    could not reset/load the database:"
    sed 's/^/    /' "$TMP/reset.log"
    fails=$((fails + 1)); results+=("FAIL $label"); return 1
  fi
  if [ "${3:-0}" = 1 ]; then
    # fixture-guard.awk asserts @@ROWCOUNT after every INSERT ... SELECT, so a fixture that matched
    # nothing aborts naming itself instead of letting the test's own THROW report the wrong reason.
    #
    # If awk itself fails, `sql` would point at an empty file and sqlcmd would exit 0 on it — every
    # domain file would report PASS while running nothing. So awk's status is checked, and the
    # guarded file is required to be non-empty, before sqlcmd is allowed near it.
    if ! awk -v LABEL="$label" -f "$DIR/fixture-guard.awk" "$sql" > "$TMP/guarded.sql"; then
      echo "FAIL $label"
      echo "    could not apply the fixture guard to $label; refusing to report a verdict on a file that never ran"
      fails=$((fails + 1)); results+=("FAIL $label"); return 1
    fi
    if [ ! -s "$TMP/guarded.sql" ]; then
      echo "FAIL $label"
      echo "    the fixture guard produced no output for $label; the file would have run as empty and passed"
      fails=$((fails + 1)); results+=("FAIL $label"); return 1
    fi
    sql="$TMP/guarded.sql"
  fi
  if out=$(sqlcmd -S "$S" -d "$DB" -I -b -f 65001 -i "$(cygpath -w "$sql")" 2>&1); then
    echo "PASS $label"; results+=("PASS $label"); return 0
  fi
  echo "FAIL $label"
  # Only the reason: sqlcmd's "(N rows affected)" chatter buries it otherwise.
  printf '%s\n' "$out" | grep -vE '^\([0-9]+ rows? affected\)$|^$' | sed 's/^/    /'
  fails=$((fails + 1)); results+=("FAIL $label"); return 1
}

echo "=== harness: the fixture guard fires on a zero-row fixture and names it"
run_one "harness-guard-selftest.sql" "$DIR/harness-guard-selftest.sql" 1

for t in "$DIR"/domain*.sql; do
  echo "=== $(basename "$t")"
  run_one "$(basename "$t")" "$t" 1
done

# These sit after the domain loop and were unreachable while run-all.sh stopped at domain04, so
# "the seed is re-runnable" had been assumed rather than checked.
echo "=== docs/seed-demo.sql (fresh database)"
run_one "docs/seed-demo.sql" "$ROOT/docs/seed-demo.sql" 0
echo "=== docs/seed-demo.sql (idempotency re-run, same database)"
run_one "docs/seed-demo.sql re-run" "$ROOT/docs/seed-demo.sql" 0 1

echo
echo "=== summary: ${#results[@]} checks, $(( ${#results[@]} - fails )) pass, $fails fail"
printf '%s\n' "${results[@]}"
if [ "$fails" -ne 0 ]; then
  echo
  echo "FAILED: $(printf '%s\n' "${results[@]}" | grep '^FAIL ' | sed 's/^FAIL //' | tr '\n' ' ')"
  exit 1
fi
echo "ALL CHECKS PASSED"
