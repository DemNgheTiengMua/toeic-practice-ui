# docs/schema-tests/sqlcmd-env.sh — sourced by reset-and-load.sh, the one place that turns the
# C# tests' TEST_DB_CONNECTION into sqlcmd arguments.
#
# Why: the harness used to hard-code (localdb)\MSSQLLocalDB, so running it on CI — where SQL Server
# lives in a container behind SQL authentication — was impossible (#170). Reading the same variables
# tests/Services.Tests/DatabaseFixture.cs already reads keeps one command for both environments
# instead of a CI-only fork that drifts.
#
# The catalog comes from TEST_DB_NAME, default CompanionSchemaTest. This harness DROPS AND RECREATES
# its database once per check, so only point TEST_DB_NAME at a scratch catalog — it exists here so
# two worktrees can run the harness at once instead of colliding on one name.
#
# Defaults are unchanged when nothing is set, so a local run behaves exactly as before.

# Field of the ADO.NET connection string, matched case-insensitively; empty when absent. awk rather
# than a `while read` loop because that loop would run in a subshell and lose the value.
#
# Scans character by character instead of splitting on ";" because a password may contain one — ADO.NET
# allows it when quoted ('a;b' or "a;b"), and a naive split would truncate the password and turn a
# login failure into what looks like a schema defect. Outer quotes are stripped from the value.
# shortcut: SqlConnectionStringBuilder alone would be exact, but it needs .NET to be built first,
# so this harness (which must fail before Build) keeps its own scanner; revisit if a field with
# escaped quotes ever appears in CI's connection string.
sqlcmd_field () {
  printf '%s' "${TEST_DB_CONNECTION:-}" |
  awk -v want="$1" '
    BEGIN { SQ = sprintf("%c", 39); DQ = sprintf("%c", 34) }
    { s = s $0 }
    END {
      nf = 0; cur = ""; q = ""
      for (i = 1; i <= length(s); i++) {
        c = substr(s, i, 1)
        if (q != "") { if (c == q) q = ""; else cur = cur c; continue }
        if (c == SQ || c == DQ) { q = c; continue }
        if (c == ";") { field[++nf] = cur; cur = ""; continue }
        cur = cur c
      }
      field[++nf] = cur
      for (i = 1; i <= nf; i++) {
        eq = index(field[i], "=")
        if (eq == 0) continue
        key = tolower(substr(field[i], 1, eq - 1))
        gsub(/^[ \t]+|[ \t]+$/, "", key)
        if (key == want) { print substr(field[i], eq + 1); exit }
      }
    }'
}

SQL_SERVER="$(sqlcmd_field server)"
[ -n "$SQL_SERVER" ] || SQL_SERVER='(localdb)\MSSQLLocalDB'
SQL_DB="${TEST_DB_NAME:-CompanionSchemaTest}"

# Trusted connection (the default) passes no credentials; a connection string carrying a user does.
# -C is unconditional, as tests/Services.Tests/DatabaseFixture.cs does: sqlcmd 18 encrypts by default
# and the CI container's certificate is self-signed.
SQL_USER="$(sqlcmd_field 'user id')"
[ -n "$SQL_USER" ] || SQL_USER="$(sqlcmd_field uid)"
SQLCMD_ARGS=(-C)
if [ -n "$SQL_USER" ]; then
  SQLCMD_ARGS+=(-U "$SQL_USER" -P "$(sqlcmd_field password)")
fi

# MSYS needs a Windows path for -i; on a POSIX host the path is already right, and cygpath would
# abort the script under `set -e`.
sqlcmd_path () {
  if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi
}
