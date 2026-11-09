-- #247.1: copy one preserved table from the source database into the target.
--
-- Invoked as:  sqlcmd -d <target> -i scripts/cutover-import-table.sql -v Table="Users" -v Source="ExtraEfficiency"
--
-- WHY THE COLUMN LIST IS GENERATED. Two attempts failed before this one, and each failure was a
-- description of the schema written by hand:
--
--   1. A hand-written column list named `Users.IsActive`, which does not exist (the flag is IsDeleted).
--      sqlcmd refused the batch: "Invalid column name 'IsActive'".
--   2. `INSERT INTO t SELECT * FROM source` is shorter, but SQL Server refuses it the moment the table
--      has an identity column: "An explicit value for the identity column ... can only be specified
--      when a column list is used and IDENTITY_INSERT is ON". Measured on Skills: 24 of 36 tables.
--
-- So the list comes from sys.columns here. It cannot drift, because it IS the schema. The id is
-- preserved (the identity wrapper is deliberate: a preserved primary key is part of the fact, not an
-- artefact of the row's arrival order).

SET NOCOUNT ON;

DECLARE @table SYSNAME = '$(Table)';
DECLARE @source SYSNAME = '$(Source)';

IF OBJECT_ID(@table) IS NULL
BEGIN
  RAISERROR('cutover-import: target has no table "%s"', 16, 1, @table);
  RETURN;
END

DECLARE @columns NVARCHAR(MAX) = (
  SELECT STRING_AGG(QUOTENAME(c.name), ',') WITHIN GROUP (ORDER BY c.column_id)
  FROM sys.columns c
  WHERE c.object_id = OBJECT_ID(@table) AND c.is_computed = 0);

IF @columns IS NULL
BEGIN
  RAISERROR('cutover-import: table "%s" reports no columns', 16, 1, @table);
  RETURN;
END

DECLARE @hasIdentity BIT = CASE WHEN EXISTS (
  SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@table) AND is_identity = 1) THEN 1 ELSE 0 END;

DECLARE @sql NVARCHAR(MAX) = N'';

IF @hasIdentity = 1
  SET @sql = N'SET IDENTITY_INSERT ' + QUOTENAME(@table) + N' ON;';

SET @sql = @sql
  + N' INSERT INTO ' + QUOTENAME(@table) + N' (' + @columns + N')'
  + N' SELECT ' + @columns + N' FROM ' + QUOTENAME(@source) + N'.dbo.' + QUOTENAME(@table) + N';';

IF @hasIdentity = 1
  SET @sql = @sql + N' SET IDENTITY_INSERT ' + QUOTENAME(@table) + N' OFF;';

EXEC sp_executesql @sql;
