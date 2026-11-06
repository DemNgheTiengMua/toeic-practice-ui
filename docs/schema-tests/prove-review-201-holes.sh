#!/usr/bin/env bash
# docs/schema-tests/prove-review-201-holes.sh — the three writes the #201 review reproduced, in BOTH
# catalog modes, as a GENUINE triage-only account.
#
# Run by hand after changing the triage guard:
#   bash docs/schema-tests/prove-review-201-holes.sh
#
# The three cases, from the review:
#   hole 1  a raw UPDATE of Explanation on an ItemVersions row whose LastWrittenBy IS NULL
#   hole 2  a raw INSERT of a whole new ItemVersions row, declaring nobody
#   hole 3  the two above as an AUTHOR, which must be ACCEPTED (the accepting control)
#
# Each case is run twice: in a FIXTURE catalog (what the harness and the test suite use) and in a
# STRICT catalog (what production is). The strict run is the one that answers "is this closed"; the
# fixture run shows the exact, documented scope of the exemption.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$DIR/../.." && pwd)"
SEED="$ROOT/docs/seed-demo.sql"
PROBE="$DIR/review-201-holes.sql"

q() { cat > /tmp/q.sql; sqlcmd -S '(localdb)\MSSQLLocalDB' -C -d EE201Sod -I -b -f 65001 -i "$(cygpath -w /tmp/q.sql)" 2>&1; }

run_mode() {
  local mode="$1"
  TEST_DB_NAME=EE201Sod bash "$DIR/reset-and-load.sh" >/dev/null 2>&1 || { echo RESET_FAILED; return 1; }
  sqlcmd -S '(localdb)\MSSQLLocalDB' -C -d EE201Sod -I -b -f 65001 -i "$(cygpath -w "$SEED")" >/dev/null 2>&1 \
    || { echo SEED_FAILED; return 1; }
  if [ "$mode" = "strict" ]; then
    echo "UPDATE CatalogMode SET IsFixtureCatalog = 0 WHERE ModeId = 1;" | q >/dev/null
  fi
  sqlcmd -S '(localdb)\MSSQLLocalDB' -C -d EE201Sod -I -b -f 65001 -i "$(cygpath -w "$PROBE")" 2>&1 \
    | grep -E '^(===|CASE|CONTROL)'
}

echo "################ MODE: FIXTURE CATALOG (harness / test suite) ################"
run_mode fixture
echo
echo "################ MODE: STRICT CATALOG (production) ################"
run_mode strict
