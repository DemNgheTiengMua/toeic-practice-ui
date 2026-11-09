#!/usr/bin/env bash
#
# #247.1: the cutover, as one window with a stated order and a rollback that is a rename.
#
#   0. refuse to start unless the readiness probe is clean      (mid-sitting on a legacy exam)
#   1. snapshot every wallet from the OLD database              (balance AND row count)
#   2. build the NEW database from docs/database-schema.sql     (single source of truth, ADR-0001)
#   3. import the preserved rows                                (same schema both sides, so SELECT *)
#   4. reconcile the NEW balances against the step-1 snapshot   (blocks on any discrepancy)
#   5. retain the OLD database under a timestamped name         (the rollback)
#
# Steps 0 and 4 REFUSE. A cutover that dropped credits cannot be detected afterwards, because the new
# balance agrees with the ledger it imported - the snapshot in step 1 is the only thing that can see it.
#
#   scripts/cutover.sh dry-run     print the plan and stop (default)
#   scripts/cutover.sh run         execute steps 0-5
#   scripts/cutover.sh rollback    drop NEW, rename OLD back
#
# Overrides: SOURCE_DB, TARGET_DB, KEEP_DB.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"   # sqlcmd cannot open MSYS-style /d/... paths, so its -i paths stay relative

SERVER='(localdb)\MSSQLLocalDB'
SOURCE_DB="${SOURCE_DB:-ExtraEfficiency}"
TARGET_DB="${TARGET_DB:-ExtraEfficiencyCut}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
KEEP_DB="${KEEP_DB:-${SOURCE_DB}_pre_${STAMP}}"

say()  { printf '\033[1;34m[cutover]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[cutover]\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31m[cutover]\033[0m %s\n' "$*" >&2; exit 1; }

sql() { sqlcmd -S "$SERVER" -I -b -f 65001 "$@"; }

q() { # q <db> <query> -> one scalar
  sql -d "$1" -h -1 -W -Q "SET NOCOUNT ON; $2" | tr -d '\r' | head -1
}

db_exists() {
  [ "$(q master "SELECT CASE WHEN DB_ID('$1') IS NULL THEN 0 ELSE 1 END")" = "1" ]
}

# The preserved set, at table granularity - the list DataDisposition marks Preserve. Dropped tables are
# absent on purpose: this script moves what survives, not what the rebuild replaces.
#
# ORDER IS NOT HAND-MAINTAINED. A hand-written foreign-key order is a second description of the schema
# and drifts; the first draft of this script also hand-wrote a COLUMN list and named a column Users does
# not have ("IsActive" - the flag is IsDeleted), which sqlcmd refused as a batch. So: constraints go off
# for the load and are CHECKED afterwards with WITH CHECK, which proves every FK and still catches a row
# that genuinely points at nothing.
PRESERVED_TABLES=(
  Roles Permissions RolePermissions
  Certificates Skills Sections ScoreBands ScoreScales
  Users
  Packages Orders PaymentEvents CreditTransactions Complaints
  MentorProfiles MentorSlots Bookings MentorPayouts MentorPayoutItems
  Exams ExamEditions ExamPublishAudits ExamSectionAudio
  QuestionCategories Questions QuestionGroups QuestionOptions SectionCriteria
  GradingCriteria GradingCriterionDescriptors GradingCriterionAnchors GradingRubricLocks
  ExamAttempts AttemptAnswers AttemptSkillScores FreeResponses AiGradings AiGradingScores
)

# ---------------------------------------------------------------------------------------------
# step 1: the snapshot, to a FILE. The comparison in step 4 runs against a number taken before the data
# moved, so a number held only in memory would not survive the thing it is meant to check.
# ---------------------------------------------------------------------------------------------
cmd_snapshot() {
  local out="logs/cutover-snapshot-${STAMP}.txt"
  mkdir -p logs
  say "snapshotting every wallet from $SOURCE_DB -> $out"
  # tr -d '\r': sqlcmd emits CRLF, so every captured value ends in a carriage return that never shows
  # in the output but makes "0" != "0\r" and reports every wallet as a mismatch. Measured: the first run
  # printed "user 1: expected 0 credits in 0 row(s), got 0 in 0" for all four users and blocked.
  sql -d "$SOURCE_DB" -h -1 -W -s '|' -Q "SET NOCOUNT ON;
    SELECT u.UserId,
           ISNULL((SELECT SUM(Delta) FROM CreditTransactions t WHERE t.UserId = u.UserId), 0),
           (SELECT COUNT(*) FROM CreditTransactions t WHERE t.UserId = u.UserId)
    FROM Users u ORDER BY u.UserId;" | tr -d '\r' > "$out"
  say "$(grep -c '|' "$out" || true) wallet(s) captured"
}

cmd_dry_run() {
  say "PLAN (nothing has been changed)"
  cat <<EOF
  source database   $SOURCE_DB
  target database   $TARGET_DB
  rollback name     $KEEP_DB
  schema            docs/database-schema.sql   (single source of truth, ADR-0001)
  preserved tables  ${#PRESERVED_TABLES[@]}

  0. readiness      refuse unless no attempt is mid-sitting on a legacy exam
  1. snapshot       every wallet's balance AND row count, to a file
  2. build          $TARGET_DB from the schema file
  3. import         the preserved tables, ids intact, constraints checked afterwards
  4. reconcile      block unless every wallet matches the step-1 snapshot
  5. retain         rename $SOURCE_DB -> $KEEP_DB

  Run 'scripts/cutover.sh run' to execute, 'rollback' to undo step 5.
EOF
}

cmd_run() {
  db_exists "$SOURCE_DB" || die "source database $SOURCE_DB does not exist"
  [ "$(q "$SOURCE_DB" "SELECT COUNT(*) FROM Users")" -gt 0 ] \
    || die "$SOURCE_DB has no users; cutting over an empty database proves nothing"

  # ---- step 0: readiness -------------------------------------------------------------------
  say "step 0: readiness probe"
  local midsitting
  midsitting="$(q "$SOURCE_DB" "
    SELECT COUNT(*) FROM ExamAttempts a
    WHERE a.Status = 'in_progress'
      AND NOT EXISTS (SELECT 1 FROM ExamItems i WHERE i.ExamId = a.ExamId);")"
  [ "$midsitting" = "0" ] \
    || die "$midsitting attempt(s) are still in progress on an exam the pool has no roster for. Finish or abandon them first (CutoverReadinessService: mid_sitting_on_legacy_exam)."

  local legacy
  legacy="$(q "$SOURCE_DB" "
    SELECT COUNT(*) FROM Exams e
    WHERE NOT EXISTS (SELECT 1 FROM ExamItems i WHERE i.ExamId = e.ExamId);")"
  if [ "$legacy" != "0" ]; then
    warn "$legacy exam(s) carry no ExamItems roster: their content lives only in Questions."
    warn "That is the #250 work and this cutover does not perform it. Recorded, not blocked:"
    warn "the readiness probe separates content readiness from data consistency."
  fi
  say "no attempt is mid-sitting on a legacy exam"

  # ---- step 1: snapshot --------------------------------------------------------------------
  cmd_snapshot
  local snapshot_file="logs/cutover-snapshot-${STAMP}.txt"

  # ---- step 2: build -----------------------------------------------------------------------
  say "step 2: building $TARGET_DB from docs/database-schema.sql"
  sql -d master -Q "IF DB_ID('$TARGET_DB') IS NOT NULL
                    BEGIN ALTER DATABASE [$TARGET_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                          DROP DATABASE [$TARGET_DB]; END;
                    CREATE DATABASE [$TARGET_DB];" >/dev/null
  sql -d "$TARGET_DB" -i docs/database-schema.sql >/dev/null
  say "schema applied"

  # ---- step 3: preserved rows --------------------------------------------------------------
  # INSERT ... SELECT with three-part names, run FROM the target database. Two reasons this beat the
  # generated-INSERT approach the first draft used:
  #   * no value is ever rendered as text, so a decimal, a datetimeoffset and a varbinary all cross
  #     with their own types instead of a format the script has to get right;
  #   * both databases come from the same schema file, so the column ORDER is identical and SELECT *
  #     cannot drift the way a hand-written list did.
  # IDENTITY_INSERT is set per table because every preserved primary key is an identity column, and the
  # id is part of the fact being preserved.
  say "step 3: importing ${#PRESERVED_TABLES[@]} preserved table(s)"
  sql -d "$TARGET_DB" -Q "EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL';" >/dev/null

  # THE WRITE-AUTHORSHIP TRIGGERS ARE TURNED OFF FOR THIS BLOCK, and that is a decision rather than a
  # shortcut. Three of them refuse a preserved row for reasons that are true of AUTHORING and false of a
  # CUTOVER:
  #   trg_Sections_TriageCannotWrite   demands LastWrittenBy, and 33 seeded sections carry NULL because
  #                                    the seed predates the column. The row was authored by a staff
  #                                    account the cutover has no record of - inventing one would be the
  #                                    fabrication, not the NULL.
  #   trg_Exams_PublishRequiresAudit   refuses an exam inserted already 'published'. A preserved exam
  #                                    IS published; re-running the six readiness invariants over
  #                                    content the new build cannot judge would refuse every paper.
  #   trg_Items_LifecycleTransitions   and friends: a preserved item is already active.
  # They are re-enabled immediately after, and the CONSTRAINT check below still proves every foreign key.
  # This is not a weakened rule: outside this block the triggers are untouched and the suite proves it.
  say "  (write-authorship triggers off for the load; re-enabled below)"
  sql -d "$TARGET_DB" -Q "EXEC sp_MSforeachtable 'ALTER TABLE ? DISABLE TRIGGER ALL';" >/dev/null

  local imported=0 skipped=0 failed=0
  for table in "${PRESERVED_TABLES[@]}"; do
    if [ "$(q "$TARGET_DB" "SELECT CASE WHEN OBJECT_ID('$table') IS NULL THEN 0 ELSE 1 END")" != "1" ]; then
      warn "preserved table $table is not in the target schema; skipping"
      skipped=$((skipped + 1))
      continue
    fi
    if [ "$(q "$SOURCE_DB" "SELECT CASE WHEN OBJECT_ID('$table') IS NULL THEN 0 ELSE 1 END")" != "1" ]; then
      warn "preserved table $table does not exist in the source; skipping"
      skipped=$((skipped + 1))
      continue
    fi

    local batch_ok=1
    if ! sql -d "$TARGET_DB" -i scripts/cutover-import-table.sql -v Table="$table" -v Source="$SOURCE_DB" >/dev/null 2>&1; then
      batch_ok=0
    fi
    if [ "$batch_ok" = "1" ]; then
      imported=$((imported + 1))
    else
      warn "table $table could not be imported"
      failed=$((failed + 1))
    fi
  done
  say "$imported table(s) imported, $skipped skipped, $failed failed"

  say "re-enabling every trigger"
  sql -d "$TARGET_DB" -Q "EXEC sp_MSforeachtable 'ALTER TABLE ? ENABLE TRIGGER ALL';" >/dev/null

  say "re-enabling and CHECKING every constraint"
  if ! sql -d "$TARGET_DB" -Q "EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';" >/dev/null 2>&1; then
    die "a foreign key does not hold after the load. The target database is incomplete; $SOURCE_DB is untouched and nothing has been retained."
  fi
  say "every constraint holds"

  # ---- step 4: reconcile -------------------------------------------------------------------
  # THE STEP THAT CANNOT BE SKIPPED. The imported ledger agrees with itself by construction, so this
  # comparison against the pre-cutover snapshot is the only thing that can see a lost row.
  say "step 4: reconciling every balance against the snapshot"
  local mismatches=0
  # The snapshot is read on FD 3, NOT stdin. `q` runs sqlcmd, and sqlcmd reads stdin: inside a plain
  # `while read ... done < file` loop it consumes the snapshot itself, the loop never advances, and the
  # run repeats the first user forever. Measured: the script printed "user 1: expected 0 credits ..."
  # hundreds of times and never finished. stdin is also passed to sqlcmd as /dev/null for the same
  # reason, so nothing else can swallow the remaining lines.
  while IFS='|' read -r user_id expected_balance expected_count <&3; do
    [ -z "${user_id// }" ] && continue
    local actual_balance actual_count
    actual_balance="$(q "$TARGET_DB" "SELECT ISNULL((SELECT SUM(Delta) FROM CreditTransactions WHERE UserId = $user_id), 0);" < /dev/null)"
    actual_count="$(q "$TARGET_DB" "SELECT COUNT(*) FROM CreditTransactions WHERE UserId = $user_id;" < /dev/null)"
    if [ "$actual_balance" != "$expected_balance" ] || [ "$actual_count" != "$expected_count" ]; then
      warn "user $user_id: expected $expected_balance credits in $expected_count row(s), got $actual_balance in $actual_count"
      mismatches=$((mismatches + 1))
    fi
  done 3< "$snapshot_file"

  if [ "$mismatches" != "0" ]; then
    die "$mismatches wallet(s) did not reconcile. The step DID NOT proceed and $SOURCE_DB is untouched. Snapshot: $snapshot_file"
  fi
  say "every wallet reconciled"

  # ---- step 5: retain ----------------------------------------------------------------------
  say "retaining $SOURCE_DB as $KEEP_DB (this IS the rollback)"
  sql -d master -Q "ALTER DATABASE [$SOURCE_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    ALTER DATABASE [$SOURCE_DB] MODIFY NAME = [$KEEP_DB];
                    ALTER DATABASE [$KEEP_DB] SET MULTI_USER;" >/dev/null

  say "CUTOVER COMPLETE"
  say "  new database   $TARGET_DB"
  say "  rollback       scripts/cutover.sh rollback"
  say "  snapshot       $snapshot_file"
}

cmd_rollback() {
  # A rename in the other direction. Deliberately not a restore from a file: the old instance was never
  # copied, so putting it back is exact rather than best-effort.
  local kept="${1:-}"
  if [ -z "$kept" ]; then
    kept="$(sql -d master -h -1 -W -Q "SET NOCOUNT ON;
      SELECT TOP 1 name FROM sys.databases WHERE name LIKE '${SOURCE_DB}_pre_%' ORDER BY name DESC;" | tr -d '\r' | head -1)"
  fi
  [ -n "$kept" ] || die "no ${SOURCE_DB}_pre_* database found to roll back to"

  say "rolling back: dropping $TARGET_DB, restoring $kept as $SOURCE_DB"
  sql -d master -Q "IF DB_ID('$TARGET_DB') IS NOT NULL
                    BEGIN ALTER DATABASE [$TARGET_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                          DROP DATABASE [$TARGET_DB]; END;
                    IF DB_ID('$SOURCE_DB') IS NOT NULL
                    BEGIN ALTER DATABASE [$SOURCE_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                          DROP DATABASE [$SOURCE_DB]; END;
                    ALTER DATABASE [$kept] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    ALTER DATABASE [$kept] MODIFY NAME = [$SOURCE_DB];
                    ALTER DATABASE [$SOURCE_DB] SET MULTI_USER;" >/dev/null
  say "rolled back: $SOURCE_DB is the pre-cutover instance again"
}

case "${1:-dry-run}" in
  dry-run)  cmd_dry_run ;;
  run)      cmd_run ;;
  rollback) cmd_rollback "${2:-}" ;;
  *)        die "usage: $0 [dry-run|run|rollback [kept-db]]" ;;
esac
