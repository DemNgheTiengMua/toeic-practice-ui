SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'R',N'R','reading',1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,DisplayOrder) VALUES (@sk,@c,'reading','P5',N'IC',4,1); DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash,ActiveCertId) VALUES (@r,'u@x.com',0x00,@c); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal,DisplayOrder) VALUES (@c,'below_A1',N'Below A1',10,119,1), (@c,'A2',N'A2',225,549,3);
INSERT INTO PathTemplates (CertId,FromBandCode,ToBandCode,Name) VALUES (@c,'below_A1','A2',N'Starter'); DECLARE @tpl INT=SCOPE_IDENTITY();
INSERT INTO PathModules (TemplateId,SkillId,DisplayOrder,Title) VALUES (@tpl,@sk,1,N'Reading'); DECLARE @mod INT=SCOPE_IDENTITY();
-- valid practice_section item (RefSectionId set, RefExamId null)
INSERT INTO PathModuleItems (ModuleId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
  VALUES (@mod,'practice_section',@sec,NULL,2,1);
INSERT INTO LearnerPaths (UserId,CertId,TemplateId,CurrentBandCode,Status) VALUES (@u,@c,@tpl,'below_A1','active');
PRINT 'valid path accepted';
-- reject (Review Focus #3): practice_section with NULL RefSectionId
BEGIN TRY
  INSERT INTO PathModuleItems (ModuleId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
    VALUES (@mod,'practice_section',NULL,NULL,2,2);
  THROW 50000,'EXPECT_REJECT FAILED: practice_section without section accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected practice_section w/o section as expected';
END CATCH
-- reject (Review Focus #3): mock_exam carrying a RefSectionId
BEGIN TRY
  INSERT INTO PathModuleItems (ModuleId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
    VALUES (@mod,'mock_exam',@sec,NULL,2,3);
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
