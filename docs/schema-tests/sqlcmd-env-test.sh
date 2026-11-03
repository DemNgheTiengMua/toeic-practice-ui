#!/usr/bin/env bash
# docs/schema-tests/sqlcmd-env-test.sh — checks sqlcmd-env.sh reads CI's connection string into the
# right sqlcmd arguments, and that a run with nothing set still targets LocalDB. Without this, a
# typo in the field parser would make CI connect to the wrong server and every check would report a
# connection failure that looks like a schema defect.
#
# Not run by run-all.sh or by CI's step: CI exercises the parser for real (a misparsed field cannot
# connect, so the step fails), and this is the fast local check for the cases a live server hides.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fail=0
check () {  # $1 = label, $2 = expected, $3 = actual
  if [ "$2" = "$3" ]; then
    echo "PASS $1"
  else
    echo "FAIL $1"
    echo "    expected: $2"
    echo "    actual:   $3"
    fail=$((fail + 1))
  fi
}

# CI's connection string, verbatim from .github/workflows/ci.yml.
out=$(
  TEST_DB_CONNECTION='Server=localhost,1433;User ID=sa;Password=Str0ng!Passw0rd;TrustServerCertificate=True' \
  bash -c '. "$1/sqlcmd-env.sh"; printf "%s|%s|%s|%s" "$SQL_SERVER" "$SQL_DB" "$SQL_USER" "$(sqlcmd_field password)"' _ "$DIR"
)
check "CI server, catalog, user and password" 'localhost,1433|CompanionSchemaTest|sa|Str0ng!Passw0rd' "$out"

# A trusted connection passes no credentials, and defaults stay LocalDB/CompanionSchemaTest.
out=$(
  env -u TEST_DB_CONNECTION -u TEST_DB_NAME -u TEST_DB_PASSWORD \
  bash -c '. "$1/sqlcmd-env.sh"; printf "%s|%s|%s" "$SQL_SERVER" "$SQL_DB" "${SQL_USER:-none}"' _ "$DIR"
)
check "no env: LocalDB default and no credentials" '(localdb)\MSSQLLocalDB|CompanionSchemaTest|none' "$out"

# TEST_DB_NAME moves the catalog, so two worktrees do not drop each other's database.
out=$(
  TEST_DB_NAME=Scratch170 \
  bash -c '. "$1/sqlcmd-env.sh"; printf "%s" "$SQL_DB"' _ "$DIR"
)
check "TEST_DB_NAME overrides the catalog" 'Scratch170' "$out"

# A ';' or '=' inside a QUOTED password must not be read as a new field. Unquoted, ';' really does
# end the field in ADO.NET, so quoting is what has to be handled here.
out=$(
  TEST_DB_CONNECTION='Server=db;User ID=sa;Password="a=b;c"' \
  bash -c '. "$1/sqlcmd-env.sh"; printf "%s" "$(sqlcmd_field password)"' _ "$DIR"
)
check "quoted password containing '=' and ';' survives" 'a=b;c' "$out"

echo
if [ "$fail" -ne 0 ]; then
  echo "FAILED: $fail check(s)"
  exit 1
fi
echo "ALL CHECKS PASSED"
