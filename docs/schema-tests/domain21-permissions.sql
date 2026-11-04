-- docs/schema-tests/domain21-permissions.sql — #200 / D16: five composable permission namespaces.
-- A role holds any combination; the set is closed; the same pair cannot be granted twice.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

-- accept: the five codes, in one INSERT
INSERT INTO Permissions (Code) VALUES
  ('pool.content.author'), ('pool.content.review'), ('assessment.blueprint.design'),
  ('operations.support.triage'), ('system.governance.admin');
IF (SELECT COUNT(*) FROM Permissions) <> 5 THROW 50000, 'the five namespaces were not all accepted', 1;
PRINT 'five namespaces accepted';

-- reject: a sixth code. ADM-01 names five, and a typo in a policy name must not look like
-- "nobody holds this".
BEGIN TRY
  INSERT INTO Permissions (Code) VALUES ('pool.content.publish');
  THROW 50000,'EXPECT_REJECT FAILED: a sixth namespace was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a sixth namespace as expected';
END CATCH

-- accept: one role holding three of the five — a combination, not a new role
INSERT INTO Roles (Code, Name) VALUES ('sme-author', N'SME who also reviews and triages');
DECLARE @r INT = SCOPE_IDENTITY();
INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES
  (@r, 'pool.content.author'),
  (@r, 'pool.content.review'),
  (@r, 'operations.support.triage');
IF (SELECT COUNT(*) FROM RolePermissions WHERE RoleId = @r) <> 3
  THROW 50000, 'a role could not hold three namespaces', 1;
PRINT 'a role holds any combination';

-- reject: the same pair twice. Two rows saying one thing is the shape #201's triggers read; a
-- duplicate would make "does this account hold X" depend on how many times it was granted.
BEGIN TRY
  INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@r, 'pool.content.author');
  THROW 50000,'EXPECT_REJECT FAILED: the same grant was accepted twice',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a duplicate grant as expected';
END CATCH

-- reject: a grant of a namespace that does not exist
BEGIN TRY
  INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@r, 'invented.namespace');
  THROW 50000,'EXPECT_REJECT FAILED: a grant of an unknown namespace was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a grant of an unknown namespace as expected';
END CATCH

-- delete the grant, not the role: removing one namespace must leave the other two
DELETE FROM RolePermissions WHERE RoleId = @r AND PermissionCode = 'pool.content.review';
IF (SELECT COUNT(*) FROM RolePermissions WHERE RoleId = @r) <> 2
  THROW 50000, 'revoking one namespace removed more than one', 1;
PRINT 'revoking one namespace leaves the rest';
