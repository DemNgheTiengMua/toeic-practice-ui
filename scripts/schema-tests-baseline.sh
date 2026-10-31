#!/usr/bin/env bash
# Run each schema-test domain file independently, so one pre-existing failure does not
# hide every later file behind it (docs/schema-tests/run-all.sh uses `set -e` and stops).
#
# Useful for deciding whether YOUR change broke a schema test: run it on the parent
# commit and on your branch, then compare the failure lists. #146 used this to prove
# its six failures predated the work.
#
#   scripts/schema-tests-baseline.sh .              # this checkout
#   git worktree add ../base HEAD~1 && scripts/schema-tests-baseline.sh ../base
set -uo pipefail
ROOT="${1:-.}"
cd "$ROOT" || exit 1
S='(localdb)\MSSQLLocalDB'
DB='CompanionSchemaTest'
fail=0
for t in docs/schema-tests/domain*.sql; do
  bash docs/schema-tests/reset-and-load.sh >/dev/null 2>&1
  if sqlcmd -S "$S" -d "$DB" -I -b -f 65001 -i "$(cygpath -w "$t")" >/dev/null 2>&1; then
    echo "PASS $(basename "$t")"
  else
    echo "FAIL $(basename "$t")"
    fail=$((fail + 1))
  fi
done
echo "--- failures: $fail ---"
