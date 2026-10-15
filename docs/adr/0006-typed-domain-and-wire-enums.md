# Typed domain and wire enums across layer boundaries

Database-first EF Core entities retain string properties matching the underlying database columns. However, domain models in `BusinessObjects` and API contracts in `Contracts` use typed C# enums instead of raw string literals (such as `"in_progress"`, `"paid"`, `"expired"`).

## Context

Prior to this change, status, purpose, modality, and question type values were passed as loose strings across `Services`, `WebAPI`, `WebMVC`, and the test suites. This invited silent typos, made refactoring hazardous, and obscured subtle bugs (such as an attempt history item hardcoding `"Completed"` where the schema only admits `"graded"`). Issue #2's specification called for explicit enums in `BusinessObjects`.

## Decision

1. **Domain Enums in `BusinessObjects` (`Enums.cs`, `EnumCodes.cs`)**:
   - Domain enums represent valid domain concepts aligned with the database schema's `CHECK` constraints (`AttemptStatus`, `OrderStatus`, `ExamPurpose`, `Modality`, `QuestionType`, `PaymentGatewayName`, `CreditReason`, etc.).
   - `EnumCodes.ToCode()` translates enums to database snake_case strings. When used inside EF Core LINQ query filters, EF funcletizes constant calls as query parameters.
   - `EnumCodes.Parse()` and `EnumCodes.TryParse()` map database strings into domain enums.

2. **Wire Enums in `Contracts` (`Enums.cs`, `EnumMapping.cs`)**:
   - `Contracts` defines corresponding DTO enums annotated with `[JsonStringEnumMemberName]` to guarantee wire payloads remain snake_case JSON strings.
   - `EnumMapping.MapTo<TTarget>()` seamlessly maps between domain and wire enums across the WebAPI boundary.
   - `EnumMapping.ParseWireCode<T>()` and `TryParseWireCode<T>()` allow parsing from form submissions or database values where required.

3. **Drift Prevention Tests (`EnumCodesTests.cs`)**:
   - Automated tests in `tests/Services.Tests/EnumCodesTests.cs` inspect `docs/database-schema.sql` `CHECK` constraints via regex and assert exact member parity with domain enums.
   - A companion test ensures wire DTO enums in `Contracts` mirror domain enums 1:1.

4. **Modular Endpoint Organization**:
   - WebAPI endpoints are grouped into dedicated static mapping classes under `src/WebAPI/Endpoints/` (`CatalogEndpoints`, `ExamEndpoints`, `LearnerEndpoints`, `PracticeEndpoints`, `SavedQuestionEndpoints`, `WeaknessEndpoints`, etc.) rather than residing in a single 1,600+ line `Program.cs`.
