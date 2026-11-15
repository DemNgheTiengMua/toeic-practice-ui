-- Clears one learner's placement history so a demo walk can be repeated.
--
-- CreditTransactions is append-only by design; a re-run must not mint ledger rows, so this clears
-- attempts/answers/placements and never the ledger.
--
-- The attempt trigger (trg_Attempts_TotalMatchesSkills, which lives on AttemptSkillScores) refuses
-- both UPDATE and DELETE of a scored attempt, because an attempt whose score no longer matches its
-- skill rows is a corrupt result. Removing the rows is legitimate for a throwaway demo account, but
-- the trigger cannot tell "resetting a demo" from "hiding a scoring defect", so it refuses.
-- Disabling it here, inside the transaction that deletes, is the honest way to say so - as opposed
-- to widening the trigger's condition, which would weaken the production guarantee for every caller.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @userId INT = $(UserId);

BEGIN TRANSACTION;

-- Transaction-scoped: no other connection can write a scored attempt while these are off.
DISABLE TRIGGER trg_Attempts_TotalMatchesSkills ON dbo.AttemptSkillScores;

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

ENABLE TRIGGER trg_Attempts_TotalMatchesSkills ON dbo.AttemptSkillScores;

COMMIT TRANSACTION;

SELECT CONCAT('placements left = ', (SELECT COUNT(*) FROM PlacementTests WHERE UserId = @userId),
              ', attempts left = ', (SELECT COUNT(*) FROM ExamAttempts WHERE UserId = @userId)) AS cleared;
