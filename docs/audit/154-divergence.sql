-- #154 evidence: two learners, same certificate, same band pair (B1 -> B2), same goal pace.
-- They differ only in which sections their practice history says are weakest, and the generated
-- paths drill different sections because of it.
SET NOCOUNT ON;

DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');

SELECT 'ACCURACY (weakest first)' AS Evidence;
SELECT u.Email AS Learner, s.Code AS Section, w.Answered, w.Correct,
       CAST(ROUND(100.0 * w.Correct / NULLIF(w.Answered, 0), 1) AS DECIMAL(5,1)) AS AccuracyPct
FROM vw_UserSectionAccuracyAll w
JOIN Sections s ON s.SectionId = w.SectionId
JOIN Users u ON u.UserId = w.UserId
WHERE w.UserId IN (SELECT UserId FROM Users WHERE Email IN ('learner@demo.local', 'learner.fresh@demo.local'))
ORDER BY Learner, AccuracyPct;

SELECT 'GOAL (identical band pair)' AS Evidence;
SELECT u.Email AS Learner, tb.CurrentBandCode, tb.TargetBandCode, tb.WeeklyStudyHours
FROM TargetBands tb JOIN Users u ON u.UserId = tb.UserId
WHERE u.Email IN ('learner@demo.local', 'learner.fresh@demo.local');

SELECT 'GENERATED PATHS (week, step type, what it drills)' AS Evidence;
SELECT u.Email AS Learner, s.WeekNumber, i.ItemType,
       COALESCE(sec.Code, les.Title, ex.Name) AS Target,
       s.EstimatedMinutes
FROM LearnerPathSteps s
JOIN LearnerPaths p ON p.LearnerPathId = s.LearnerPathId
JOIN Users u ON u.UserId = p.UserId
JOIN PathModuleItems i ON i.ItemId = s.PathModuleItemId
LEFT JOIN Sections sec ON sec.SectionId = i.RefSectionId
LEFT JOIN Lessons les ON les.LessonId = i.RefLessonId
LEFT JOIN Exams ex ON ex.ExamId = i.RefExamId
WHERE u.Email IN ('learner@demo.local', 'learner.fresh@demo.local')
  AND p.Status = 'active'
ORDER BY Learner, s.StepId;
