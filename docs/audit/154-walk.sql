# Browser walk evidence for #154.
#
# The demo learner's path was generated from the four-week planner (see 154-four-week-plan.png),
# then one step of each type was completed THROUGH THE RUNNING APP, and the row re-read from SQL.
#
#   step type     how it was completed                     writer
#   -----------   --------------------------------------   --------------------------------
#   practice      answer the last question of the session  PracticeService.SubmitAnswerAsync
#   lesson        view the last card of the lesson         LessonService.RecordCardViewAsync
#   mock_exam     sit and submit the mock exam             AttemptEngine.SubmitAttemptAsync
#
# Usage: sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -W -s"|" -i docs/audit/154-walk.sql
SET NOCOUNT ON;

DECLARE @u INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @p INT = (SELECT MAX(LearnerPathId) FROM LearnerPaths WHERE UserId = @u AND Status = 'active');

SELECT 'BEFORE' AS Phase,
       s.WeekNumber, s.Status, i.ItemType,
       s.EstimatedMinutes, s.TargetAccuracyPct, s.ActualAccuracyPct, s.Score
FROM LearnerPathSteps s
JOIN PathModuleItems i ON i.ItemId = s.PathModuleItemId
WHERE s.LearnerPathId = @p
ORDER BY s.StepId;

-- One step of each type, as the app leaves it after the activity.
SELECT 'WRITERS' AS Phase,
       i.ItemType,
       COUNT(*) AS Steps,
       SUM(CASE WHEN s.Status = 'done' THEN 1 ELSE 0 END) AS Done,
       SUM(CASE WHEN s.ActualAccuracyPct IS NOT NULL THEN 1 ELSE 0 END) AS WithAccuracy
FROM LearnerPathSteps s
JOIN PathModuleItems i ON i.ItemId = s.PathModuleItemId
WHERE s.LearnerPathId = @p
GROUP BY i.ItemType;
