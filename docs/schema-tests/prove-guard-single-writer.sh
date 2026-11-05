#!/usr/bin/env bash
# docs/schema-tests/prove-guard-single-writer.sh — prove every guard in domain27-single-writer.sql is
# load-bearing.
#
# Run by hand when the single-writer rules change:  bash docs/schema-tests/prove-guard-single-writer.sh
# For each guard it removes or weakens it in docs/database-schema.sql, re-runs domain27, checks the
# failure is the expected one, then restores the schema. A guard whose removal leaves domain27 green
# was not tested. A patch whose replacement did not compile proves nothing either, so that is
# reported separately.
#
# Deliberately NOT part of run-all.sh, for the reason prove-guard.sh states: it rewrites the schema in
# place, and a harness that does that on every CI run is one interrupted job away from a truncated
# schema in the working tree. NEVER run this while dotnet test is going - DatabaseFixture loads the
# schema once per run and would load a patched copy.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCHEMA="$DIR/../database-schema.sql"
TEST="$DIR/domain27-single-writer.sql"
BACKUP="$(mktemp)"; cp "$SCHEMA" "$BACKUP"
trap 'cp "$BACKUP" "$SCHEMA"; rm -f "$BACKUP"' EXIT

run_patch () {  # $1 label, $2 expected-failure substring, $3 text to replace, $4 replacement, $5 mode
  local label="$1" expect="$2" old="$3" new="$4" mode="${5:-deleted}" log
  cp "$BACKUP" "$SCHEMA"
  python - "$SCHEMA" "$old" "$new" <<'PY' || { echo "INVALID           $label  (patch target not found)"; return; }
import sys, io
path, old, new = sys.argv[1], sys.argv[2], sys.argv[3]
text = io.open(path, encoding='utf-8').read()
if old not in text: sys.exit(1)
io.open(path, 'w', encoding='utf-8', newline='').write(text.replace(old, new, 1))
PY
  log="$(mktemp)"
  bash "$DIR/run-one.sh" "$TEST" >"$log" 2>&1
  if [ $? -eq 0 ]; then
    echo "NOT LOAD-BEARING  $label  (domain27 still passed with the guard removed)"
  elif grep -q 'Incorrect syntax\|Invalid object name\|Invalid column name\|Invalid constraint\|Invalid object' "$log"; then
    echo "INVALID           $label  (the patched schema did not compile, so the failure proves nothing)"
  elif grep -qF "$expect" "$log"; then
    echo "RED               $label  -> $(grep -o 'EXPECT_REJECT FAILED[^,]*' "$log" | head -1)"
  elif grep -q 'refused by the wrong guard' "$log"; then
    # The guard that fired was a different one. On this table that is the trap the ticket names: a
    # CHECK or a foreign key refusing the row before the writer guard ever runs.
    echo "DIFFERENT GUARD   $label  -> $(grep -o 'refused by the wrong guard.*' "$log" | head -1)"
  else
    echo "UNEXPECTED        $label  -> $(grep -oE 'Msg [0-9]+.*|EXPECT_REJECT FAILED.*' "$log" | head -1)"
  fi
  rm -f "$log"
}

# ---------------------------------------------------------------------------------------------
# The attempt score and band guard.
# ---------------------------------------------------------------------------------------------

# 1. the whole writer comparison. RETURN exits before it.
run_patch "trg_Attempts_SingleWriter ownership check (removed)" \
  "a second writer changed the attempt band" \
  "  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN @touched t ON t.AttemptId = i.AttemptId
    WHERE ISNULL(i.WrittenByService, '') <> @owner
       OR (EXISTS (SELECT 1 FROM deleted d WHERE d.AttemptId = i.AttemptId) AND NOT UPDATE(WrittenByService))
  )
    THROW 50070" \
  "  IF 1 = 0
    THROW 50070"

# 2. the UPDATE(WrittenByService) half. This is the one a value-only guard omits, and the test it
#    breaks is the inherited-name case: a second writer that changes only TotalScore.
run_patch "the UPDATE(WrittenByService) statement check (attempt)" \
  "a second writer inherited the owner's name" \
  "       OR (EXISTS (SELECT 1 FROM deleted d WHERE d.AttemptId = i.AttemptId) AND NOT UPDATE(WrittenByService))" \
  ""

# 3. the monotonic comparison. `<=` weakened to `<` lets an equal version through, which is a replay
#    overwriting the result it is replaying.
run_patch "the monotonic comparison (attempt, <= weakened to <)" \
  "an equal projection version overwrote the stored result" \
  "    WHERE i.ProjectionVersion <= t.WasVersion
  )
    THROW 50071, 'The attempt score and band projection version must be newer" \
  "    WHERE i.ProjectionVersion < t.WasVersion
  )
    THROW 50071, 'The attempt score and band projection version must be newer"

# 3b. and `<=` replaced by `<>`. This is the naive "the version must have changed" operator, and it is
#     wrong in the OPPOSITE direction from patch 3: it refuses a forward move as well as a stale one,
#     so the owner cannot write its own result at all. The owner-write test is what catches it. Worth
#     recording because both operators look reasonable and each breaks a different half.
run_patch "the monotonic comparison (attempt, <= replaced by <>)" \
  "the attempt scoring writer could not write its own score" \
  "    WHERE i.ProjectionVersion <= t.WasVersion
  )
    THROW 50071, 'The attempt score and band projection version must be newer" \
  "    WHERE i.ProjectionVersion <> t.WasVersion
  )
    THROW 50071, 'The attempt score and band projection version must be newer"

# 4. the whole monotonic guard removed. The equal-version test is the first to notice, which is why
#    it carries the ownership of this patch rather than the lower-version one.
run_patch "the monotonic guard (attempt, removed)" \
  "an equal projection version overwrote the stored result" \
  "  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN @touched t ON t.AttemptId = i.AttemptId
    WHERE i.ProjectionVersion <= t.WasVersion
  )
    THROW 50071" \
  "  IF 1 = 0
    THROW 50071"

# ---------------------------------------------------------------------------------------------
# The placement snapshot guard.
# ---------------------------------------------------------------------------------------------

# 5. the ownership comparison on the placement snapshot. Unlike the attempt guard this one is reached
#    by a row that also satisfies CK_Placement_DoneHasResult and the snapshot-matches-attempt trigger,
#    so a DIFFERENT GUARD verdict here would mean the test is no longer about this rule.
run_patch "trg_PlacementTests_SingleWriter ownership check (removed)" \
  "PlacementService wrote a snapshot AttemptEngine owns" \
  "  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN @touched t ON t.PlacementId = i.PlacementId
    WHERE ISNULL(i.WrittenByService, '') <> @owner
       OR (EXISTS (SELECT 1 FROM deleted d WHERE d.PlacementId = i.PlacementId) AND NOT UPDATE(WrittenByService))
  )
    THROW 50072" \
  "  IF 1 = 0
    THROW 50072"

# 6. the inherited-name half on the snapshot
run_patch "the UPDATE(WrittenByService) statement check (placement)" \
  "a second writer inherited the placement owner's name" \
  "       OR (EXISTS (SELECT 1 FROM deleted d WHERE d.PlacementId = i.PlacementId) AND NOT UPDATE(WrittenByService))" \
  ""

# 7. the monotonic guard, which is what refuses the out-of-order callback
run_patch "the monotonic guard (placement, removed)" \
  "a stale callback overwrote a newer placement result" \
  "  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN @touched t ON t.PlacementId = i.PlacementId
    WHERE i.ProjectionVersion <= t.WasVersion
  )
    THROW 50073" \
  "  IF 1 = 0
    THROW 50073"

# 8. the same guard weakened to `<`, which accepts an equal version
run_patch "the monotonic comparison (placement, <= weakened to <)" \
  "a stale callback overwrote a newer placement result" \
  "    WHERE i.ProjectionVersion <= t.WasVersion
  )
    THROW 50073" \
  "    WHERE i.ProjectionVersion < t.WasVersion
  )
    THROW 50073"

# ---------------------------------------------------------------------------------------------
# The step closure guard.
# ---------------------------------------------------------------------------------------------

# 9. the ownership comparison
run_patch "trg_LearnerPathSteps_SingleWriter ownership check (removed)" \
  "LearningPathService rewrote a closed step" \
  "  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN @touched t ON t.StepId = i.StepId
    WHERE ISNULL(i.WrittenByService, '') <> @owner
       OR (EXISTS (SELECT 1 FROM deleted d WHERE d.StepId = i.StepId) AND NOT UPDATE(WrittenByService))
  )
    THROW 50075" \
  "  IF 1 = 0
    THROW 50075"

# 10. the inherited-name half on the closure
run_patch "the UPDATE(WrittenByService) statement check (step)" \
  "a second writer inherited the step closure owner's name" \
  "       OR (EXISTS (SELECT 1 FROM deleted d WHERE d.StepId = i.StepId) AND NOT UPDATE(WrittenByService))" \
  ""

# 11. the reopen guard. Without it a closed step can be moved back to an open state by its own owner,
#     which is the same fact written through the open-state door.
run_patch "the closed-step reopen guard" \
  "an open step was reopened through the closure guard" \
  "  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN @touched t ON t.StepId = i.StepId
    WHERE t.WasDone = 1 AND i.Status <> 'done'
  )
    THROW 50074" \
  "  IF 1 = 0
    THROW 50074"

# 12. the open-to-open escape. If the touched predicate widened to every Status change, moving a step
#     from available to in_progress would be refused - a guard that looks right and breaks the
#     product. This patch reproduces that over-reach and expects the control test to go red.
run_patch "the open-to-open escape (guard widened to every status move)" \
  "the closure guard blocked an open-to-open step move" \
  "             AND (i.Status = 'done' OR d.Status = 'done'" \
  "             AND (i.Status <> d.Status OR d.Status = 'done'"

# ---------------------------------------------------------------------------------------------
# The registry, which is the only place coverage is stated.
# ---------------------------------------------------------------------------------------------

# 13. the CHECK that stops a fact claiming coverage it does not have.
run_patch "CK_DerivedFactOwners_CoveredNamesTable" \
  "a covered fact with no table was accepted" \
  "  CONSTRAINT CK_DerivedFactOwners_CoveredNamesTable CHECK (
       (Coverage =  'covered' AND CoveredTable IS NOT NULL)
    OR (Coverage <> 'covered' AND CoveredTable IS NULL))" \
  "  CONSTRAINT CK_DerivedFactOwners_CoveredNamesTable CHECK (1 = 1)"

# 14. the control: unpatched, the file is green, so every RED above means something
cp "$BACKUP" "$SCHEMA"
bash "$DIR/run-one.sh" "$TEST" >/dev/null 2>&1 \
  && echo "GREEN             unpatched schema (control: the file passes on its own)" \
  || echo "NOT GREEN         unpatched schema - the whole exercise is meaningless"
