-- Emits the schema's tables, columns and foreign keys as one JSON document for gen-drawio.py.
-- Run against a database loaded from database-schema.sql (see docs/schema-tests/reset-and-load.sh).
SET NOCOUNT ON;
SELECT (
  SELECT
    (SELECT t.name AS [table],
       (SELECT c.name AS [name],
               ty.name + CASE WHEN ty.name IN ('varchar','nvarchar','char','nchar','varbinary')
                 THEN '(' + CASE WHEN c.max_length = -1 THEN 'max'
                                 WHEN ty.name LIKE 'n%' THEN CAST(c.max_length/2 AS VARCHAR(10))
                                 ELSE CAST(c.max_length AS VARCHAR(10)) END + ')'
                 WHEN ty.name = 'decimal' THEN '(' + CAST(c.precision AS VARCHAR(5)) + ',' + CAST(c.scale AS VARCHAR(5)) + ')'
                 ELSE '' END AS [type],
               c.is_nullable AS [nullable],
               CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i
                     ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                     WHERE i.is_primary_key = 1 AND ic.object_id = c.object_id AND ic.column_id = c.column_id)
                 THEN 1 ELSE 0 END AS BIT) AS [pk],
               CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_key_columns fc
                     WHERE fc.parent_object_id = c.object_id AND fc.parent_column_id = c.column_id)
                 THEN 1 ELSE 0 END AS BIT) AS [fk]
        FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = t.object_id ORDER BY c.column_id FOR JSON PATH) AS [columns]
     FROM sys.tables t ORDER BY t.name FOR JSON PATH) AS [tables],
    (SELECT fk.name AS [name], OBJECT_NAME(fk.parent_object_id) AS [from],
            OBJECT_NAME(fk.referenced_object_id) AS [to],
            (SELECT TOP 1 COL_NAME(fc.parent_object_id, fc.parent_column_id) FROM sys.foreign_key_columns fc
              WHERE fc.constraint_object_id = fk.object_id ORDER BY fc.constraint_column_id) AS [col]
     FROM sys.foreign_keys fk ORDER BY fk.name FOR JSON PATH) AS [fks]
  FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
) AS doc;
