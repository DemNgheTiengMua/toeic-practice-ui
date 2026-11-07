#!/usr/bin/env bash
# docs/schema-tests/prove-guard-placement-result.sh — prove every guard in
# domain37-placement-skill-result.sql is load-bearing.
#
# Run by hand when the per-skill result's rules change:
#   bash docs/schema-tests/prove-guard-placement-result.sh
#
# For each guard it removes or weakens it in docs/database-schema.sql, re-runs domain37, checks the
# failure is the expected one, then restores the schema. A guard whose removal leaves domain37 green
# was not tested. Deliberately NOT part of run-all.sh for the reason prove-guard.sh states: it rewrites
# the schema in place. NEVER run this while dotnet test is going.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCHEMA="$DIR/../database-schema.sql"
TEST="$DIR/domain37-placement-skill-result.sql"
BACKUP="$(mktemp)"; cp "$SCHEMA" "$BACKUP"
trap 'cp "$BACKUP" "$SCHEMA"; rm -f "$BACKUP"' EXIT

run_patch () {  # $1 label, $2 expected-failure substring, $3 text to replace, $4 replacement
  local label="$1" expect="$2" old="$3" new="$4" log
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
    echo "NOT LOAD-BEARING  $label  (domain37 still passed with the guard removed)"
  elif grep -q 'Incorrect syntax\|Invalid object name\|Invalid column name\|Invalid constraint\|Invalid object' "$log"; then
    echo "INVALID           $label  (the patched schema did not compile, so the failure proves nothing)"
  elif grep -qF "$expect" "$log"; then
    echo "RED               $label  -> $(grep -o 'EXPECT_REJECT FAILED[^,]*' "$log" | head -1)"
  elif grep -q 'refused by the wrong guard' "$log"; then
    echo "DIFFERENT GUARD   $label  -> $(grep -o 'refused by the wrong guard.*' "$log" | head -1)"
  else
    echo "UNEXPECTED        $label  -> $(grep -oE 'Msg [0-9]+.*|EXPECT_REJECT FAILED.*' "$log" | head -1)"
  fi
  rm -f "$log"
}

# 1. the shape of each status. Without it a `measured` row with no level is stor able, which is the
#    original defect wearing the new status column.
run_patch "CK_PSR_StatusCarriesItsShape" \
  "a measured skill with no level was accepted" \
  "  CONSTRAINT CK_PSR_StatusCarriesItsShape CHECK (
       (ResultStatus IN ('measured','estimated_from_one_task')
          AND CefrLevel IS NOT NULL AND PositionInBand IS NOT NULL
          AND ContinuousCoordinate IS NOT NULL AND MeasuredAt IS NOT NULL AND ExpiresAt IS NOT NULL
          AND FailureReason IS NULL)
    OR (ResultStatus IN ('grading','not_measured','exempt_accommodated')
          AND CefrLevel IS NULL AND PositionInBand IS NULL AND ContinuousCoordinate IS NULL
          AND MeasuredAt IS NULL AND ExpiresAt IS NULL))," \
  "  CONSTRAINT CK_PSR_StatusCarriesItsShape CHECK (1 = 1),"

# 2. the projection triple, which is what makes the cut-score version load-bearing
run_patch "CK_PSR_ProjectionIsATriple" \
  "a projected score with no cut-score version was accepted" \
  "  CONSTRAINT CK_PSR_ProjectionIsATriple CHECK (
       (ProjectedBandCode IS NULL AND ProjectedScore IS NULL AND CutScoreTableVersion IS NULL)
    OR (ProjectedBandCode IS NOT NULL AND ProjectedScore IS NOT NULL AND CutScoreTableVersion IS NOT NULL
        AND ResultStatus IN ('measured','estimated_from_one_task')))," \
  "  CONSTRAINT CK_PSR_ProjectionIsATriple CHECK (1 = 1),"

# 3. the failure reason belongs to not_measured alone. Removing it is noticed first by the earlier
#    assertion that a not_measured row must CARRY one - which is the same rule read from the other side.
run_patch "CK_PSR_FailureOnlyWhenNotMeasured" \
  "a not_measured skill with no failure reason was accepted" \
  "  CONSTRAINT CK_PSR_FailureOnlyWhenNotMeasured CHECK (
       (ResultStatus = 'not_measured' AND FailureReason IS NOT NULL)
    OR (ResultStatus <> 'not_measured' AND FailureReason IS NULL))," \
  "  CONSTRAINT CK_PSR_FailureOnlyWhenNotMeasured CHECK (1 = 1),"

# 4. the single-writer ownership check
run_patch "trg_PlacementSkillResults_SingleWriter ownership check (removed)" \
  "PlacementService wrote a per-skill result AttemptEngine owns" \
  "  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN @touched t ON t.PlacementId = i.PlacementId AND t.SkillId = i.SkillId
    WHERE ISNULL(i.WrittenByService, '') <> @owner
       OR (EXISTS (SELECT 1 FROM deleted d
                   WHERE d.PlacementId = i.PlacementId AND d.SkillId = i.SkillId)
           AND NOT UPDATE(WrittenByService))
  )
    THROW 50076" \
  "  IF 1 = 0
    THROW 50076"

# 5. the UPDATE(WrittenByService) half, which a value-only guard omits
run_patch "the UPDATE(WrittenByService) statement check (per-skill result)" \
  "a second writer inherited the owner's name" \
  "       OR (EXISTS (SELECT 1 FROM deleted d
                   WHERE d.PlacementId = i.PlacementId AND d.SkillId = i.SkillId)
           AND NOT UPDATE(WrittenByService))" \
  ""

# 6. the completion guard, widened to count ROWS instead of measured statuses. This is the input the
#    ticket names: four not_measured rows is a placement that measured nothing.
run_patch "the completion guard (counts rows, not measured statuses)" \
  "a placement with four not_measured skills was marked done" \
  "        SELECT 1 FROM PlacementSkillResults r
        WHERE r.PlacementId = i.PlacementId
          AND r.ResultStatus IN ('measured','estimated_from_one_task'))" \
  "        SELECT 1 FROM PlacementSkillResults r
        WHERE r.PlacementId = i.PlacementId)"

# 7. the completion guard, removed entirely. The row-count test above it also fires - both are the same
#    rule, so either message is the right failure and the assertion below names the later one.
run_patch "trg_PlacementTests_CompletionIsEarned (removed)" \
  "a placement with four not_measured skills was marked done" \
  "  IF EXISTS (
    SELECT 1
    FROM inserted i
    WHERE i.Status = 'done'
      AND i.CompletionFailureReason IS NULL
      AND NOT EXISTS (
        SELECT 1 FROM PlacementSkillResults r
        WHERE r.PlacementId = i.PlacementId
          AND r.ResultStatus IN ('measured','estimated_from_one_task'))
  )
    THROW 50078" \
  "  IF 1 = 0
    THROW 50078"

# 8. the other direction: a done placement cannot lose its last measured row. With the trigger removed
#    the UPDATE succeeds and the test's own THROW reports it, which is the correct RED.
run_patch "trg_PlacementSkillResults_CompletionIsEarned (removed)" \
  "a done placement lost its last measured skill" \
  "      AND (EXISTS (SELECT 1 FROM inserted i WHERE i.PlacementId = p.PlacementId)
        OR EXISTS (SELECT 1 FROM deleted d WHERE d.PlacementId = p.PlacementId))" \
  "      AND 1 = 0"

# 9. the monotonic version, weakened to `<` so an equal version overwrites its own result
run_patch "the monotonic comparison (per-skill result, <= weakened to <)" \
  "an equal projection version overwrote the stored result" \
  "    WHERE i.ProjectionVersion <= t.WasVersion
  )
    THROW 50077" \
  "    WHERE i.ProjectionVersion < t.WasVersion
  )
    THROW 50077"

# 10. the registry's honest coverage count, which #212 moved from 4/2/1 to 5/2/1 and #206 to 6/2/1.
#     Demoting the new fact in the SCHEMA is what the count exists to catch: a table that is guarded but
#     not registered.
run_patch "the registry covers the per-skill result" \
  "is not registered as a covered fact owned by AttemptEngine" \
  "  ('placement_skill_result', 'AttemptEngine',      'covered',        'PlacementSkillResults'," \
  "  ('placement_skill_result', 'AttemptEngine',      'awaiting_table', NULL,"

# 11. the control: unpatched, the file is green, so every RED above means something
cp "$BACKUP" "$SCHEMA"
bash "$DIR/run-one.sh" "$TEST" >/dev/null 2>&1 \
  && echo "GREEN             unpatched schema (control: the file passes on its own)" \
  || echo "NOT GREEN         unpatched schema - the whole exercise is meaningless"
