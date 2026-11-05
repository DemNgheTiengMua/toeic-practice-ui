#!/usr/bin/env bash
# docs/schema-tests/prove-guard-examitems.sh — prove every guard in domain23-examitems.sql is
# load-bearing.
#
# Run by hand when #185's constraints change:   bash docs/schema-tests/prove-guard-examitems.sh
# For each guard it removes it from docs/database-schema.sql, re-runs domain23, checks the failure is
# the expected one, then restores the schema. A guard whose removal leaves domain23 green was not
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

run_patch () {  # $1 label, $2 text to replace, $3 replacement
  local label="$1" old="$2" new="$3" log
  cp "$BACKUP" "$SCHEMA"
  python - "$SCHEMA" "$old" "$new" <<'PY' || { echo "INVALID           $label  (patch target not found)"; return; }
import sys, io
path, old, new = sys.argv[1], sys.argv[2], sys.argv[3]
text = io.open(path, encoding='utf-8').read()
if old not in text: sys.exit(1)
io.open(path, 'w', encoding='utf-8', newline='').write(text.replace(old, new, 1))
PY
  log="$(mktemp)"
  bash "$DIR/run-one.sh" "$DIR/domain23-examitems.sql" >"$log" 2>&1
  if [ $? -eq 0 ]; then
    echo "NOT LOAD-BEARING  $label  (domain23 still passed with the guard removed)"
  elif grep -q 'Incorrect syntax\|Invalid object name\|Invalid column name\|Invalid constraint\|Invalid object' "$log"; then
    echo "INVALID           $label  (the patched schema did not compile, so the failure proves nothing)"
  elif grep -q 'was accepted\|was created\|left a row behind' "$log"; then
    # The guard is gone, so the statement it used to refuse succeeded and the test's own positive
    # assertion fired. This is the verdict that proves the guard was doing the work.
    echo "RED               $label  -> $(grep -o 'EXPECT_REJECT FAILED[^,]*' "$log" | head -1)"
  elif grep -q 'refused by the wrong guard' "$log"; then
    # The guard is gone but something else refused the statement: the assertion names a guard that was
    # never exercised. This is the failure mode lib.sql exists to end.
    echo "DIFFERENT GUARD   $label  -> $(grep -o 'refused by the wrong guard.*' "$log" | head -1)"
  elif grep -q 'EXPECT_REJECT FAILED' "$log"; then
    echo "RED               $label  -> $(grep -o 'EXPECT_REJECT FAILED[^,]*' "$log" | head -1)"
  else
    echo "UNEXPECTED        $label  -> $(grep -oE 'Msg [0-9]+.*|EXPECT_REJECT FAILED.*' "$log" | head -1)"
  fi
  rm -f "$log"
}

# 1. INV-6 trigger. RETURN exits the trigger before the predicate: patching part of the statement
#    would leave it live and prove nothing.
run_patch "trg_ExamItems_ProductionBindsProduction" \
  "ON ExamItems AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;" \
  "ON ExamItems AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF 1 = 1 RETURN;"

# 1b. the same rule from the exam side. Without it, promoting a demo-bound exam slips INV-6 through.
run_patch "trg_Exams_TierKeepsContent (promotion)" \
  "ON Exams AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted e
    JOIN ExamItems b ON b.ExamId = e.ExamId" \
  "ON Exams AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF 1 = 1 RETURN;
  IF EXISTS (
    SELECT 1
    FROM inserted e
    JOIN ExamItems b ON b.ExamId = e.ExamId"

# 2. certificate isolation, section side: drop the section FK's CertId half by retargeting it to a
#    single column, which keeps the constraint name in the error message.
run_patch "FK_ExamItems_SectionCert (cross-certificate section)" \
  "  CONSTRAINT FK_ExamItems_SectionCert  FOREIGN KEY (SectionId, CertId) REFERENCES Sections(SectionId, CertId)," \
  "  CONSTRAINT FK_ExamItems_SectionCert  FOREIGN KEY (SectionId) REFERENCES Sections(SectionId),"

# 3. certificate isolation, exam side.
run_patch "FK_ExamItems_ExamCert (wrong-cert exam)" \
  "  CONSTRAINT FK_ExamItems_ExamCert     FOREIGN KEY (ExamId, CertId)    REFERENCES Exams(ExamId, CertId)," \
  "  CONSTRAINT FK_ExamItems_ExamCert     FOREIGN KEY (ExamId)    REFERENCES Exams(ExamId),"

# 4. the skill rule, section side.
run_patch "FK_ExamItems_SectionSkill (cross-skill section)" \
  "  CONSTRAINT FK_ExamItems_SectionSkill FOREIGN KEY (SectionId, Modality) REFERENCES Sections(SectionId, Modality)," \
  "  CONSTRAINT FK_ExamItems_SectionSkill CHECK (1 = 1),"

# 5. the skill rule, item side.
run_patch "FK_ExamItems_ItemSkill (mislabelled item)" \
  "  CONSTRAINT FK_ExamItems_ItemSkill    FOREIGN KEY (ItemId, Modality)    REFERENCES Items(ItemId, Modality)," \
  "  CONSTRAINT FK_ExamItems_ItemSkill    CHECK (1 = 1),"

# 6. the pinned version belongs to the bound item.
run_patch "FK_ExamItems_VersionItem (another item's version)" \
  "  CONSTRAINT FK_ExamItems_VersionItem  FOREIGN KEY (ItemVersionId, ItemId)
    REFERENCES ItemVersions(ItemVersionId, ItemId)," \
  "  CONSTRAINT FK_ExamItems_VersionItem  CHECK (1 = 1),"

# 7. one position per section.
run_patch "UQ_ExamItems_Exam_Section_Order (duplicate position)" \
  "  CONSTRAINT UQ_ExamItems_Exam_Section_Order UNIQUE (ExamId, SectionId, DisplayOrder)" \
  "  CONSTRAINT UQ_ExamItems_Exam_Section_Order UNIQUE (ExamId, SectionId, DisplayOrder, ItemId)"

# 8. position is positive.
run_patch "CK_ExamItems_DisplayOrder (position 0)" \
  "CONSTRAINT CK_ExamItems_DisplayOrder CHECK (DisplayOrder > 0)" \
  "CONSTRAINT CK_ExamItems_DisplayOrder CHECK (DisplayOrder >= 0)"

# 9. unit is positive.
run_patch "CK_ExamItems_UnitNumber (unit 0)" \
  "CONSTRAINT CK_ExamItems_UnitNumber CHECK (UnitNumber > 0)" \
  "CONSTRAINT CK_ExamItems_UnitNumber CHECK (UnitNumber >= 0)"

# 10. the control: with nothing patched the file is green, so every RED above means something
cp "$BACKUP" "$SCHEMA"
bash "$DIR/run-one.sh" "$DIR/domain23-examitems.sql" >/dev/null 2>&1 \
  && echo "GREEN             unpatched schema (control: the file passes on its own)" \
  || echo "NOT GREEN         unpatched schema - the whole exercise is meaningless"
