#!/usr/bin/env bash
# Run each schema-test domain file independently on its own fresh database and report one verdict
# per file, plus the error numbers behind each failure.
#
# run-all.sh reports the same per-file verdicts from the same fixture guard; its output is a superset
# (it also runs the guard self-test and the seed load). Diff the `PASS`/`FAIL` lines to compare two
# branches; the sets must match when nothing changed.
#
# Useful for deciding whether YOUR change broke a schema test: run it on the parent commit and on
# your branch, then compare the failure lists. #146 used this to prove its six failures predated the
# work.
#
#   scripts/schema-tests-baseline.sh .              # this checkout
#   git worktree add ../base HEAD~1 && scripts/schema-tests-baseline.sh ../base
set -uo pipefail
ROOT="${1:-.}"
cd "$ROOT" || exit 1
DIR=docs/schema-tests
# Same server/credentials as reset-and-load.sh: the reset and the test must hit one database.
. "$DIR/sqlcmd-env.sh"   # SQL_SERVER, SQL_DB, SQLCMD_ARGS, sqlcmd_path
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
fail=0
for t in "$DIR"/domain*.sql; do
  if ! bash "$DIR/reset-and-load.sh" >"$TMP/reset.log" 2>&1; then
    echo "FAIL $(basename "$t") (database reset/load failed)"
    sed 's/^/    /' "$TMP/reset.log"
    fail=$((fail + 1)); continue
  fi
  # Same fixture guard run-all.sh uses: without it, a fixture INSERT ... SELECT matching zero rows
  # reports the test's own EXPECT_REJECT instead of the row-count failure that actually happened.
  # A failed awk leaves an empty guarded file, and sqlcmd exits 0 on an empty file — which would
  # report PASS for a file that never ran. Check the guard before trusting its output.
  if ! awk -v LABEL="$(basename "$t")" -f "$DIR/fixture-guard.awk" "$t" > "$TMP/guarded.sql" \
     || [ ! -s "$TMP/guarded.sql" ]; then
    echo "FAIL $(basename "$t") (fixture guard produced no output; the file would have run as empty and passed)"
    fail=$((fail + 1)); continue
  fi
  if out=$(sqlcmd -S "$SQL_SERVER" "${SQLCMD_ARGS[@]}" -d "$SQL_DB" -I -b -f 65001 -i "$(sqlcmd_path "$TMP/guarded.sql")" 2>&1); then
    echo "PASS $(basename "$t")"
  else
    # The error numbers, not only the filename: "the same six files fail" is not the same claim
    # as "for the same six reasons", and a branch can change a failure's cause without adding one.
    echo "FAIL $(basename "$t") $(echo "$out" | grep -oE 'Msg [0-9]+' | sort -u | tr '\n' ' ')"
    fail=$((fail + 1))
  fi
done
echo "--- failures: $fail ---"
exit $(( fail > 0 ))
