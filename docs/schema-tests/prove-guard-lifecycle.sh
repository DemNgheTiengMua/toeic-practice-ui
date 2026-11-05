#!/usr/bin/env bash
# docs/schema-tests/prove-guard-lifecycle.sh — prove every guard in domain26-item-lifecycle.sql is
# load-bearing.
#
# Run by hand when #190's rules change:   bash docs/schema-tests/prove-guard-lifecycle.sh
# For each guard it removes it from docs/database-schema.sql, re-runs domain26, checks the failure is
# the expected one, then restores the schema. A guard whose removal leaves domain26 green was not
# tested; a guard whose "removal" broke the schema proves nothing at all.
#
# Deliberately NOT part of run-all.sh, for the reason prove-guard.sh states: it rewrites the schema in
# place, and a harness that does that on every CI run is one interrupted job away from a truncated
# schema in the working tree. NEVER run this while dotnet test is going - DatabaseFixture loads the
# schema once per run and would load a patched copy.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCHEMA="$DIR/../database-schema.sql"
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
  bash "$DIR/run-one.sh" "$DIR/domain26-item-lifecycle.sql" >"$log" 2>&1
  if [ $? -eq 0 ]; then
    echo "NOT LOAD-BEARING  $label  (domain26 still passed with the guard removed)"
  elif grep -q 'Incorrect syntax\|Invalid object name\|Invalid column name\|Invalid constraint\|Invalid object' "$log"; then
    echo "INVALID           $label  (the patched schema did not compile, so the failure proves nothing)"
  elif grep -qF "$expect" "$log"; then
    echo "RED               $label  -> $(grep -o 'EXPECT_REJECT FAILED[^,]*' "$log" | head -1)"
  elif [ "$mode" = "reason-only" ] && grep -qF 'FK_AttemptAnswers_VersionItem\|FK_PracticeAnswers_VersionItem' "$log"; then
    # The clause is gone. The delete is STILL refused, by the answer's own foreign key - so the clause
    # buys the reason rather than the block. The test notices exactly that: it demands the CNT-43
    # wording, finds a foreign-key error instead, and fails naming the rule that should have fired.
    # Reported separately so nobody reads a foreign key standing in for a rule as a proven rule.
    echo "RED (REASON ONLY) $label  -> the delete was still refused by the answer's foreign key; the test failed because the CNT-43 reason it asserts never fired"
  elif grep -q 'refused by the wrong guard' "$log"; then
    echo "DIFFERENT GUARD   $label  -> $(grep -o 'refused by the wrong guard.*' "$log" | head -1)"
  else
    echo "UNEXPECTED        $label  -> $(grep -oE 'Msg [0-9]+.*|EXPECT_REJECT FAILED.*' "$log" | head -1)"
  fi
  rm -f "$log"
}

# ---------------------------------------------------------------------------------------------
# The transition guard.
# ---------------------------------------------------------------------------------------------

# 1. the guard itself. RETURN exits before the predicate; patching one row of the VALUES table would
#    leave the rest live and prove nothing.
run_patch "trg_Items_LifecycleTransitions (removed)" \
  "the unnamed transition" \
  "ON Items AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN deleted d ON d.ItemId = i.ItemId
    WHERE i.LifecycleStatus <> d.LifecycleStatus" \
  "ON Items AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF 1 = 1 RETURN;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN deleted d ON d.ItemId = i.ItemId
    WHERE i.LifecycleStatus <> d.LifecycleStatus"

# 2. `draft -> retired`. This is the edge that keeps a referenced draft item disposable. Without it
#    the pair matrix rejects it and the row is stranded.
run_patch "the draft -> retired edge" \
  "the named transition draft -> retired was refused" \
  "          ('draft',       'retired')," \
  ""

# 3. `validated -> quarantined`. The zero-tolerance key rule quarantines the audited stratum, which is
#    this state, not the live one.
run_patch "the validated -> quarantined edge" \
  "the named transition validated -> quarantined was refused" \
  "          ('validated',   'quarantined')," \
  ""

# 4. `quarantined -> active`. Re-verification must be able to return an item to service; a
#    quarantined item that can only be retired makes the review pointless.
run_patch "the quarantined -> active edge" \
  "the named transition quarantined -> active was refused" \
  "          ('quarantined', 'active')," \
  ""

# 5. the state comparison. Without it the guard consults the pair table for every row, including the
#    one that is not moving, and CNT-46's bulk decommission stops being re-runnable.
run_patch "the i.LifecycleStatus <> d.LifecycleStatus comparison" \
  "re-writing the same state is not a transition" \
  "    WHERE i.LifecycleStatus <> d.LifecycleStatus
      AND NOT EXISTS (" \
  "    WHERE 1 = 1
      AND NOT EXISTS ("

# ---------------------------------------------------------------------------------------------
# The delete guard.
# ---------------------------------------------------------------------------------------------

# 6. the whole delete guard. With it gone the server's own default applies, so every state is
#    deletable and the state half of section 5 goes red.
run_patch "trg_Items_NoHardDelete (removed)" \
  "an item in state" \
  "ON Items INSTEAD OF DELETE AS
BEGIN
  SET NOCOUNT ON;" \
  "ON Items INSTEAD OF DELETE AS
BEGIN
  SET NOCOUNT ON;
  DELETE v FROM ItemVersions v JOIN deleted d ON d.ItemId = v.ItemId;
  DELETE i FROM Items i JOIN deleted d ON d.ItemId = i.ItemId;
  RETURN;"

# 7. the state clause alone. This is the guard the ticket's third criterion names, so removing it
#    must leave a non-draft item deletable.
run_patch "the left-draft clause" \
  "the guard accepted the delete of an item in state" \
  "  IF EXISTS (SELECT 1 FROM deleted d WHERE d.LifecycleStatus <> 'draft')
    THROW 50061, 'CNT-43: an item that has left draft cannot be deleted; retire it instead.', 1;" \
  ""

# 8. the learner-reference clause alone. A trigger that checked only the state would pass every test
#    above and still delete an item a learner answered, which is the half of CNT-43 that protects
#    history rather than the pool.
run_patch "the learner-reference clause" \
  "the answered draft item was refused by the wrong guard" \
  "  IF EXISTS (
    SELECT 1
    FROM deleted d
    WHERE EXISTS (SELECT 1 FROM ItemVersions v
                  JOIN AttemptAnswers aa ON aa.ItemVersionId = v.ItemVersionId
                  WHERE v.ItemId = d.ItemId)
       OR EXISTS (SELECT 1 FROM ItemVersions v
                  JOIN PracticeAnswers pa ON pa.ItemVersionId = v.ItemVersionId
                  WHERE v.ItemId = d.ItemId)
  )
    THROW 50062, 'CNT-43: an item a learner has answered cannot be deleted; retire it instead.', 1;" \
  "" \
  "reason-only"

# 9. the practice half of the learner-reference clause, on its own. The attempt half and the practice
#    half are two different learner records; a single EXISTS over one of them would leave the other
#    deletable, and every test above would still pass.
run_patch "the practice-reference half" \
  "the practised draft item was refused by the wrong guard" \
  "       OR EXISTS (SELECT 1 FROM ItemVersions v
                  JOIN PracticeAnswers pa ON pa.ItemVersionId = v.ItemVersionId
                  WHERE v.ItemId = d.ItemId)" \
  "" \
  "reason-only"

# 10. the cascade. INSTEAD OF must do the delete itself; without this the trigger refuses every
#     delete, which is the "a trigger that refuses every DELETE is wrong" trap.
run_patch "the cascade that makes a draft delete actually delete" \
  "a draft item could not be deleted" \
  "  DELETE v FROM ItemVersions v JOIN deleted d ON d.ItemId = v.ItemId;
  DELETE i FROM Items i JOIN deleted d ON d.ItemId = i.ItemId;" \
  "  RETURN;"
# 11. the control: with nothing patched the file is green, so every RED above means something
cp "$BACKUP" "$SCHEMA"
bash "$DIR/run-one.sh" "$DIR/domain26-item-lifecycle.sql" >/dev/null 2>&1 \
  && echo "GREEN             unpatched schema (control: the file passes on its own)" \
  || echo "NOT GREEN         unpatched schema - the whole exercise is meaningless"
