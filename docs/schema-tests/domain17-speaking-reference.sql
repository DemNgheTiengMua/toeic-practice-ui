SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #152 / #269: ReferenceText + ReferenceKind are declared together, and the kind is a closed set. The
-- pair lives on ItemVersions now (the scored, versioned content), not on a Questions row: a Read Aloud
-- task carries its script without a picture deciding what that script means, and a past attempt
-- renders the script it was given.
--
-- Every refusal is checked by the name of the constraint that fired. A catch that only proves
-- "something failed" cannot tell a working CHECK from a typo in this file.
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
INSERT INTO Topics (Modality,Code,Name) VALUES ('speaking','S-DESC',N'Informational description and personal framing');
DECLARE @topic INT=(SELECT TopicId FROM Topics WHERE Code='S-DESC');

-- A task with no reference at all is free speech, and stays legal.
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat)
  VALUES ('speaking',@topic,'B1','descriptive_monologue','free_response');
DECLARE @free INT=SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,ResponseSeconds)
  VALUES (@free,1,N'Do you work or are you a student?',90);
PRINT 'accepted a free-speech task with no reference';

-- A Read Aloud task carries its script and says so.
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat)
  VALUES ('speaking',@topic,'B1','descriptive_monologue','free_response');
DECLARE @scripted INT=SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,ReferenceText,ReferenceKind,ResponseSeconds)
  VALUES (@scripted,1,N'Read the announcement aloud.',
          N'Attention all shoppers. The sale ends today at 9:00 PM.','script',90);
PRINT 'accepted a scripted task';

-- A picture description is the other legal kind.
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat)
  VALUES ('speaking',@topic,'B2','descriptive_monologue','free_response');
DECLARE @pic INT=SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,ReferenceText,ReferenceKind,ResponseSeconds)
  VALUES (@pic,1,N'Describe the picture.',N'Two colleagues are talking across a desk.','image_desc',90);
PRINT 'accepted a picture description';

-- Text with no kind: nothing says whether it is a script or a caption, so it cannot be used.
BEGIN TRY
  INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,ReferenceText,ResponseSeconds)
    VALUES (@scripted,2,N'unclassified',N'Some words with no kind.',90);
  THROW 50000,'EXPECT_REJECT FAILED: reference text with no kind accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_ReferencePair%' THROW;
  PRINT 'rejected reference text with no kind, by CK_ItemVersions_ReferencePair';
END CATCH

-- A kind with no text is a declaration of nothing.
BEGIN TRY
  INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,ReferenceKind,ResponseSeconds)
    VALUES (@scripted,3,N'kind only','script',90);
  THROW 50000,'EXPECT_REJECT FAILED: reference kind with no text accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_ReferencePair%' THROW;
  PRINT 'rejected reference kind with no text, by CK_ItemVersions_ReferencePair';
END CATCH

-- An unknown kind is not a kind.
BEGIN TRY
  INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,ReferenceText,ReferenceKind,ResponseSeconds)
    VALUES (@scripted,4,N'bad kind',N'Some words.','essay',90);
  THROW 50000,'EXPECT_REJECT FAILED: unknown reference kind accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_ReferenceKind%' THROW;
  PRINT 'rejected unknown reference kind, by CK_ItemVersions_ReferenceKind';
END CATCH

PRINT 'domain17 ok: the reference and its kind are declared together, and the kind is closed';
