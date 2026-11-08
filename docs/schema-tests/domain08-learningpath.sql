SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'R',N'R','reading',1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,ExpectedQuestionCount,DisplayOrder) VALUES (@sk,@c,'reading','P5',N'IC',4,30,1); DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash,ActiveCertId) VALUES (@r,'u@x.com',0x00,@c); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal,DisplayOrder) VALUES (@c,'below_A1',N'Below A1',10,119,1), (@c,'A2',N'A2',225,549,3);
INSERT INTO Topics (Modality,Code,Name) VALUES ('reading','R-DET',N'Detail'); DECLARE @tDet INT=SCOPE_IDENTITY();
INSERT INTO Topics (Modality,Code,Name) VALUES ('reading','R-GRAM',N'Grammar'); DECLARE @tGram INT=SCOPE_IDENTITY();
INSERT INTO Lessons (CertId,SkillId,Modality,TopicId,CefrLevel,Title,EstimatedMinutes,DisplayOrder) VALUES (@c,@sk,'reading',@tDet,'B1',N'Lesson',10,1); DECLARE @les INT=SCOPE_IDENTITY();
INSERT INTO LearnerPaths (UserId,CertId,CurrentBandCode,Status) VALUES (@u,@c,'below_A1','active');
DECLARE @lp INT=SCOPE_IDENTITY();
PRINT 'valid path accepted';

-- #232 / D10: THE STEP IS A COORDINATE. A step names its skill, topic, level, type and size; it no
-- longer points at a PathModuleItem. These checks are the coordinate's own constraints.

-- valid drill step: no resource at all, because the selector picks its items at run time
INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,WeekNumber,WeekSequence)
  VALUES (@lp,@c,@sk,'reading',@tDet,'B1','focus_practice','developing',1,1);
PRINT 'valid drill step accepted';

-- reject (CK_Steps_ResourceMatchesType): a drill that names a lesson is not a drill
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,RefLessonId)
    VALUES (@lp,@c,@sk,'reading',@tDet,'B1','focus_practice','developing',@les);
  THROW 50000,'EXPECT_REJECT FAILED: a drill naming a lesson was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected drill with a lesson as expected';
END CATCH

-- reject (CK_Steps_ResourceMatchesType): a lesson step with no lesson opens an empty reader
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier)
    VALUES (@lp,@c,@sk,'reading',@tDet,'B1','concept_lesson','developing');
  THROW 50000,'EXPECT_REJECT FAILED: a lesson step with no lesson was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected lesson step without a lesson as expected';
END CATCH

-- the valid lesson step, which the cross-cert checks below re-use
INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,RefLessonId)
  VALUES (@lp,@c,@sk,'reading',@tGram,'B1','concept_lesson','developing',@les);
PRINT 'valid lesson step accepted';

-- reject (CK_Steps_TargetCefr): the reportable floor is not an authorable level
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier)
    VALUES (@lp,@c,@sk,'reading',@tDet,'pre_a1','focus_practice','developing');
  THROW 50000,'EXPECT_REJECT FAILED: a step authored at pre_a1 was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected pre_a1 target level as expected';
END CATCH

-- reject (CK_Steps_SizingTier): D10 has exactly three tiers
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier)
    VALUES (@lp,@c,@sk,'reading',@tDet,'B1','focus_practice','enormous');
  THROW 50000,'EXPECT_REJECT FAILED: a fourth sizing tier was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a sizing tier D10 does not name as expected';
END CATCH

-- reject (CK_Steps_StepType): the six types D10 names, and no invented seventh
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier)
    VALUES (@lp,@c,@sk,'reading',@tDet,'B1','mentor_chat','developing');
  THROW 50000,'EXPECT_REJECT FAILED: an invented step type was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an invented step type as expected';
END CATCH

-- reject (FK_Steps_SkillModality): a step whose modality is not its skill's
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier)
    VALUES (@lp,@c,@sk,'listening',@tDet,'B1','focus_practice','developing');
  THROW 50000,'EXPECT_REJECT FAILED: a reading skill with listening modality was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a step whose modality is not its skill''s as expected';
END CATCH

-- reject (FK_Steps_TopicModality): a listening step on a reading topic
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier)
    VALUES (@lp,@c,@sk,'listening',@tDet,'B1','focus_practice','developing');
  THROW 50000,'EXPECT_REJECT FAILED: a listening step on a reading topic was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a listening step on a reading topic as expected';
END CATCH

-- reject (Review Focus #5): a second active path for the same (UserId,CertId)
BEGIN TRY
  INSERT INTO LearnerPaths (UserId,CertId,CurrentBandCode,Status) VALUES (@u,@c,'below_A1','active');
  THROW 50000,'EXPECT_REJECT FAILED: second active path accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected second active path as expected';
END CATCH

-- cross-cert fixtures (IELTS) for M-1/M-2 leakage checks
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('IELTS',N'I',1); DECLARE @c2 INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c2,'SPK',N'Speaking','speaking',1); DECLARE @sk2 INT=SCOPE_IDENTITY();
-- OptionCount is NOT NULL (2026-10-26), 2..6; 4 is what the certificate wizard supplies.
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,ExpectedQuestionCount,DisplayOrder) VALUES (@sk2,@c2,'speaking','S1',N'Interview',4,3,1); DECLARE @sec2 INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c2,N'IELTS Exam','published',60); DECLARE @exam2 INT=SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal,DisplayOrder) VALUES (@c2,'below_A1',N'Below A1',10,119,1), (@c2,'A2',N'A2',225,549,3);
INSERT INTO Topics (Modality,Code,Name) VALUES ('speaking','S-DESC',N'Describe'); DECLARE @tDesc INT=SCOPE_IDENTITY();
INSERT INTO Lessons (CertId,SkillId,Modality,TopicId,CefrLevel,Title,EstimatedMinutes,DisplayOrder) VALUES (@c2,@sk2,'speaking',@tDesc,'B1',N'IELTS Lesson',10,1); DECLARE @les2 INT=SCOPE_IDENTITY();

-- M-1: reject a step whose CertId is not its path's own. The path is this certificate's; the skill and
-- topic below belong to the other one, so every carried-down key disagrees at once.
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier)
    VALUES (@lp,@c2,@sk2,'speaking',@tDesc,'B1','focus_practice','developing');
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert LearnerPathSteps accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert LearnerPathSteps as expected';
END CATCH

-- M-1: reject a lesson step whose lesson belongs to a different certificate
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,RefLessonId)
    VALUES (@lp,@c,@sk,'reading',@tGram,'B1','concept_lesson','developing',@les2);
  THROW 50000,'EXPECT_REJECT FAILED: a cross-cert lesson reference was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a cross-cert lesson reference as expected';
END CATCH

-- M-2: reject a PlacementTests row whose ExamId belongs to a different certificate
BEGIN TRY
  INSERT INTO PlacementTests (UserId,CertId,ExamId,Status) VALUES (@u,@c,@exam2,'in_progress');
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert PlacementTests exam accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert PlacementTests exam as expected';
END CATCH
