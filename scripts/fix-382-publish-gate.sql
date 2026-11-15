-- #382: makes every seeded free-response item publishable.
--
-- Three deficits, all measurable from the publish gate:
--   1. ItemVersions scored fields are copy-on-write (trigger trg_ItemVersions_ScoredFieldsImmutable),
--      so a missing PrepSeconds/ResponseSeconds/MinWords/MaxWords needs a NEW version row, then a
--      re-bind. Editing the old row is refused by design, not a workaround.
--   2. ExamItems requires LastWrittenBy on every write (trg_ExamItems_TriageCannotWrite).
--   3. SectionCriteria was empty, so every paper with a free-response task failed
--      Q_FR_CRITERIA_MISSING and could never be published.
--
-- Idempotent: re-running adds no second version and no duplicate criterion.

SET NOCOUNT ON;

-- ---- 1. Item 45 (placement speaking, interview) declares no prep window ------------------------
IF NOT EXISTS (SELECT 1 FROM ItemVersions WHERE ItemId = 45 AND VersionNumber = 2)
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, Explanation, MinWords, MaxWords,
                            PrepSeconds, ResponseSeconds, MaxSelections, AllowNumber, CreatedAt, LastWrittenBy)
  SELECT ItemId, 2, Stem, Explanation, MinWords, MaxWords, 30, 60, MaxSelections, AllowNumber,
         SYSDATETIMEOFFSET(), 1
  FROM ItemVersions WHERE ItemId = 45 AND VersionNumber = 1;

-- ---- 2. Item 51 (speaking practice, leisure) declares no prep window ---------------------------
IF NOT EXISTS (SELECT 1 FROM ItemVersions WHERE ItemId = 51 AND VersionNumber = 2)
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, Explanation, MinWords, MaxWords,
                            PrepSeconds, ResponseSeconds, MaxSelections, AllowNumber, CreatedAt, LastWrittenBy)
  SELECT ItemId, 2, Stem, Explanation, MinWords, MaxWords, 30, 60, MaxSelections, AllowNumber,
         SYSDATETIMEOFFSET(), 1
  FROM ItemVersions WHERE ItemId = 51 AND VersionNumber = 1;

-- ---- 3. Item 47 (placement writing) declares no response window --------------------------------
-- PrepSeconds stays NULL: CK_ItemVersions_Timing allows NULL or > 0, never 0.
IF NOT EXISTS (SELECT 1 FROM ItemVersions WHERE ItemId = 47 AND VersionNumber = 2)
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, Explanation, MinWords, MaxWords,
                            PrepSeconds, ResponseSeconds, MaxSelections, AllowNumber, CreatedAt, LastWrittenBy)
  SELECT ItemId, 2, Stem, Explanation,
         ISNULL(NULLIF(MinWords, 0), 150), ISNULL(NULLIF(MaxWords, 0), 220),
         NULL, 1200, MaxSelections, AllowNumber, SYSDATETIMEOFFSET(), 1
  FROM ItemVersions WHERE ItemId = 47 AND VersionNumber = 1;

-- ---- 4. Re-bind the papers to the versions that now carry a window -----------------------------
UPDATE ExamItems SET ItemVersionId = v.ItemVersionId, LastWrittenBy = 1
FROM ExamItems ei
JOIN ItemVersions v ON v.ItemId = ei.ItemId AND v.VersionNumber = 2
WHERE ei.ItemVersionId <> v.ItemVersionId
  AND ei.ItemId IN (45, 47, 51);

-- ---- 5. Speaking practice needs a third task: section INT declares 3 ---------------------------
IF NOT EXISTS (SELECT 1 FROM ExamItems WHERE ExamId = 8 AND SectionId = 20 AND DisplayOrder = 3)
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber, CreatedAt, LastWrittenBy)
  SELECT 8, 20, 2, v.ItemVersionId, i.ItemId, N'speaking', 3, 1, SYSDATETIMEOFFSET(), 1
  FROM Items i
  JOIN ItemVersions v ON v.ItemId = i.ItemId AND v.VersionNumber = 1
  WHERE i.ItemId = 52;

-- ---- 6. Every (writing|speaking) section carries its skill's rubric criteria --------------------
INSERT INTO SectionCriteria (SectionId, CriterionId, SkillId, MaxScore)
SELECT s.SectionId, c.CriterionId, c.SkillId, 9.00
FROM Sections s
JOIN GradingCriteria c ON c.SkillId = s.SkillId
WHERE s.CertId = 2
  AND s.Modality IN ('writing', 'speaking')
  AND NOT EXISTS (SELECT 1 FROM SectionCriteria sc WHERE sc.SectionId = s.SectionId AND sc.CriterionId = c.CriterionId);

SELECT CONCAT('SectionCriteria rows = ', COUNT(*)) FROM SectionCriteria;
SELECT CONCAT('papers carrying a free-response task with a window = ',
  (SELECT COUNT(*) FROM ExamItems ei JOIN ItemVersions v ON v.ItemVersionId = ei.ItemVersionId
   WHERE v.ResponseSeconds IS NULL)) AS still_missing_window;
