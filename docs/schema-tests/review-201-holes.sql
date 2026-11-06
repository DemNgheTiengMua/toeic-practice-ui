-- The reviewer's three cases, run against a catalog in BOTH modes.
-- Every case is attempted as a genuine triage account, then as an author (the accepting control).
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = 'operations.support.triage')
  INSERT INTO Permissions (Code) VALUES
    ('pool.content.author'), ('pool.content.review'), ('assessment.blueprint.design'),
    ('operations.support.triage'), ('system.governance.admin');

IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'v-triage')
  INSERT INTO Roles (Code, Name) VALUES ('v-triage', N'Triage');
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'v-author')
  INSERT INTO Roles (Code, Name) VALUES ('v-author', N'Author');
DECLARE @rt INT = (SELECT RoleId FROM Roles WHERE Code = 'v-triage');
DECLARE @ra INT = (SELECT RoleId FROM Roles WHERE Code = 'v-author');
IF NOT EXISTS (SELECT 1 FROM RolePermissions WHERE RoleId = @rt AND PermissionCode = 'operations.support.triage')
  INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@rt, 'operations.support.triage');
IF NOT EXISTS (SELECT 1 FROM RolePermissions WHERE RoleId = @ra AND PermissionCode = 'pool.content.author')
  INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@ra, 'pool.content.author');

DECLARE @triage INT = (SELECT UserId FROM Users WHERE Email = 'v-triage@test.local');
IF @triage IS NULL
BEGIN
  INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@rt, 'v-triage@test.local', 0x11);
  SET @triage = SCOPE_IDENTITY();
END;
DECLARE @author INT = (SELECT UserId FROM Users WHERE Email = 'v-author@test.local');
IF @author IS NULL
BEGIN
  INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@ra, 'v-author@test.local', 0x12);
  SET @author = SCOPE_IDENTITY();
END;

DECLARE @mode BIT = dbo.fn_IsFixtureCatalog();
DECLARE @t TINYINT = dbo.fn_IsTriageOnly(@triage);
PRINT '=== CatalogMode fixture=' + CAST(@mode AS VARCHAR(1)) + ', triage=' + CAST(@triage AS VARCHAR(4))
    + ' (fn_IsTriageOnly=' + CAST(@t AS VARCHAR(1)) + '), author=' + CAST(@author AS VARCHAR(4)) + ' ===';

-- CASE 1: UPDATE of Explanation on a row with NO author, as triage.
DECLARE @v INT = (SELECT TOP 1 ItemVersionId FROM ItemVersions WHERE LastWrittenBy IS NULL);
IF @v IS NULL
BEGIN
  DECLARE @it INT = (SELECT TOP 1 ItemId FROM Items);
  DECLARE @vn INT = (SELECT ISNULL(MAX(VersionNumber),0)+1 FROM ItemVersions WHERE ItemId=@it);
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
    VALUES (@it, @vn, N'undeclared probe', N'a',N'b',N'c',N'd','A', NULL);
  SET @v = SCOPE_IDENTITY();
END
BEGIN TRY
  UPDATE ItemVersions SET Explanation = N'TRIAGE EDITED THIS' WHERE ItemVersionId = @v;
  PRINT 'CASE1 triage UPDATE of undeclared row: ACCEPTED';
END TRY BEGIN CATCH
  PRINT 'CASE1 triage UPDATE of undeclared row: REFUSED';
END CATCH

-- CASE 2: INSERT a whole new version, as triage.
DECLARE @i2 INT = (SELECT TOP 1 ItemId FROM Items);
DECLARE @n2 INT = (SELECT ISNULL(MAX(VersionNumber),0)+1 FROM ItemVersions WHERE ItemId=@i2);
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
    VALUES (@i2, @n2, N'TRIAGE AUTHORED THIS ITEM', N'a',N'b',N'c',N'd','A');
  PRINT 'CASE2 triage INSERT of new version: ACCEPTED';
END TRY BEGIN CATCH
  PRINT 'CASE2 triage INSERT of new version: REFUSED';
END CATCH

-- CONTROL A: the same UPDATE by an author, declaring itself.
BEGIN TRY
  UPDATE ItemVersions SET Explanation = N'author edited this', LastWrittenBy = @author WHERE ItemVersionId = @v;
  PRINT 'CONTROL-A author UPDATE of undeclared row: ACCEPTED';
END TRY BEGIN CATCH
  PRINT 'CONTROL-A author UPDATE of undeclared row: REFUSED <-- bad';
END CATCH

-- CONTROL B: the same INSERT by an author, declaring itself.
DECLARE @n3 INT = (SELECT ISNULL(MAX(VersionNumber),0)+1 FROM ItemVersions WHERE ItemId=@i2);
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
    VALUES (@i2, @n3, N'AUTHOR AUTHORED THIS ITEM', N'a',N'b',N'c',N'd','A', @author);
  PRINT 'CONTROL-B author INSERT of new version: ACCEPTED';
END TRY BEGIN CATCH
  PRINT 'CONTROL-B author INSERT of new version: REFUSED <-- bad';
END CATCH

-- CASE 3: the triage account declares ITSELF. This must be refused in BOTH modes.
DECLARE @n4 INT = (SELECT ISNULL(MAX(VersionNumber),0)+1 FROM ItemVersions WHERE ItemId=@i2);
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
    VALUES (@i2, @n4, N'TRIAGE DECLARED ITSELF', N'a',N'b',N'c',N'd','A', @triage);
  PRINT 'CASE3 triage INSERT declaring itself: ACCEPTED <-- bad';
END TRY BEGIN CATCH
  PRINT 'CASE3 triage INSERT declaring itself: REFUSED';
END CATCH
