-- Clears one learner's placement history so a walk can be repeated.
-- CreditTransactions is append-only by design; a demo re-run must not mint ledger rows,
-- so this only clears attempts/answers/placements, never the ledger.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @userId INT = $(UserId);

DELETE FROM AttemptAnswers
WHERE AttemptId IN (SELECT AttemptId FROM ExamAttempts WHERE UserId = @userId);

DELETE FROM AttemptSkillScores
WHERE AttemptId IN (SELECT AttemptId FROM ExamAttempts WHERE UserId = @userId);

DELETE FROM AiGradings
WHERE FreeResponseId IN (
  SELECT fr.FreeResponseId FROM FreeResponses fr
  JOIN ExamAttempts a ON a.AttemptId = fr.AttemptId
  WHERE a.UserId = @userId);

DELETE FROM FreeResponses
WHERE AttemptId IN (SELECT AttemptId FROM ExamAttempts WHERE UserId = @userId);

DELETE FROM PlacementTests WHERE UserId = @userId;

DELETE FROM ExamAttempts WHERE UserId = @userId;

SELECT CONCAT('placements left = ', (SELECT COUNT(*) FROM PlacementTests WHERE UserId = @userId),
              ', attempts left = ', (SELECT COUNT(*) FROM ExamAttempts WHERE UserId = @userId)) AS cleared;
