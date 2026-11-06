#!/usr/bin/env bash
# docs/schema-tests/prove-guard-triage-selfapproval.sh — prove every guard in domain35 is load-bearing.
#
# Run by hand when the two invariants change:
#   bash docs/schema-tests/prove-guard-triage-selfapproval.sh
#
# For each guard it removes or weakens it in docs/database-schema.sql, re-runs domain35, checks the
# failure is the EXPECTED one, then restores the schema. A guard whose removal leaves domain35 green
# was not tested. A patch whose replacement did not compile proves nothing either, so that is reported
# separately.
#
# Deliberately NOT part of run-all.sh, for the reason prove-guard.sh states: it rewrites the schema in
# place, and a harness that does that on every CI run is one interrupted job away from a truncated
# schema in the working tree. NEVER run this while dotnet test is going - DatabaseFixture loads the
# schema once per run and would load a patched copy.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCHEMA="$DIR/../database-schema.sql"
TEST="$DIR/domain35-triage-and-selfapproval.sql"
BACKUP="$(mktemp)"; cp "$SCHEMA" "$BACKUP"
trap 'cp "$BACKUP" "$SCHEMA"; rm -f "$BACKUP"' EXIT

run_patch () {  # $1 label, $2 expected failure, $3 old text, $4 new text
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
    echo "NOT LOAD-BEARING  $label  (the file still passed with the guard removed)"
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

# ================================================================================================
# INVARIANT 1: no self-approval
# ================================================================================================

# 1. the whole actor comparison. The trigger still exists and still refuses DELETE, so the expected
#    failure is the first self-approval the test reaches, not a syntax error.
run_patch "trg_ReviewSampleDecisions_NoSelfApproval actor check (removed)" \
  "the uploader claimed a sample for its own batch" \
  "  IF EXISTS (
    SELECT 1
    FROM inserted n
    JOIN ImportBatches b ON b.BatchId = n.BatchId" \
  "  IF 1 = 0 AND EXISTS (
    SELECT 1
    FROM inserted n
    JOIN ImportBatches b ON b.BatchId = n.BatchId"

# 2. ONLY the batch half. This is the rule the ticket's acceptance criterion would let you write, and
#    the one its "or last modified" clause exists to forbid: the colleague who revised the item is no
#    longer refused. The test that notices is the one about the newest version, not the uploader.
run_patch "the item-version half of the actor check (batch-only rule)" \
  "the account whose revision is the newest version claimed that item" \
  "       OR n.ActorUserId = (
            SELECT TOP 1 v.LastWrittenBy
            FROM ItemVersions v
            WHERE v.ItemId = n.ItemId
            ORDER BY v.VersionNumber DESC)" \
  ""

# 3. ONLY the item-version half. The uploader of a batch whose item somebody else wrote is no longer
#    refused, which is the reading that forgets ImportBatches.UploadedBy entirely. The first
#    self-approval that goes through is the 'clear' on batch2, whose item was written by the colleague.
run_patch "the batch half of the actor check (version-only rule)" \
  "the uploader cleared a sample on a batch it uploaded" \
  "    WHERE n.ActorUserId = b.UploadedBy
       OR n.ActorUserId = b.LastModifiedBy" \
  "    WHERE 1 = 0
       OR 1 = 0"

# 4. the ImportBatches.LastModifiedBy arm. The column exists and nothing writes it today, so this is
#    the only assertion that keeps it read; without it, "or last modified" at the batch level is a
#    column that is never consulted.
run_patch "ImportBatches.LastModifiedBy arm" \
  "the batch's last modifier claimed a sample for it" \
  "       OR n.ActorUserId = b.LastModifiedBy
" \
  ""

# 5. the DELETE refusal. Deleting the decision is how a self-approval is laundered: remove the row,
#    insert one made by nobody. With this arm gone, the delete succeeds and the test notices.
run_patch "the DELETE refusal on ReviewSampleDecisions" \
  "a review decision was deleted" \
  "  IF EXISTS (SELECT 1 FROM deleted)
    THROW 50080" \
  "  IF 1 = 0
    THROW 50080"

# ================================================================================================
# INVARIANT 2: triage cannot write, and the UPDATE half of each
# ================================================================================================

# 6-10. Each of the five tables. Removing one guard must fail on that table's own test, so the five
#       are proved separately rather than by removing a shared helper - a shared helper is what the
#       function is, and it is proved once below. Each patch is anchored on that table's own columns.
# Removing the whole ItemVersions guard means the FIRST refusal in the file to stop firing is review
# hole 1 (the undeclared-row UPDATE), not the later declared-triage insert. Either is a valid RED; the
# earlier one is what the run reports, so it is what is expected.
run_patch "trg_ItemVersions_TriageCannotWrite (removed)" \
  "triage edited item text on a row that names no author" \
  "    LEFT JOIN deleted d ON d.ItemVersionId = i.ItemVersionId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.ItemVersionId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)" \
  "    LEFT JOIN deleted d ON d.ItemVersionId = i.ItemVersionId
    WHERE 1 = 0 AND dbo.fn_IsTriageOnly(i.LastWrittenBy) = 0)"

run_patch "trg_ExamItems_TriageCannotWrite (removed)" \
  "triage bound an item into a paper" \
  "    LEFT JOIN deleted d ON d.ExamItemId = i.ExamItemId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.ExamItemId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)" \
  "    LEFT JOIN deleted d ON d.ExamItemId = i.ExamItemId
    WHERE 1 = 0 AND dbo.fn_IsTriageOnly(i.LastWrittenBy) = 0)"

run_patch "trg_Sections_TriageCannotWrite (removed)" \
  "triage added a section" \
  "    LEFT JOIN deleted d ON d.SectionId = i.SectionId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.SectionId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)" \
  "    LEFT JOIN deleted d ON d.SectionId = i.SectionId
    WHERE 1 = 0 AND dbo.fn_IsTriageOnly(i.LastWrittenBy) = 0)"

run_patch "trg_GradingCriteria_TriageCannotWrite (removed)" \
  "triage added a rubric criterion" \
  "    LEFT JOIN deleted d ON d.CriterionId = i.CriterionId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.CriterionId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)" \
  "    LEFT JOIN deleted d ON d.CriterionId = i.CriterionId
    WHERE 1 = 0 AND dbo.fn_IsTriageOnly(i.LastWrittenBy) = 0)"

run_patch "trg_GradingCriterionDescriptors_TriageCannotWrite (removed)" \
  "triage added a rubric descriptor" \
  "    LEFT JOIN deleted d ON d.CriterionId = i.CriterionId AND d.Band = i.Band
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.CriterionId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)" \
  "    LEFT JOIN deleted d ON d.CriterionId = i.CriterionId AND d.Band = i.Band
    WHERE 1 = 0 AND dbo.fn_IsTriageOnly(i.LastWrittenBy) = 0)"

# 10a. THE DECLARATION REQUIREMENT ITSELF (review hole 1). Weakened to the first draft's rule: only
#      refuse an undeclared row when the stored row already named an author. A triage UPDATE of
#      Explanation on a seeded row then passes, which is exactly what the review reproduced.
run_patch "the undeclared-row refusal (weakened to 'only when the stored row names an author')" \
  "triage edited item text on a row that names no author" \
  "    LEFT JOIN deleted d ON d.ItemVersionId = i.ItemVersionId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.ItemVersionId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)" \
  "    LEFT JOIN deleted d ON d.ItemVersionId = i.ItemVersionId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (d.ItemVersionId IS NOT NULL AND d.LastWrittenBy IS NOT NULL AND NOT UPDATE(LastWrittenBy)))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)"

# 10b. THE INSERT HALF (review hole 2). The undeclared arm stops firing for an INSERT, so a triage
#      account can author a new version with no declaration. The UPDATE half is untouched, which is
#      why the expected failure is hole 2's own message and not hole 1's.
run_patch "the undeclared-INSERT arm (removed)" \
  "triage authored a new item version with no declaration" \
  "    LEFT JOIN deleted d ON d.ItemVersionId = i.ItemVersionId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.ItemVersionId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)" \
  "    LEFT JOIN deleted d ON d.ItemVersionId = i.ItemVersionId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (d.ItemVersionId IS NOT NULL AND NOT UPDATE(LastWrittenBy)))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)"

# 10c. THE EXEMPTION'S BOUNDARY. Widened so the fixture flag also skips the triage arm, which is what
#      the review's requirement 4 forbids ("cannot be reached by a triage account"). Strict-mode tests
#      still pass, so the failure is the fixture-boundary case itself - not a coincidence.
run_patch "the fixture exemption (widened to admit a triage account)" \
  "the fixture flag let a triage account author a version" \
  "       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)
    THROW 50081, 'ADM-03: support triage may inspect item text" \
  "       OR (dbo.fn_IsFixtureCatalog() = 0 AND dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1))
    THROW 50081, 'ADM-03: support triage may inspect item text"

# 11. the UPDATE(LastWrittenBy) half, on one table. This is the check a value-only guard omits: a
#     triage UPDATE that sets only Explanation inherits the previous author's name and passes. The
#     test it breaks is the in-place edit, not the insert.
run_patch "the UPDATE(LastWrittenBy) re-declaration half (item text)" \
  "triage edited an existing item version" \
  "                  OR (d.ItemVersionId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)" \
  "                  OR (d.ItemVersionId IS NOT NULL AND 1 = 0)))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)"

# 12. the helper itself, weakened to "anybody who holds triage". This is the reading D16 forbids in
#     the other direction: it would stop an account holding all five from authoring. The first write
#     it breaks is the author's own INSERT of a version, which is why that is the expectation: a raw
#     Msg 50081 means the guard fired on an AUTHOR, which is the control case failing.
run_patch "fn_IsTriageOnly (widened to 'holds triage', ignoring the other namespaces)" \
  "Msg 50081" \
  "  ) AND NOT EXISTS (" \
  "  ) OR 1 = 1 AND NOT EXISTS ("

# 13. the helper's author exclusion, removed. The uploader holds pool.content.author, so the widened
#     rule refuses the author's own writes; the accepting control catches it with the same refusal.
run_patch "fn_IsTriageOnly (author exclusion removed)" \
  "Msg 50081" \
  "      AND rp.PermissionCode IN ('pool.content.author','assessment.blueprint.design')
  ) THEN 1 ELSE 0 END;" \
  "      AND 1 = 0
  ) THEN 1 ELSE 0 END;"

# ================================================================================================
# The control: unpatched, the file is green, so every RED above means something.
# ================================================================================================
cp "$BACKUP" "$SCHEMA"
bash "$DIR/run-one.sh" "$TEST" >/dev/null 2>&1 \
  && echo "GREEN             unpatched schema (control: the file passes on its own)" \
  || echo "NOT GREEN         unpatched schema - the whole exercise is meaningless"
