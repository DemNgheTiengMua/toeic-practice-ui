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
  if out=$(sqlcmd -S "$S" -d "$DB" -I -b -f 65001 -i "$(cygpath -w "$t")" 2>&1); then
    echo "PASS $(basename "$t")"
  else
    # The error numbers, not only the filename: "the same six files fail" is not the same claim
    # as "for the same six reasons", and a branch can change a failure's cause without adding one.
    echo "FAIL $(basename "$t") $(echo "$out" | grep -oE 'Msg [0-9]+' | sort -u | tr '\n' ' ')"
    fail=$((fail + 1))
  fi
done
echo "--- failures: $fail ---"
