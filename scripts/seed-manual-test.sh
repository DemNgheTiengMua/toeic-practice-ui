#!/usr/bin/env bash
#
# seed-manual-test.sh — Reset + seed the manual-test database, then start both apps
# with the MOCK payment gateway and FAKE AI so a manual pass never spends real
# credits or real API quota.
#
#   scripts/seed-manual-test.sh seed     drop → schema → seed-demo → seed-diagnostic-benchmark
#   scripts/seed-manual-test.sh start    build and start WebAPI (5001) + WebMVC (5000)
#   scripts/seed-manual-test.sh fresh    stop → seed → start   (use before each pass)
#   scripts/seed-manual-test.sh stop     stop whatever listens on 5000 / 5001
#   scripts/seed-manual-test.sh status   ports, learner balance, seeded row counts
#   scripts/seed-manual-test.sh errors   server-side exceptions logged since start
#
# Differs from crawl-env.sh on purpose:
#   - crawl-env.sh uses a throwaway database (ExtraEfficiencyCrawl) for pipeline runs.
#   - This script targets the everyday ExtraEfficiency database that
#     src/WebAPI/appsettings.json points at, so `dotnet run --project src/WebAPI`
#     works too, and it loads BOTH seed files (demo + diagnostic benchmark).
#
# You may pass --real-ai to call SpeechSuper/Claude for real (costs money, needs
# .env keys). Logs land in logs/manual/{api,mvc}.log (gitignored).

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"   # sqlcmd cannot open MSYS-style /d/... paths, so its -i paths stay relative
SERVER='(localdb)\MSSQLLocalDB'
DB='ExtraEfficiency'
LOG_DIR="$ROOT/logs/manual"
API_URL='http://localhost:5001'
MVC_URL='http://localhost:5000'
CONFIG='Debug'
TFM='net10.0'

AI_MODE='fake'
for arg in "$@"; do
  case "$arg" in
    --real-ai) AI_MODE='real' ;;
  esac
done

say() { printf '\033[1;34m[seed]\033[0m %s\n' "$*"; }
die() { printf '\033[1;31m[seed]\033[0m %s\n' "$*" >&2; exit 1; }

# -f 65001: the .sql files are BOM-less UTF-8; without it every non-ASCII char is mangled.
sql() { sqlcmd -S "$SERVER" -I -b -f 65001 "$@"; }

# Windows PIDs listening on a TCP port (Git Bash has no lsof).
port_pids() {
  netstat -ano -p tcp 2>/dev/null \
    | awk -v p=":$1" '$2 ~ p"$" && $4 == "LISTENING" { print $5 }' | sort -u
}

learner_balance() {
  sql -d "$DB" -h -1 -W -Q "SET NOCOUNT ON; SELECT ISNULL(SUM(c.Delta),0) FROM CreditTransactions c JOIN Users u ON u.UserId = c.UserId WHERE u.Email = 'learner@demo.local';" | tr -d '[:space:]'
}

cmd_seed() {
  [[ "$DB" == "ExtraEfficiency" ]] || die "refusing to drop '$DB'"
  say "Dropping and recreating [$DB]"
  # SINGLE_USER kicks the API's pooled connections; a plain DROP would fail.
  sql -Q "IF DB_ID('$DB') IS NOT NULL BEGIN ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DB]; END; CREATE DATABASE [$DB];"

  say "Loading schema (docs/database-schema.sql)"
  sql -d "$DB" -i docs/database-schema.sql > "$LOG_DIR/seed.log"

  say "Loading demo seed (docs/seed-demo.sql)"
  sql -d "$DB" -i docs/seed-demo.sql >> "$LOG_DIR/seed.log"

  say "Loading diagnostic benchmark seed (docs/seed-diagnostic-benchmark.sql)"
  sql -d "$DB" -i docs/seed-diagnostic-benchmark.sql >> "$LOG_DIR/seed.log"

  say "Learner balance: $(learner_balance) credits"
}

cmd_stop() {
  local port pid
  for port in 5000 5001; do
    for pid in $(port_pids "$port"); do
      say "Stopping PID $pid on port $port"
      powershell -NoProfile -Command "Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue" > /dev/null 2>&1 || true
    done
  done
}

wait_up() {
  local url="$1" name="$2" log="$3" i
  for i in $(seq 1 60); do
    if [[ "$(curl -s -o /dev/null -w '%{http_code}' "$url" || true)" != "000" ]]; then
      say "$name is up at $url"; return 0
    fi
    sleep 1
  done
  tail -n 40 "$log" >&2
  die "$name did not come up within 60s (log: $log)"
}

cmd_start() {
  local port
  for port in 5000 5001; do
    [[ -z "$(port_pids "$port")" ]] || die "port $port is busy — run '$0 stop' first"
  done

  say "Building solution"
  # Relative path on purpose: MSYS path conversion is disabled on this host, so a
  # native tool (dotnet) cannot open "/d/..." — only bash builtins translate it.
  dotnet build ExtraEfficiency.slnx -c "$CONFIG" -v q -nologo > "$LOG_DIR/build.log" \
    || { tail -n 30 "$LOG_DIR/build.log" >&2; die "build failed (log: $LOG_DIR/build.log)"; }

  # The working directory is the content root: appsettings, wwwroot, mail-drop
  # and the .env lookup all resolve from the project folder, as with dotnet run.
  say "Starting WebAPI → $LOG_DIR/api.log"
  (
    cd "$ROOT/src/WebAPI"
    # `env` because bash rejects the dots in the logging key. EF command logs
    # are muted: the grading/insight workers poll every 2s and drown the log.
    nohup env \
      ASPNETCORE_ENVIRONMENT=Development \
      ASPNETCORE_URLS="$API_URL" \
      Payment__Gateway=mock \
      AI_PROVIDER="$AI_MODE" \
      Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning \
      dotnet "bin/$CONFIG/$TFM/WebAPI.dll" > "$LOG_DIR/api.log" 2>&1 &
  )
  wait_up "$API_URL/api/certificates" WebAPI "$LOG_DIR/api.log"

  say "Starting WebMVC → $LOG_DIR/mvc.log"
  (
    cd "$ROOT/src/WebMVC"
    nohup env \
      ASPNETCORE_ENVIRONMENT=Development \
      ASPNETCORE_URLS="$MVC_URL" \
      ApiBaseUrl="$API_URL" \
      dotnet "bin/$CONFIG/$TFM/WebMVC.dll" > "$LOG_DIR/mvc.log" 2>&1 &
  )
  wait_up "$MVC_URL/auth/sign-in" WebMVC "$LOG_DIR/mvc.log"

  say "Ready: $MVC_URL/auth/sign-in  (payment=mock, ai=$AI_MODE, db=$DB)"
}

cmd_status() {
  local port pids
  for port in 5000 5001; do
    pids="$(port_pids "$port" | tr '\n' ' ')"
    printf '  port %s: %s\n' "$port" "${pids:-down}"
  done
  printf '  learner balance: %s\n' "$(learner_balance 2>/dev/null || echo '?')"
  printf '  logs: %s\n' "$LOG_DIR"
  echo "  seeded rows:"
  sql -d "$DB" -h -1 -W -s"|" -Q "SET NOCOUNT ON;
    SELECT '    Users', COUNT(*) FROM Users
    UNION ALL SELECT '    Certificates', COUNT(*) FROM Certificates
    UNION ALL SELECT '    Exams', COUNT(*) FROM Exams
    UNION ALL SELECT '    Questions', COUNT(*) FROM Questions
    UNION ALL SELECT '    Lessons', COUNT(*) FROM Lessons
    UNION ALL SELECT '    Orders', COUNT(*) FROM Orders;"
}

# Every unhandled exception, with the stack trace that the browser never sees.
cmd_errors() {
  local f
  for f in "$LOG_DIR/api.log" "$LOG_DIR/mvc.log"; do
    [[ -f "$f" ]] || continue
    printf '\n===== %s =====\n' "${f##*/}"
    grep -n -A 25 -E '^(fail|crit):' "$f" || echo '  (no fail/crit entries)'
  done
}

mkdir -p "$LOG_DIR"
case "${1:-}" in
  fresh)  cmd_stop; cmd_seed; cmd_start ;;
  seed)   cmd_stop; cmd_seed ;;
  start)  cmd_start ;;
  stop)   cmd_stop ;;
  status) cmd_status ;;
  errors) cmd_errors ;;
  *) sed -n '3,24p' "$0"; exit 1 ;;
esac
