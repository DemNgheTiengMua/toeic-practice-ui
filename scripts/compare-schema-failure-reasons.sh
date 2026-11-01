#!/usr/bin/env bash
# Compare the SQL error numbers behind the pre-existing schema-test failures, so "same six files
# fail" is proved by the same six causes rather than only the same filenames.
set -uo pipefail
S='(localdb)\MSSQLLocalDB'
DB='CompanionSchemaTest'
files="domain04-content domain05-practice domain06-mockexam domain07-aigrading domain08-learningpath domain15-multicert-scoring"

for root in "$@"; do
  echo "#### $root"
  cd "$root" || exit 1
  for f in $files; do
    bash docs/schema-tests/reset-and-load.sh >/dev/null 2>&1
    codes=$(sqlcmd -S "$S" -d "$DB" -I -b -f 65001 -i "docs/schema-tests/$f.sql" 2>&1 \
      | grep -oE 'Msg [0-9]+' | sort -u | tr '\n' ' ')
    echo "$f: $codes"
  done
  cd - >/dev/null || exit 1
done
