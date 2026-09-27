SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'R',N'R','reading',1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,DisplayOrder) VALUES (@sk,@c,'reading','P5',N'IC',4,1); DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash,ActiveCertId) VALUES (@r,'u@x.com',0x00,@c); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal,DisplayOrder) VALUES (@c,'below_A1',N'Below A1',10,119,1), (@c,'A2',N'A2',225,549,3);
INSERT INTO PathTemplates (CertId,FromBandCode,ToBandCode,Name) VALUES (@c,'below_A1','A2',N'Starter'); DECLARE @tpl INT=SCOPE_IDENTITY();
INSERT INTO PathModules (TemplateId,SkillId,CertId,DisplayOrder,Title) VALUES (@tpl,@sk,@c,1,N'Reading'); DECLARE @mod INT=SCOPE_IDENTITY();
-- valid practice_section item (RefSectionId set, RefExamId null)
INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
  VALUES (@mod,@c,@tpl,'practice_section',@sec,NULL,2,1);
INSERT INTO LearnerPaths (UserId,CertId,TemplateId,CurrentBandCode,Status) VALUES (@u,@c,@tpl,'below_A1','active');
DECLARE @lp INT=SCOPE_IDENTITY();
PRINT 'valid path accepted';
-- reject (Review Focus #3): practice_section with NULL RefSectionId
BEGIN TRY
  INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
    VALUES (@mod,@c,@tpl,'practice_section',NULL,NULL,2,2);
  THROW 50000,'EXPECT_REJECT FAILED: practice_section without section accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected practice_section w/o section as expected';
END CATCH
-- reject (Review Focus #3): mock_exam carrying a RefSectionId
BEGIN TRY
  INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
    VALUES (@mod,@c,@tpl,'mock_exam',@sec,NULL,2,3);
  THROW 50000,'EXPECT_REJECT FAILED: mock_exam with section accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected mock_exam with section as expected';
END CATCH
-- reject (Review Focus #5): a second active path for the same (UserId,CertId)
BEGIN TRY
  INSERT INTO LearnerPaths (UserId,CertId,TemplateId,CurrentBandCode,Status) VALUES (@u,@c,@tpl,'below_A1','active');
  THROW 50000,'EXPECT_REJECT FAILED: second active path accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected second active path as expected';
END CATCH
-- cross-cert fixtures (IELTS) for M-1/M-2 leakage checks
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('IELTS',N'I',1); DECLARE @c2 INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c2,'SPK',N'Speaking','speaking',1); DECLARE @sk2 INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,DisplayOrder) VALUES (@sk2,@c2,'speaking','S1',N'Interview',NULL,1); DECLARE @sec2 INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c2,N'IELTS Exam','published',60); DECLARE @exam2 INT=SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal,DisplayOrder) VALUES (@c2,'below_A1',N'Below A1',10,119,1), (@c2,'A2',N'A2',225,549,3);
INSERT INTO PathTemplates (CertId,FromBandCode,ToBandCode,Name) VALUES (@c2,'below_A1','A2',N'IELTS Starter'); DECLARE @tpl2 INT=SCOPE_IDENTITY();
-- M-1: reject a PathModules row whose SkillId belongs to a different certificate than its template
BEGIN TRY
  INSERT INTO PathModules (TemplateId,SkillId,CertId,DisplayOrder,Title) VALUES (@tpl,@sk2,@c,2,N'Cross-cert module');
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert PathModules skill accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert PathModules skill as expected';
END CATCH
-- M-1: reject a PathModuleItems row whose RefSectionId belongs to a different certificate than its module
BEGIN TRY
  INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
    VALUES (@mod,@c,@tpl,'practice_section',@sec2,NULL,2,4);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert PathModuleItems section accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert PathModuleItems section as expected';
END CATCH
-- M-1: reject a PathModuleItems row whose RefExamId belongs to a different certificate than its module
BEGIN TRY
  INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
    VALUES (@mod,@c,@tpl,'mock_exam',NULL,@exam2,2,5);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert PathModuleItems exam accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert PathModuleItems exam as expected';
END CATCH
-- M-1: reject a LearnerPaths row whose CertId disagrees with its TemplateId's own certificate
BEGIN TRY
  INSERT INTO LearnerPaths (UserId,CertId,TemplateId,CurrentBandCode,Status) VALUES (@u,@c2,@tpl,'below_A1','active');
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert LearnerPaths template accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert LearnerPaths template as expected';
END CATCH
-- M-1: reject a LearnerPathSteps row whose PathModuleItemId belongs to a different template
INSERT INTO PathModules (TemplateId,SkillId,CertId,DisplayOrder,Title) VALUES (@tpl2,@sk2,@c2,1,N'IELTS Speaking'); DECLARE @mod2 INT=SCOPE_IDENTITY();
INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
  VALUES (@mod2,@c2,@tpl2,'practice_section',@sec2,NULL,2,1); DECLARE @item2 INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,PathModuleItemId,TemplateId,Status) VALUES (@lp,@item2,@tpl,'locked');
  THROW 50000,'EXPECT_REJECT FAILED: cross-template LearnerPathSteps item accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-template LearnerPathSteps item as expected';
END CATCH
-- M-2: reject a PlacementTests row whose ExamId belongs to a different certificate
BEGIN TRY
  INSERT INTO PlacementTests (UserId,CertId,ExamId,Status) VALUES (@u,@c,@exam2,'in_progress');
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert PlacementTests exam accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert PlacementTests exam as expected';
END CATCH
-- Low: IsPremium is derived from a premium_path ledger charge via vw_LearnerPathIsPremium,
-- not a free-standing column (the column was removed).
IF EXISTS (SELECT 1 FROM vw_LearnerPathIsPremium WHERE LearnerPathId=@lp AND IsPremium=1)
  THROW 50000,'EXPECT_REJECT FAILED: path shows premium without a premium_path charge',1;
PRINT 'IsPremium view reports false without a premium_path charge, as expected';
