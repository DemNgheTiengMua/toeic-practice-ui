SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #152: Questions.SampleAnswerOrImageDesc + ReferenceKind are declared together, and the kind is a
-- closed set. The pair is what lets a Read Aloud question carry its script without a picture
-- deciding what that script means.
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'TOEIC',1);
DECLARE @c INT=(SELECT CertId FROM Certificates WHERE Code='TOEIC');
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'Speaking','speaking',1);
DECLARE @spk INT=(SELECT SkillId FROM Skills WHERE CertId=@c AND Code='SPK');
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount,DisplayOrder)
  VALUES (@spk,@c,'speaking','S1',N'Read aloud',2,1);
DECLARE @sec INT=(SELECT SectionId FROM Sections WHERE Code='S1');
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose)
  VALUES (@c,N'Speaking 1','published',20,'speaking_practice');
DECLARE @ex INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@ex,@c,@sec,1);
DECLARE @g INT=SCOPE_IDENTITY();

-- A question with no reference at all is free speech, and stays legal.
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder)
  VALUES (@g,@ex,@sec,@spk,N'Do you work or are you a student?','B1','free_response',1);
PRINT 'accepted a free-speech question with no reference';

-- A Read Aloud question carries its script and says so.
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder,SampleAnswerOrImageDesc,ReferenceKind)
  VALUES (@g,@ex,@sec,@spk,N'Read the announcement aloud.','B1','free_response',2,
          N'Attention all shoppers. The sale ends today at 9:00 PM.','script');
PRINT 'accepted a scripted question';

-- A picture description is the other legal kind.
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder,SampleAnswerOrImageDesc,ReferenceKind)
  VALUES (@g,@ex,@sec,@spk,N'Describe the picture.','B2','free_response',3,
          N'Two colleagues are talking across a desk.','image_desc');
PRINT 'accepted a picture description';

-- Text with no kind: nothing says whether it is a script or a caption, so it cannot be used.
BEGIN TRY
  INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder,SampleAnswerOrImageDesc)
    VALUES (@g,@ex,@sec,@spk,N'unclassified','B1','free_response',4,N'Some words with no kind.');
  THROW 50000,'EXPECT_REJECT FAILED: reference text with no kind accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected reference text with no kind, as expected';
END CATCH

-- A kind with no text is a declaration of nothing.
BEGIN TRY
  INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder,ReferenceKind)
    VALUES (@g,@ex,@sec,@spk,N'kind only','B1','free_response',5,'script');
  THROW 50000,'EXPECT_REJECT FAILED: reference kind with no text accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected reference kind with no text, as expected';
END CATCH

-- An unknown kind is not a kind.
BEGIN TRY
  INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder,SampleAnswerOrImageDesc,ReferenceKind)
    VALUES (@g,@ex,@sec,@spk,N'bad kind','B1','free_response',6,N'Some words.','essay');
  THROW 50000,'EXPECT_REJECT FAILED: unknown reference kind accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected unknown reference kind, as expected';
END CATCH

PRINT 'domain17 ok: the reference and its kind are declared together, and the kind is closed';
