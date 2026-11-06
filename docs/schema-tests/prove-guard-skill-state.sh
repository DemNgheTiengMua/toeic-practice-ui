#!/usr/bin/env bash
# docs/schema-tests/prove-guard-skill-state.sh — prove every guard in domain34-skill-state.sql is
# load-bearing.
#
# Run by hand when the per-skill state rules change:
#   bash docs/schema-tests/prove-guard-skill-state.sh
#
# For each guard it removes or weakens it in docs/database-schema.sql, re-runs domain34, checks the
# failure is the EXPECTED one, then restores the schema. A guard whose removal leaves domain34 green
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
TEST="$DIR/domain34-skill-state.sql"
DOMAIN27="$DIR/domain27-single-writer.sql"
BACKUP="$(mktemp)"; cp "$SCHEMA" "$BACKUP"
# domain27 owns the registry's arithmetic, so the two split assertions are proved against it. It is
# restored on exit for the same reason the schema is.
BACKUP27="$(mktemp)"; cp "$DOMAIN27" "$BACKUP27"
trap 'cp "$BACKUP" "$SCHEMA"; cp "$BACKUP27" "$DOMAIN27"; rm -f "$BACKUP" "$BACKUP27"' EXIT

run_patch () {  # $1 label, $2 expected failure, $3 old text, $4 new text, $5 test file, $6 file to patch
  local label="$1" expect="$2" old="$3" new="$4" target="${5:-$TEST}" patchfile="${6:-$SCHEMA}" log
  cp "$BACKUP" "$SCHEMA"
  cp "$BACKUP27" "$DOMAIN27"
  python - "$patchfile" "$old" "$new" <<'PY' || { echo "INVALID           $label  (patch target not found)"; return; }
import sys, io
path, old, new = sys.argv[1], sys.argv[2], sys.argv[3]
text = io.open(path, encoding='utf-8').read()
if old not in text: sys.exit(1)
io.open(path, 'w', encoding='utf-8', newline='').write(text.replace(old, new, 1))
PY
  log="$(mktemp)"
  bash "$DIR/run-one.sh" "$target" >"$log" 2>&1
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

# ---------------------------------------------------------------------------------------------
# The single-writer guard.
# ---------------------------------------------------------------------------------------------

# 1. the whole writer comparison. RETURN exits before it.
run_patch "trg_LearnerSkillStates_SingleWriter ownership check (removed)" \
  "CalibrationService wrote a row SkillStateWriter owns" \
  "  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN @touched t
      ON t.UserId = i.UserId AND t.CertId = i.CertId AND t.SkillId = i.SkillId
    WHERE ISNULL(i.WrittenByService, '') <> @owner
       OR (EXISTS (SELECT 1 FROM deleted d
                   WHERE d.UserId = i.UserId AND d.CertId = i.CertId AND d.SkillId = i.SkillId)
           AND NOT UPDATE(WrittenByService))
  )
    THROW 50077" \
  "  IF 1 = 0
    THROW 50077"

# 2. the UPDATE(WrittenByService) half. This is the one a value-only guard omits, and the test it
#    breaks is the inherited-name case: a second writer that changes only the level and position.
run_patch "the UPDATE(WrittenByService) statement check (skill state)" \
  "a second writer inherited the owner's name" \
  "       OR (EXISTS (SELECT 1 FROM deleted d
                   WHERE d.UserId = i.UserId AND d.CertId = i.CertId AND d.SkillId = i.SkillId)
           AND NOT UPDATE(WrittenByService))" \
  ""

# 3. the monotonic comparison, `<=` weakened to `<`. An equal version is a replay overwriting the state
#    it replays.
run_patch "the monotonic comparison (skill state, <= weakened to <)" \
  "an equal projection version overwrote the stored state" \
  "    WHERE i.ProjectionVersion <= t.WasVersion
  )
    THROW 50078" \
  "    WHERE i.ProjectionVersion < t.WasVersion
  )
    THROW 50078"

# 3b. `<=` replaced by `<>`. The naive "the version must have changed" operator, wrong in the opposite
#     direction: `1 <> 0` refuses the very first insert, so the owner cannot write its state at all.
#     The expectation is the raw error because this fires before any of the file's own PRINTs.
run_patch "the monotonic comparison (skill state, <= replaced by <>)" \
  "Msg 50078" \
  "    WHERE i.ProjectionVersion <= t.WasVersion
  )
    THROW 50078" \
  "    WHERE i.ProjectionVersion <> t.WasVersion
  )
    THROW 50078"

# 3c. the whole monotonic guard removed.
run_patch "the monotonic guard (skill state, removed)" \
  "an equal projection version overwrote the stored state" \
  "  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN @touched t
      ON t.UserId = i.UserId AND t.CertId = i.CertId AND t.SkillId = i.SkillId
    WHERE i.ProjectionVersion <= t.WasVersion
  )
    THROW 50078" \
  "  IF 1 = 0
    THROW 50078"

# 3d. the derived-fact predicate widened to EVERY update. A guard that refused a no-op save would look
#     right and break every idempotent write EF makes; the control test is what notices.
run_patch "the derived-fact predicate (widened to every update)" \
  "the guard blocked a write that derives nothing" \
  "    WHERE d.UserId IS NULL
       OR ISNULL(i.CefrLevel, '') <> ISNULL(d.CefrLevel, '')" \
  "    WHERE 1 = 1
       OR ISNULL(i.CefrLevel, '') <> ISNULL(d.CefrLevel, '')"

# ---------------------------------------------------------------------------------------------
# The shape, which carries four of the ticket's five facts.
# ---------------------------------------------------------------------------------------------

# 4. the level CHECK.
run_patch "CK_LSS_Level" \
  "an invented level code was accepted" \
  "  CefrLevel            VARCHAR(10) NOT NULL
    CONSTRAINT CK_LSS_Level CHECK (CefrLevel IN ('pre_a1','A1','A2','B1','B2','C1','C2'))," \
  "  CefrLevel            VARCHAR(10) NOT NULL,"

# 5. the position bound. Weakened to the plausible one-sided CHECK a careless implementation writes.
run_patch "CK_LSS_Position (both bounds weakened to >= 0)" \
  "a position of 101 inside a 0-100 band was accepted" \
  "    CONSTRAINT CK_LSS_Position CHECK (PositionInBand BETWEEN 0 AND 100)," \
  "    CONSTRAINT CK_LSS_Position CHECK (PositionInBand >= 0),"

# 6. the coordinate bound, weakened to the one-sided rule a careless implementation writes.
run_patch "CK_LSS_Coordinate (upper bound removed)" \
  "a coordinate above the top of the scale was accepted" \
  "    CONSTRAINT CK_LSS_Coordinate CHECK (ContinuousCoordinate BETWEEN 1.00 AND 6.99)," \
  "    CONSTRAINT CK_LSS_Coordinate CHECK (ContinuousCoordinate > 0),"

# 7. the source vocabulary, weakened to "there is some text".
run_patch "CK_LSS_Source (closed vocabulary widened to any non-empty string)" \
  "an invented measurement source was accepted" \
  "    CONSTRAINT CK_LSS_Source CHECK (Source IN ('placement','mock_section','calibration'))," \
  "    CONSTRAINT CK_LSS_Source CHECK (LEN(Source) > 0),"

# 8. the expiry rule, weakened to the plausible "merely later" a careless implementation writes.
run_patch "CK_LSS_Expiry (exact 90-day window weakened to 'later than MeasuredAt')" \
  "a 91-day expiry was accepted" \
  "  CONSTRAINT CK_LSS_Expiry CHECK (ExpiresAt = DATEADD(DAY, 90, MeasuredAt))," \
  "  CONSTRAINT CK_LSS_Expiry CHECK (ExpiresAt > MeasuredAt),"

# 9. the composite isolation key, replaced by the single-column one. This is the shape that lets one
#    certificate's skill be recorded under another's.
run_patch "FK_LSS_SkillCert (composite key reduced to SkillId alone)" \
  "another certificate's skill was storable in this one" \
  "  CONSTRAINT FK_LSS_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId)" \
  "  CONSTRAINT FK_LSS_SkillCert FOREIGN KEY (SkillId) REFERENCES Skills(SkillId)"

# ---------------------------------------------------------------------------------------------
# The registry, which is the only place coverage is stated.
# ---------------------------------------------------------------------------------------------

# 10. the primary key. Two things catch this and the shape assertion runs first: the file refuses to
#     continue when the key is not (UserId, CertId, SkillId), so that is the expected failure. The
#     duplicate-row fixture behind it is the second half - it is what would catch a key reduced in a
#     way that still has three columns.
run_patch "PK_LearnerSkillStates (one row per learner per skill widened)" \
  "the key of LearnerSkillStates is not (UserId, CertId, SkillId)" \
  "  CONSTRAINT PK_LearnerSkillStates PRIMARY KEY (UserId, CertId, SkillId)," \
  "  CONSTRAINT PK_LearnerSkillStates PRIMARY KEY (UserId, CertId, SkillId, MeasuredAt),"

# 10b. A STORED OVERALL. Two names, because the obvious guard is `LIKE '%overall%'` and it misses the
#      second one entirely. The exact-column-set assertion is what catches both, and these two patches
#      are the evidence: if it were the LIKE search, `AggregateLevel` would sail through.
run_patch "the no-overall assertion (a stored overall named OverallLevel)" \
  "LearnerSkillStates carries an extra or missing column (OverallLevel extra)" \
  "  ProjectionVersion    INT NOT NULL CONSTRAINT DF_LSS_ProjectionVersion DEFAULT 0," \
  "  ProjectionVersion    INT NOT NULL CONSTRAINT DF_LSS_ProjectionVersion DEFAULT 0,
  OverallLevel         VARCHAR(10) NULL,"

run_patch "the no-overall assertion (a stored overall named AggregateLevel, which LIKE '%overall%' misses)" \
  "LearnerSkillStates carries an extra or missing column (AggregateLevel extra)" \
  "  ProjectionVersion    INT NOT NULL CONSTRAINT DF_LSS_ProjectionVersion DEFAULT 0," \
  "  ProjectionVersion    INT NOT NULL CONSTRAINT DF_LSS_ProjectionVersion DEFAULT 0,
  AggregateLevel       VARCHAR(10) NULL,"

# ---------------------------------------------------------------------------------------------
# The registry and its split. These belong to domain27, which owns the registry's arithmetic; they are
# here because #211 is what moved the split from 3/3/1 to 4/2/1 and a future move has to be deliberate.
# ---------------------------------------------------------------------------------------------

# 11. the registry row left at awaiting_table while the table exists. The per-fact coverage comparison
#     in domain27 sees it, and so does the covered count.
run_patch "the registry covering learner_skill_state" \
  "the registry does not name the seven facts with their real coverage" \
  "  ('learner_skill_state',    'SkillStateWriter',   'covered',        'LearnerSkillStates'," \
  "  ('learner_skill_state',    'SkillStateWriter',   'awaiting_table', NULL," \
  "$DOMAIN27"

# 12. the covered count. A fact that was covered without the count moving is the registry claiming one
#     thing and doing another; this is the assertion that pins 4/2/1 rather than "seven".
run_patch "the 4/2/1 split in domain27 (covered count pinned at 4)" \
  "the covered set is not the four tables that exist today" \
  "IF (SELECT COUNT(*) FROM DerivedFactOwners WHERE Coverage = 'covered') <> 4" \
  "IF (SELECT COUNT(*) FROM DerivedFactOwners WHERE Coverage = 'covered') <> 3" \
  "$DOMAIN27" "$DOMAIN27"

# ---------------------------------------------------------------------------------------------
# The control: unpatched, the file is green, so every RED above means something.
# ---------------------------------------------------------------------------------------------
cp "$BACKUP" "$SCHEMA"
cp "$BACKUP27" "$DOMAIN27"
bash "$DIR/run-one.sh" "$TEST" >/dev/null 2>&1 \
  && bash "$DIR/run-one.sh" "$DOMAIN27" >/dev/null 2>&1 \
  && echo "GREEN             unpatched schema (control: both files pass on their own)" \
  || echo "NOT GREEN         unpatched schema - the whole exercise is meaningless"
