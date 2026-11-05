#!/usr/bin/env bash
# docs/schema-tests/prove-guard.sh — prove every guard in domain22-pool.sql is load-bearing.
#
# Run by hand when #184's constraints change:   bash docs/schema-tests/prove-guard.sh
# For each guard it disables the guard in docs/database-schema.sql, re-runs domain22, checks the
# failure is the expected one, then restores the schema. A guard whose removal leaves domain22 green
# was not tested; a guard whose "removal" broke the schema proves nothing at all.
#
# Deliberately NOT part of run-all.sh: it rewrites the schema in place, and a harness that does that
# on every CI run is one interrupted job away from a truncated schema in the working tree.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCHEMA="$DIR/../database-schema.sql"
BACKUP="$(mktemp)"; cp "$SCHEMA" "$BACKUP"
trap 'cp "$BACKUP" "$SCHEMA"; rm -f "$BACKUP"' EXIT

run_patch () {  # $1 label, $2 text to replace, $3 replacement, $4 file (default: the schema)
  local label="$1" old="$2" new="$3" file="${4:-$SCHEMA}" log
  cp "$BACKUP" "$SCHEMA"
  python - "$file" "$old" "$new" <<'PY' || { echo "INVALID           $label  (patch target not found)"; return; }
import sys, io
path, old, new = sys.argv[1], sys.argv[2], sys.argv[3]
text = io.open(path, encoding='utf-8').read()
if old not in text: sys.exit(1)
io.open(path, 'w', encoding='utf-8', newline='').write(text.replace(old, new, 1))
PY
  log="$(mktemp)"
  bash "$DIR/run-one.sh" "$DIR/domain22-pool.sql" >"$log" 2>&1
  if [ $? -eq 0 ]; then
    echo "NOT LOAD-BEARING  $label  (domain22 still passed with the guard removed)"
  elif grep -q 'Incorrect syntax\|Invalid object name\|Invalid column name\|Invalid constraint' "$log"; then
    echo "INVALID           $label  (the patched schema did not compile, so the failure proves nothing)"
  elif grep -q 'was accepted' "$log"; then
    # The guard is gone, so the statement it used to refuse succeeded and the test's own positive
    # assertion fired. This is the verdict that proves the guard was doing the work.
    echo "RED               $label  -> $(grep -o 'EXPECT_REJECT FAILED[^,]*' "$log" | head -1)"
  elif grep -q 'refused by the wrong guard' "$log"; then
    # The guard is gone but something else refused the statement: the assertion names a guard that was
    # never exercised. This is the failure mode lib.sql exists to end.
    echo "DIFFERENT GUARD   $label  -> $(grep -o 'refused by the wrong guard.*' "$log" | head -1)"
  elif grep -q 'EXPECT_REJECT FAILED' "$log"; then
    # Any other EXPECT_REJECT FAILED is the test's own assertion firing because the guard it was
    # checking is gone: the statement succeeded, or a row the file requires is absent.
    echo "RED               $label  -> $(grep -o 'EXPECT_REJECT FAILED[^,]*' "$log" | head -1)"
  else
    echo "UNEXPECTED        $label  -> $(grep -oE 'Msg [0-9]+.*|EXPECT_REJECT FAILED.*' "$log" | head -1)"
  fi
  rm -f "$log"
}

# 1. copy-on-write trigger. RETURN exits the trigger before the predicate: patching one clause of the
#    OR chain would leave the others live (AND binds tighter than OR), which is how a "removed" guard
#    silently proves nothing.
run_patch "trg_ItemVersions_ScoredFieldsImmutable" \
  "ON ItemVersions AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;" \
  "ON ItemVersions AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF 1 = 1 RETURN;"

# 2. an item's level cannot be the reportable floor
run_patch "CK_Items_Level (pre_a1)" \
  "CONSTRAINT CK_Items_Level CHECK (CefrLevel IN ('A1','A2','B1','B2','C1','C2'))" \
  "CONSTRAINT CK_Items_Level CHECK (CefrLevel IN ('A1','A2','B1','B2','C1','C2','pre_a1'))"

# 3. a stimulus's level is a level that exists
run_patch "CK_Stimuli_Level (C3)" \
  "CONSTRAINT CK_Stimuli_Level CHECK (CefrLevel IN ('A1','A2','B1','B2','C1','C2'))" \
  "CONSTRAINT CK_Stimuli_Level CHECK (CefrLevel IN ('A1','A2','B1','B2','C1','C2','C3'))"

# 4. an item's skill is its topic's skill
run_patch "FK_Items_TopicModality" \
  "  CONSTRAINT FK_Items_TopicModality FOREIGN KEY (TopicId, Modality) REFERENCES Topics(TopicId, Modality)," \
  ""

# 5. four options and a key, or neither
run_patch "CK_ItemVersions_OptionsComplete" \
  "  CONSTRAINT CK_ItemVersions_OptionsComplete CHECK (
       (OptionA IS NULL AND OptionB IS NULL AND OptionC IS NULL AND OptionD IS NULL AND KeyLabel IS NULL)
    OR (OptionA IS NOT NULL AND OptionB IS NOT NULL AND OptionC IS NOT NULL AND OptionD IS NOT NULL
        AND KeyLabel IS NOT NULL)
  )," \
  "  CONSTRAINT CK_ItemVersions_OptionsComplete CHECK (1 = 1),"

# 6. the key is one of four labels
run_patch "CK_ItemVersions_KeyLabel (E)" \
  "CONSTRAINT CK_ItemVersions_KeyLabel CHECK (KeyLabel IN ('A','B','C','D'))," \
  "CONSTRAINT CK_ItemVersions_KeyLabel CHECK (KeyLabel IN ('A','B','C','D','E')),"

# 7. an image carries alt text
run_patch "CK_Stimuli_ImageAlt" \
  "CONSTRAINT CK_Stimuli_ImageAlt CHECK (ImagePath IS NULL OR ImageAlt IS NOT NULL)" \
  "CONSTRAINT CK_Stimuli_ImageAlt CHECK (1 = 1)"

# 8. the domain context is one of three
run_patch "CK_Stimuli_Domain (military)" \
  "CONSTRAINT CK_Stimuli_Domain CHECK (DomainContext IN ('workplace','academic','general'))," \
  "CONSTRAINT CK_Stimuli_Domain CHECK (DomainContext IN ('workplace','academic','general','military')),"

# 9. the lifecycle states are the six
run_patch "CK_Items_Lifecycle (approved)" \
  "('draft','validated','active','quarantined','retired','retired_defective'))," \
  "('draft','validated','active','quarantined','retired','retired_defective','approved')),"

# 10. the content tier is production or demo
run_patch "CK_Items_ContentTier (pilot)" \
  "CONSTRAINT CK_Items_ContentTier CHECK (ContentTier IN ('production','demo'))," \
  "CONSTRAINT CK_Items_ContentTier CHECK (ContentTier IN ('production','demo','pilot')),"

# 11. the item type vocabulary is the fourteen, mapped to their own modality
run_patch "CK_Items_Modality_Type" \
  "  CONSTRAINT CK_Items_Modality_Type CHECK (
       (Modality = 'listening' AND ItemType IN ('short_exchange','extended_dialogue','monologue_talk'))
    OR (Modality = 'reading'   AND ItemType IN ('sentence_cloze','passage_comprehension','text_insertion_cohesion'))
    OR (Modality = 'speaking'  AND ItemType IN ('descriptive_monologue','situational_negotiation','argumentative_monologue','visual_data_synthesis'))
    OR (Modality = 'writing'   AND ItemType IN ('transactional_correspondence','visual_data_synthesis','persuasive_essay','analytical_evaluation_essay'))
  )" \
  "  CONSTRAINT CK_Items_Modality_Type CHECK (1 = 1)"

# 12. a version belongs to an item that exists
run_patch "FK_ItemVersions_Item" \
  "  ItemId         INT NOT NULL CONSTRAINT FK_ItemVersions_Item REFERENCES Items(ItemId)," \
  "  ItemId         INT NOT NULL,"

# 13. the canonical topic codes, not merely twenty rows. Swapping one code for a plausible one is
#     the mistake a count-based assertion accepts. The list lives in the test file, not the schema.
TESTFILE="$DIR/domain22-pool.sql"
TESTBACKUP="$(mktemp)"; cp "$TESTFILE" "$TESTBACKUP"
run_patch "canonical topic codes (a wrong twenty: L-ORG -> L-MISC)" \
  "  ('listening','L-ORG', N'Discourse structure, signposting, synthesis')," \
  "  ('listening','L-MISC',N'Discourse structure, signposting, synthesis')," \
  "$TESTFILE"
cp "$TESTBACKUP" "$TESTFILE"; rm -f "$TESTBACKUP"

# 14. the control: with nothing patched the file is green, so every RED above means something
cp "$BACKUP" "$SCHEMA"
bash "$DIR/run-one.sh" "$DIR/domain22-pool.sql" >/dev/null 2>&1 \
  && echo "GREEN             unpatched schema (control: the file passes on its own)" \
  || echo "NOT GREEN         unpatched schema - the whole exercise is meaningless"
