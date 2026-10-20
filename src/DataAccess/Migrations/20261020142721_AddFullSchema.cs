using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddFullSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "SEQ_PaymentNo");

            migrationBuilder.CreateTable(
                name: "Certificates",
                columns: table => new
                {
                    CertId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Certific__E5BD38C52927F6C0", x => x.CertId);
                });

            migrationBuilder.CreateTable(
                name: "Packages",
                columns: table => new
                {
                    PackageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Credits = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false),
                    Features = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Packages__322035CCF93142FE", x => x.PackageId);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Roles__8AFACE1AE028D7CB", x => x.RoleId);
                });

            migrationBuilder.CreateTable(
                name: "Exams",
                columns: table => new
                {
                    ExamId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false, defaultValue: "draft"),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "mock")
                        .Annotation("Relational:DefaultConstraintName", "DF_Exams_Purpose"),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    InternalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    QuestionCount = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Exams__297521C7A4367810", x => x.ExamId);
                    table.UniqueConstraint("AK_Exams_ExamId_CertId", x => new { x.ExamId, x.CertId });
                    table.UniqueConstraint("AK_Exams_ExamId_CertId_Purpose", x => new { x.ExamId, x.CertId, x.Purpose });
                    table.ForeignKey(
                        name: "FK__Exams__CertId__160F4887",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                });

            migrationBuilder.CreateTable(
                name: "QuestionCategories",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Question__19093A0B5AA3959D", x => x.CategoryId);
                    table.ForeignKey(
                        name: "FK__QuestionC__CertI__29221CFB",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                });

            migrationBuilder.CreateTable(
                name: "ScoreBands",
                columns: table => new
                {
                    BandId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    MinTotal = table.Column<int>(type: "int", nullable: false),
                    MaxTotal = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ScoreBan__A03693A89112EB10", x => x.BandId);
                    table.UniqueConstraint("AK_ScoreBands_CertId_Code", x => new { x.CertId, x.Code });
                    table.ForeignKey(
                        name: "FK__ScoreBand__CertI__09A971A2",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                });

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    SkillId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Modality = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Skills__DFA09187AB75DC71", x => x.SkillId);
                    table.UniqueConstraint("AK_Skills_SkillId_CertId", x => new { x.SkillId, x.CertId });
                    table.UniqueConstraint("AK_Skills_SkillId_Modality", x => new { x.SkillId, x.Modality });
                    table.ForeignKey(
                        name: "FK__Skills__CertId__6383C8BA",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    PasswordHash = table.Column<byte[]>(type: "varbinary(256)", maxLength: 256, nullable: false),
                    ActiveCertId = table.Column<int>(type: "int", nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    SecurityStamp = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false, defaultValueSql: "(CONVERT([varchar](36),newid()))")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_SecurityStamp"),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EmailVerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TermsAcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, defaultValue: "Asia/Ho_Chi_Minh")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_TimeZoneId"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_CreatedAt")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Users__1788CC4C33908FE3", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Users_ActiveCert",
                        column: x => x.ActiveCertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                    table.ForeignKey(
                        name: "FK_Users_Role",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "RoleId");
                });

            migrationBuilder.CreateTable(
                name: "PathTemplates",
                columns: table => new
                {
                    TemplateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    FromBandCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ToBandCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__PathTemp__F87ADD2726DC6DA2", x => x.TemplateId);
                    table.UniqueConstraint("AK_PathTemplates_TemplateId_CertId", x => new { x.TemplateId, x.CertId });
                    table.ForeignKey(
                        name: "FK_PathTpl_FromBand",
                        columns: x => new { x.CertId, x.FromBandCode },
                        principalTable: "ScoreBands",
                        principalColumns: new[] { "CertId", "Code" });
                    table.ForeignKey(
                        name: "FK_PathTpl_ToBand",
                        columns: x => new { x.CertId, x.ToBandCode },
                        principalTable: "ScoreBands",
                        principalColumns: new[] { "CertId", "Code" });
                    table.ForeignKey(
                        name: "FK__PathTempl__CertI__056ECC6A",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                });

            migrationBuilder.CreateTable(
                name: "GradingCriteria",
                columns: table => new
                {
                    CriterionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    MaxScore = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(5,2)", nullable: false, defaultValue: 1.0m),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__GradingC__647C3BB12364CBC3", x => x.CriterionId);
                    table.UniqueConstraint("AK_GradingCriteria_CriterionId_SkillId", x => new { x.CriterionId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_GradingCriteria_SkillCert",
                        columns: x => new { x.SkillId, x.CertId },
                        principalTable: "Skills",
                        principalColumns: new[] { "SkillId", "CertId" });
                    table.ForeignKey(
                        name: "FK__GradingCr__CertI__308E3499",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                });

            migrationBuilder.CreateTable(
                name: "Lessons",
                columns: table => new
                {
                    LessonId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 5),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Lessons__B084ACD044B3F97E", x => x.LessonId);
                    table.UniqueConstraint("AK_Lessons_LessonId_CertId", x => new { x.LessonId, x.CertId });
                    table.ForeignKey(
                        name: "FK_Lessons_SkillCert",
                        columns: x => new { x.SkillId, x.CertId },
                        principalTable: "Skills",
                        principalColumns: new[] { "SkillId", "CertId" });
                    table.ForeignKey(
                        name: "FK__Lessons__Categor__38EE7070",
                        column: x => x.CategoryId,
                        principalTable: "QuestionCategories",
                        principalColumn: "CategoryId");
                    table.ForeignKey(
                        name: "FK__Lessons__CertId__37FA4C37",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                });

            migrationBuilder.CreateTable(
                name: "ScoreScales",
                columns: table => new
                {
                    ScaleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: true),
                    ExamId = table.Column<int>(type: "int", nullable: true),
                    RawScore = table.Column<int>(type: "int", nullable: false),
                    ScaledScore = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ScoreSca__27D595667A08EBC9", x => x.ScaleId);
                    table.ForeignKey(
                        name: "FK_ScoreScales_Exam",
                        columns: x => new { x.ExamId, x.CertId },
                        principalTable: "Exams",
                        principalColumns: new[] { "ExamId", "CertId" });
                    table.ForeignKey(
                        name: "FK_ScoreScales_SkillCert",
                        columns: x => new { x.SkillId, x.CertId },
                        principalTable: "Skills",
                        principalColumns: new[] { "SkillId", "CertId" });
                    table.ForeignKey(
                        name: "FK__ScoreScal__CertI__10566F31",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    SectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    Modality = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OptionCount = table.Column<int>(type: "int", nullable: true),
                    ExpectedQuestionCount = table.Column<int>(type: "int", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Sections__80EF0872ACBB3BA8", x => x.SectionId);
                    table.UniqueConstraint("AK_Sections_SectionId_CertId", x => new { x.SectionId, x.CertId });
                    table.UniqueConstraint("AK_Sections_SectionId_Modality", x => new { x.SectionId, x.Modality });
                    table.UniqueConstraint("AK_Sections_SectionId_SkillId", x => new { x.SectionId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_Sections_SkillCert",
                        columns: x => new { x.SkillId, x.CertId },
                        principalTable: "Skills",
                        principalColumns: new[] { "SkillId", "CertId" });
                    table.ForeignKey(
                        name: "FK_Sections_SkillModality",
                        columns: x => new { x.SkillId, x.Modality },
                        principalTable: "Skills",
                        principalColumns: new[] { "SkillId", "Modality" });
                });

            migrationBuilder.CreateTable(
                name: "EmailVerificationTokens",
                columns: table => new
                {
                    TokenId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "varbinary(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                        .Annotation("Relational:DefaultConstraintName", "DF_EmailVerificationTokens_CreatedAt")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__EmailVer__658FEEEA5F8B9DDD", x => x.TokenId);
                    table.ForeignKey(
                        name: "FK_EmailVerificationTokens_User",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    BatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "pending"),
                    UploadedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())"),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ImportBa__5D55CE589E3B37D5", x => x.BatchId);
                    table.ForeignKey(
                        name: "FK__ImportBat__CertI__4D5F7D71",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                    table.ForeignKey(
                        name: "FK__ImportBat__Uploa__503BEA1C",
                        column: x => x.UploadedBy,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "MentorProfiles",
                columns: table => new
                {
                    MentorId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Bio = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<string>(type: "varchar(6)", unicode: false, maxLength: 6, nullable: false),
                    PricePerSlot = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PriceCredits = table.Column<int>(type: "int", nullable: false, defaultValue: 1)
                        .Annotation("Relational:DefaultConstraintName", "DF_Mentor_PriceCredits"),
                    Headline = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Expertise = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SessionMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 60)
                        .Annotation("Relational:DefaultConstraintName", "DF_Mentor_SessionMinutes"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__MentorPr__053B7E982DEF0306", x => x.MentorId);
                    table.UniqueConstraint("AK_MentorProfiles_MentorId_Type", x => new { x.MentorId, x.Type });
                    table.ForeignKey(
                        name: "FK_Mentor_SkillCert",
                        columns: x => new { x.SkillId, x.CertId },
                        principalTable: "Skills",
                        principalColumns: new[] { "SkillId", "CertId" });
                    table.ForeignKey(
                        name: "FK__MentorPro__CertI__567ED357",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                    table.ForeignKey(
                        name: "FK__MentorPro__Skill__5772F790",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "SkillId");
                    table.ForeignKey(
                        name: "FK__MentorPro__UserI__558AAF1E",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PackageId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false, defaultValue: "pending"),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    OrderCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PaymentNo = table.Column<long>(type: "bigint", nullable: false),
                    Gateway = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    GatewayTxnRef = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    GatewayErrorCode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())"),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Orders__C3905BCFA292CBC6", x => x.OrderId);
                    table.UniqueConstraint("AK_Orders_OrderId_UserId", x => new { x.OrderId, x.UserId });
                    table.ForeignKey(
                        name: "FK__Orders__PackageI__25A691D2",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "PackageId");
                    table.ForeignKey(
                        name: "FK__Orders__UserId__24B26D99",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    TokenId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "varbinary(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                        .Annotation("Relational:DefaultConstraintName", "DF_PasswordResetTokens_CreatedAt")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Password__658FEEEA66FD11FE", x => x.TokenId);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_User",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "TargetBands",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    TargetBandCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TargetBands", x => new { x.UserId, x.CertId });
                    table.ForeignKey(
                        name: "FK_TargetBands_Band",
                        columns: x => new { x.CertId, x.TargetBandCode },
                        principalTable: "ScoreBands",
                        principalColumns: new[] { "CertId", "Code" });
                    table.ForeignKey(
                        name: "FK__TargetBan__CertI__7FB5F314",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                    table.ForeignKey(
                        name: "FK__TargetBan__UserI__7EC1CEDB",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "PathModules",
                columns: table => new
                {
                    ModuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__PathModu__2B7477A71FBFA640", x => x.ModuleId);
                    table.UniqueConstraint("AK_PathModules_ModuleId_CertId", x => new { x.ModuleId, x.CertId });
                    table.UniqueConstraint("AK_PathModules_ModuleId_TemplateId", x => new { x.ModuleId, x.TemplateId });
                    table.ForeignKey(
                        name: "FK_PathModules_SkillCert",
                        columns: x => new { x.SkillId, x.CertId },
                        principalTable: "Skills",
                        principalColumns: new[] { "SkillId", "CertId" });
                    table.ForeignKey(
                        name: "FK_PathModules_TemplateCert",
                        columns: x => new { x.TemplateId, x.CertId },
                        principalTable: "PathTemplates",
                        principalColumns: new[] { "TemplateId", "CertId" });
                });

            migrationBuilder.CreateTable(
                name: "LessonCards",
                columns: table => new
                {
                    CardId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LessonId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    CardType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ContentMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuestionPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OptionA = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    OptionB = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    OptionC = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    OptionD = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CorrectOption = table.Column<string>(type: "char(1)", unicode: false, fixedLength: true, maxLength: 1, nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rationale = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LessonCa__55FECDAE5025293F", x => x.CardId);
                    table.ForeignKey(
                        name: "FK_LessonCards_LessonCert",
                        columns: x => new { x.LessonId, x.CertId },
                        principalTable: "Lessons",
                        principalColumns: new[] { "LessonId", "CertId" });
                });

            migrationBuilder.CreateTable(
                name: "ExamSectionAudio",
                columns: table => new
                {
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    AudioPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DurationSec = table.Column<int>(type: "int", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSectionAudio", x => new { x.ExamId, x.SectionId });
                    table.ForeignKey(
                        name: "FK_ExamSectionAudio_ExamCert",
                        columns: x => new { x.ExamId, x.CertId },
                        principalTable: "Exams",
                        principalColumns: new[] { "ExamId", "CertId" });
                    table.ForeignKey(
                        name: "FK_ExamSectionAudio_SectionCert",
                        columns: x => new { x.SectionId, x.CertId },
                        principalTable: "Sections",
                        principalColumns: new[] { "SectionId", "CertId" });
                });

            migrationBuilder.CreateTable(
                name: "QuestionGroups",
                columns: table => new
                {
                    GroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    Passage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AudioPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AudioStartMs = table.Column<int>(type: "int", nullable: true),
                    AudioEndMs = table.Column<int>(type: "int", nullable: true),
                    ImagePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ImageAlt = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Transcript = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PrepSeconds = table.Column<int>(type: "int", nullable: true),
                    ResponseSeconds = table.Column<int>(type: "int", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Question__149AF36A03E95EE7", x => x.GroupId);
                    table.UniqueConstraint("AK_QuestionGroups_GroupId_ExamId", x => new { x.GroupId, x.ExamId });
                    table.UniqueConstraint("AK_QuestionGroups_GroupId_SectionId", x => new { x.GroupId, x.SectionId });
                    table.ForeignKey(
                        name: "FK_QGroups_ExamCert",
                        columns: x => new { x.ExamId, x.CertId },
                        principalTable: "Exams",
                        principalColumns: new[] { "ExamId", "CertId" });
                    table.ForeignKey(
                        name: "FK_QGroups_SectionCert",
                        columns: x => new { x.SectionId, x.CertId },
                        principalTable: "Sections",
                        principalColumns: new[] { "SectionId", "CertId" });
                });

            migrationBuilder.CreateTable(
                name: "SectionCriteria",
                columns: table => new
                {
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    CriterionId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    MaxScore = table.Column<decimal>(type: "decimal(5,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SectionCriteria", x => new { x.SectionId, x.CriterionId });
                    table.ForeignKey(
                        name: "FK_SectionCriteria_CriterionSkill",
                        columns: x => new { x.CriterionId, x.SkillId },
                        principalTable: "GradingCriteria",
                        principalColumns: new[] { "CriterionId", "SkillId" });
                    table.ForeignKey(
                        name: "FK_SectionCriteria_SectionSkill",
                        columns: x => new { x.SectionId, x.SkillId },
                        principalTable: "Sections",
                        principalColumns: new[] { "SectionId", "SkillId" });
                });

            migrationBuilder.CreateTable(
                name: "ImportLog",
                columns: table => new
                {
                    LogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    RowNo = table.Column<int>(type: "int", nullable: true),
                    Level = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    LoggedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ImportLo__5E548648FBF92143", x => x.LogId);
                    table.ForeignKey(
                        name: "FK__ImportLog__Batch__540C7B00",
                        column: x => x.BatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "BatchId");
                });

            migrationBuilder.CreateTable(
                name: "MentorPayouts",
                columns: table => new
                {
                    PayoutId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MentorId = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false, defaultValue: "pending"),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__MentorPa__35C3DF8E859EADFC", x => x.PayoutId);
                    table.UniqueConstraint("AK_MentorPayouts_PayoutId_MentorId", x => new { x.PayoutId, x.MentorId });
                    table.ForeignKey(
                        name: "FK__MentorPay__Mento__0CDAE408",
                        column: x => x.MentorId,
                        principalTable: "MentorProfiles",
                        principalColumn: "MentorId");
                });

            migrationBuilder.CreateTable(
                name: "MentorSlots",
                columns: table => new
                {
                    SlotId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MentorId = table.Column<int>(type: "int", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "varchar(6)", unicode: false, maxLength: 6, nullable: false, defaultValue: "open")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__MentorSl__0A124AAFDF2C4576", x => x.SlotId);
                    table.UniqueConstraint("AK_MentorSlots_SlotId_MentorId", x => new { x.SlotId, x.MentorId });
                    table.ForeignKey(
                        name: "FK__MentorSlo__Mento__63D8CE75",
                        column: x => x.MentorId,
                        principalTable: "MentorProfiles",
                        principalColumn: "MentorId");
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    EventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ReportedStatus = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: true),
                    SignatureValid = table.Column<bool>(type: "bit", nullable: false),
                    WasApplied = table.Column<bool>(type: "bit", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                        .Annotation("Relational:DefaultConstraintName", "DF_PaymentEvents_CreatedAt")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__PaymentE__7944C8104787C6C1", x => x.EventId);
                    table.ForeignKey(
                        name: "FK__PaymentEv__Order__2E3BD7D3",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "OrderId");
                });

            migrationBuilder.CreateTable(
                name: "PathModuleItems",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModuleId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    ItemType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    RefSectionId = table.Column<int>(type: "int", nullable: true),
                    RefExamId = table.Column<int>(type: "int", nullable: true),
                    DifficultyLevel = table.Column<int>(type: "int", nullable: true),
                    QuestionCount = table.Column<int>(type: "int", nullable: true),
                    IsPremiumOnly = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    RefLessonId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__PathModu__727E838B42341F73", x => x.ItemId);
                    table.UniqueConstraint("AK_PathModuleItems_ItemId_TemplateId", x => new { x.ItemId, x.TemplateId });
                    table.ForeignKey(
                        name: "FK_PMI_ExamCert",
                        columns: x => new { x.RefExamId, x.CertId },
                        principalTable: "Exams",
                        principalColumns: new[] { "ExamId", "CertId" });
                    table.ForeignKey(
                        name: "FK_PMI_LessonCert",
                        columns: x => new { x.RefLessonId, x.CertId },
                        principalTable: "Lessons",
                        principalColumns: new[] { "LessonId", "CertId" });
                    table.ForeignKey(
                        name: "FK_PMI_ModuleCert",
                        columns: x => new { x.ModuleId, x.CertId },
                        principalTable: "PathModules",
                        principalColumns: new[] { "ModuleId", "CertId" });
                    table.ForeignKey(
                        name: "FK_PMI_ModuleTemplate",
                        columns: x => new { x.ModuleId, x.TemplateId },
                        principalTable: "PathModules",
                        principalColumns: new[] { "ModuleId", "TemplateId" });
                    table.ForeignKey(
                        name: "FK_PMI_SectionCert",
                        columns: x => new { x.RefSectionId, x.CertId },
                        principalTable: "Sections",
                        principalColumns: new[] { "SectionId", "CertId" });
                });

            migrationBuilder.CreateTable(
                name: "PathModuleTopics",
                columns: table => new
                {
                    TopicId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModuleId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    QuestionCount = table.Column<int>(type: "int", nullable: true),
                    VideoPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__PathModu__022E0F5D1DBB1B56", x => x.TopicId);
                    table.ForeignKey(
                        name: "FK__PathModul__Categ__1F2E9E6D",
                        column: x => x.CategoryId,
                        principalTable: "QuestionCategories",
                        principalColumn: "CategoryId");
                    table.ForeignKey(
                        name: "FK__PathModul__Modul__1E3A7A34",
                        column: x => x.ModuleId,
                        principalTable: "PathModules",
                        principalColumn: "ModuleId");
                });

            migrationBuilder.CreateTable(
                name: "LearnerLessonProgress",
                columns: table => new
                {
                    ProgressId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LessonId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    LastViewedCardId = table.Column<int>(type: "int", nullable: true),
                    CardsCompleted = table.Column<int>(type: "int", nullable: false),
                    TotalCards = table.Column<int>(type: "int", nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    FirstOpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())"),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LearnerL__BAE29CA5C616E5E3", x => x.ProgressId);
                    table.ForeignKey(
                        name: "FK_LLP_LessonCert",
                        columns: x => new { x.LessonId, x.CertId },
                        principalTable: "Lessons",
                        principalColumns: new[] { "LessonId", "CertId" });
                    table.ForeignKey(
                        name: "FK__LearnerLe__LastV__4924D839",
                        column: x => x.LastViewedCardId,
                        principalTable: "LessonCards",
                        principalColumn: "CardId");
                    table.ForeignKey(
                        name: "FK__LearnerLe__UserI__4830B400",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "Questions",
                columns: table => new
                {
                    QuestionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    Stem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DifficultyLevel = table.Column<int>(type: "int", nullable: false),
                    QuestionType = table.Column<string>(type: "varchar(14)", unicode: false, maxLength: 14, nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    PrepSeconds = table.Column<int>(type: "int", nullable: true),
                    ResponseSeconds = table.Column<int>(type: "int", nullable: true),
                    MinWords = table.Column<int>(type: "int", nullable: true),
                    MaxWords = table.Column<int>(type: "int", nullable: true),
                    Hint = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Question__0DC06FACD56E3AE0", x => x.QuestionId);
                    table.UniqueConstraint("AK_Questions_QuestionId_ExamId", x => new { x.QuestionId, x.ExamId });
                    table.UniqueConstraint("AK_Questions_QuestionId_QuestionType", x => new { x.QuestionId, x.QuestionType });
                    table.UniqueConstraint("AK_Questions_QuestionId_SectionId", x => new { x.QuestionId, x.SectionId });
                    table.UniqueConstraint("AK_Questions_QuestionId_SkillId", x => new { x.QuestionId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_Questions_Category",
                        column: x => x.CategoryId,
                        principalTable: "QuestionCategories",
                        principalColumn: "CategoryId");
                    table.ForeignKey(
                        name: "FK_Questions_GroupExam",
                        columns: x => new { x.GroupId, x.ExamId },
                        principalTable: "QuestionGroups",
                        principalColumns: new[] { "GroupId", "ExamId" });
                    table.ForeignKey(
                        name: "FK_Questions_GroupSection",
                        columns: x => new { x.GroupId, x.SectionId },
                        principalTable: "QuestionGroups",
                        principalColumns: new[] { "GroupId", "SectionId" });
                    table.ForeignKey(
                        name: "FK_Questions_SectionSkill",
                        columns: x => new { x.SectionId, x.SkillId },
                        principalTable: "Sections",
                        principalColumns: new[] { "SectionId", "SkillId" });
                    table.ForeignKey(
                        name: "FK__Questions__Group__3A4CA8FD",
                        column: x => x.GroupId,
                        principalTable: "QuestionGroups",
                        principalColumn: "GroupId");
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    BookingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    MentorId = table.Column<int>(type: "int", nullable: false),
                    SlotId = table.Column<int>(type: "int", nullable: true),
                    MentorType = table.Column<string>(type: "varchar(6)", unicode: false, maxLength: 6, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "pending"),
                    MeetLink = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    FocusArea = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Bookings__73951AEDC046E1C8", x => x.BookingId);
                    table.UniqueConstraint("AK_Bookings_BookingId_MentorId", x => new { x.BookingId, x.MentorId });
                    table.UniqueConstraint("AK_Bookings_BookingId_UserId", x => new { x.BookingId, x.UserId });
                    table.ForeignKey(
                        name: "FK_Bookings_MentorType",
                        columns: x => new { x.MentorId, x.MentorType },
                        principalTable: "MentorProfiles",
                        principalColumns: new[] { "MentorId", "Type" });
                    table.ForeignKey(
                        name: "FK_Bookings_SlotMentor",
                        columns: x => new { x.SlotId, x.MentorId },
                        principalTable: "MentorSlots",
                        principalColumns: new[] { "SlotId", "MentorId" });
                    table.ForeignKey(
                        name: "FK__Bookings__Mentor__6C6E1476",
                        column: x => x.MentorId,
                        principalTable: "MentorProfiles",
                        principalColumn: "MentorId");
                    table.ForeignKey(
                        name: "FK__Bookings__SlotId__6D6238AF",
                        column: x => x.SlotId,
                        principalTable: "MentorSlots",
                        principalColumn: "SlotId");
                    table.ForeignKey(
                        name: "FK__Bookings__UserId__6B79F03D",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "ExamAttempts",
                columns: table => new
                {
                    AttemptId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false, defaultValue: "in_progress"),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsAutoSubmitted = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastQuestionId = table.Column<int>(type: "int", nullable: true),
                    TotalScore = table.Column<int>(type: "int", nullable: true),
                    BandCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ExamAtte__891A68E672B77157", x => x.AttemptId);
                    table.UniqueConstraint("AK_ExamAttempts_AttemptId_CertId", x => new { x.AttemptId, x.CertId });
                    table.UniqueConstraint("AK_ExamAttempts_AttemptId_ExamId", x => new { x.AttemptId, x.ExamId });
                    table.UniqueConstraint("AK_ExamAttempts_AttemptId_UserId", x => new { x.AttemptId, x.UserId });
                    table.ForeignKey(
                        name: "FK_Attempts_ExamCert",
                        columns: x => new { x.ExamId, x.CertId },
                        principalTable: "Exams",
                        principalColumns: new[] { "ExamId", "CertId" });
                    table.ForeignKey(
                        name: "FK_Attempts_LastQuestionExam",
                        columns: x => new { x.LastQuestionId, x.ExamId },
                        principalTable: "Questions",
                        principalColumns: new[] { "QuestionId", "ExamId" });
                    table.ForeignKey(
                        name: "FK__ExamAttem__CertI__7EF6D905",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                    table.ForeignKey(
                        name: "FK__ExamAttem__ExamI__7E02B4CC",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId");
                    table.ForeignKey(
                        name: "FK__ExamAttem__UserI__7D0E9093",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "QuestionOptions",
                columns: table => new
                {
                    OptionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    QuestionType = table.Column<string>(type: "varchar(14)", unicode: false, maxLength: 14, nullable: false),
                    Label = table.Column<string>(type: "varchar(4)", unicode: false, maxLength: 4, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    Rationale = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Question__92C7A1FFB8F0027D", x => x.OptionId);
                    table.UniqueConstraint("AK_QuestionOptions_OptionId_QuestionId", x => new { x.OptionId, x.QuestionId });
                    table.ForeignKey(
                        name: "FK_Options_QuestionType",
                        columns: x => new { x.QuestionId, x.QuestionType },
                        principalTable: "Questions",
                        principalColumns: new[] { "QuestionId", "QuestionType" });
                    table.ForeignKey(
                        name: "FK__QuestionO__Quest__339FAB6E",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "QuestionId");
                });

            migrationBuilder.CreateTable(
                name: "SavedQuestions",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedQuestions", x => new { x.UserId, x.QuestionId });
                    table.ForeignKey(
                        name: "FK__SavedQues__Quest__756D6ECB",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "QuestionId");
                    table.ForeignKey(
                        name: "FK__SavedQues__UserI__74794A92",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "MentorPayoutItems",
                columns: table => new
                {
                    PayoutId = table.Column<int>(type: "int", nullable: false),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    MentorId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutItems", x => new { x.PayoutId, x.BookingId });
                    table.ForeignKey(
                        name: "FK_PayoutItems_BookingMentor",
                        columns: x => new { x.BookingId, x.MentorId },
                        principalTable: "Bookings",
                        principalColumns: new[] { "BookingId", "MentorId" });
                    table.ForeignKey(
                        name: "FK_PayoutItems_PayoutMentor",
                        columns: x => new { x.PayoutId, x.MentorId },
                        principalTable: "MentorPayouts",
                        principalColumns: new[] { "PayoutId", "MentorId" });
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    ReviewId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Reviews__74BC79CE80FE2BB3", x => x.ReviewId);
                    table.ForeignKey(
                        name: "FK_Reviews_BookingUser",
                        columns: x => new { x.BookingId, x.UserId },
                        principalTable: "Bookings",
                        principalColumns: new[] { "BookingId", "UserId" });
                    table.ForeignKey(
                        name: "FK__Reviews__UserId__78D3EB5B",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "AttemptSkillScores",
                columns: table => new
                {
                    AttemptId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    RawScore = table.Column<int>(type: "int", nullable: false),
                    ScaledScore = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttemptSkillScores", x => new { x.AttemptId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_ASS_AttemptCert",
                        columns: x => new { x.AttemptId, x.CertId },
                        principalTable: "ExamAttempts",
                        principalColumns: new[] { "AttemptId", "CertId" });
                    table.ForeignKey(
                        name: "FK_ASS_SkillCert",
                        columns: x => new { x.SkillId, x.CertId },
                        principalTable: "Skills",
                        principalColumns: new[] { "SkillId", "CertId" });
                    table.ForeignKey(
                        name: "FK__AttemptSk__Attem__0880433F",
                        column: x => x.AttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "AttemptId");
                    table.ForeignKey(
                        name: "FK__AttemptSk__Skill__09746778",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "SkillId");
                });

            migrationBuilder.CreateTable(
                name: "FreeResponses",
                columns: table => new
                {
                    FreeResponseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttemptId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    QuestionType = table.Column<string>(type: "varchar(14)", unicode: false, maxLength: 14, nullable: false),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AudioPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false, defaultValue: "pending_ai"),
                    DraftSavedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__FreeResp__239C05117F02D927", x => x.FreeResponseId);
                    table.UniqueConstraint("AK_FreeResponses_FreeResponseId_SkillId", x => new { x.FreeResponseId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_FreeResp_AttemptExam",
                        columns: x => new { x.AttemptId, x.ExamId },
                        principalTable: "ExamAttempts",
                        principalColumns: new[] { "AttemptId", "ExamId" });
                    table.ForeignKey(
                        name: "FK_FreeResp_QuestionExam",
                        columns: x => new { x.QuestionId, x.ExamId },
                        principalTable: "Questions",
                        principalColumns: new[] { "QuestionId", "ExamId" });
                    table.ForeignKey(
                        name: "FK_FreeResp_QuestionSkill",
                        columns: x => new { x.QuestionId, x.SkillId },
                        principalTable: "Questions",
                        principalColumns: new[] { "QuestionId", "SkillId" });
                    table.ForeignKey(
                        name: "FK_FreeResp_QuestionType",
                        columns: x => new { x.QuestionId, x.QuestionType },
                        principalTable: "Questions",
                        principalColumns: new[] { "QuestionId", "QuestionType" });
                    table.ForeignKey(
                        name: "FK__FreeRespo__Attem__18B6AB08",
                        column: x => x.AttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "AttemptId");
                    table.ForeignKey(
                        name: "FK__FreeRespo__Quest__19AACF41",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "QuestionId");
                });

            migrationBuilder.CreateTable(
                name: "PlacementTests",
                columns: table => new
                {
                    PlacementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false, defaultValue: "in_progress"),
                    ResultBandCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    AttemptId = table.Column<int>(type: "int", nullable: true),
                    ExamPurpose = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "placement")
                        .Annotation("Relational:DefaultConstraintName", "DF_Placement_ExamPurpose"),
                    TotalScore = table.Column<int>(type: "int", nullable: true),
                    TakenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Placemen__2E328C652BD86022", x => x.PlacementId);
                    table.UniqueConstraint("AK_PlacementTests_PlacementId_UserId", x => new { x.PlacementId, x.UserId });
                    table.ForeignKey(
                        name: "FK_Placement_AttemptUser",
                        columns: x => new { x.AttemptId, x.UserId },
                        principalTable: "ExamAttempts",
                        principalColumns: new[] { "AttemptId", "UserId" });
                    table.ForeignKey(
                        name: "FK_Placement_ExamCert",
                        columns: x => new { x.ExamId, x.CertId },
                        principalTable: "Exams",
                        principalColumns: new[] { "ExamId", "CertId" });
                    table.ForeignKey(
                        name: "FK_Placement_ExamPurpose",
                        columns: x => new { x.ExamId, x.CertId, x.ExamPurpose },
                        principalTable: "Exams",
                        principalColumns: new[] { "ExamId", "CertId", "Purpose" });
                    table.ForeignKey(
                        name: "FK__Placement__CertI__725BF7F6",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                    table.ForeignKey(
                        name: "FK__Placement__UserI__7167D3BD",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "AttemptAnswers",
                columns: table => new
                {
                    AnswerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttemptId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    SelectedOptionId = table.Column<int>(type: "int", nullable: true),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: true),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__AttemptA__D48250049EDA465F", x => x.AnswerId);
                    table.ForeignKey(
                        name: "FK_AttemptAnswers_AttemptExam",
                        columns: x => new { x.AttemptId, x.ExamId },
                        principalTable: "ExamAttempts",
                        principalColumns: new[] { "AttemptId", "ExamId" });
                    table.ForeignKey(
                        name: "FK_AttemptAnswers_OptionQuestion",
                        columns: x => new { x.SelectedOptionId, x.QuestionId },
                        principalTable: "QuestionOptions",
                        principalColumns: new[] { "OptionId", "QuestionId" });
                    table.ForeignKey(
                        name: "FK_AttemptAnswers_QuestionExam",
                        columns: x => new { x.QuestionId, x.ExamId },
                        principalTable: "Questions",
                        principalColumns: new[] { "QuestionId", "ExamId" });
                    table.ForeignKey(
                        name: "FK__AttemptAn__Attem__0F2D40CE",
                        column: x => x.AttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "AttemptId");
                    table.ForeignKey(
                        name: "FK__AttemptAn__Quest__10216507",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "QuestionId");
                });

            migrationBuilder.CreateTable(
                name: "AiGradings",
                columns: table => new
                {
                    GradingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FreeResponseId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false, defaultValue: "pending"),
                    OverallScaled = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                    BandCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 1)
                        .Annotation("Relational:DefaultConstraintName", "DF_AiGradings_AttemptCount"),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())"),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__AiGradin__773832AF85C52DBC", x => x.GradingId);
                    table.UniqueConstraint("AK_AiGradings_GradingId_SkillId", x => new { x.GradingId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_AiGradings_ResponseSkill",
                        columns: x => new { x.FreeResponseId, x.SkillId },
                        principalTable: "FreeResponses",
                        principalColumns: new[] { "FreeResponseId", "SkillId" });
                    table.ForeignKey(
                        name: "FK__AiGrading__FreeR__3DE82FB7",
                        column: x => x.FreeResponseId,
                        principalTable: "FreeResponses",
                        principalColumn: "FreeResponseId");
                });

            migrationBuilder.CreateTable(
                name: "LearnerPaths",
                columns: table => new
                {
                    LearnerPathId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    CurrentBandCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    PlacementId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "active"),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LearnerP__166F32282358C553", x => x.LearnerPathId);
                    table.UniqueConstraint("AK_LearnerPaths_LearnerPathId_TemplateId", x => new { x.LearnerPathId, x.TemplateId });
                    table.UniqueConstraint("AK_LearnerPaths_LearnerPathId_UserId", x => new { x.LearnerPathId, x.UserId });
                    table.ForeignKey(
                        name: "FK_LearnerPaths_PlacementUser",
                        columns: x => new { x.PlacementId, x.UserId },
                        principalTable: "PlacementTests",
                        principalColumns: new[] { "PlacementId", "UserId" });
                    table.ForeignKey(
                        name: "FK_LearnerPaths_TemplateCert",
                        columns: x => new { x.TemplateId, x.CertId },
                        principalTable: "PathTemplates",
                        principalColumns: new[] { "TemplateId", "CertId" });
                    table.ForeignKey(
                        name: "FK__LearnerPa__CertI__26CFC035",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                    table.ForeignKey(
                        name: "FK__LearnerPa__UserI__25DB9BFC",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "AiGradingScores",
                columns: table => new
                {
                    GradingId = table.Column<int>(type: "int", nullable: false),
                    CriterionId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGradingScores", x => new { x.GradingId, x.CriterionId });
                    table.ForeignKey(
                        name: "FK_AGS_CriterionSkill",
                        columns: x => new { x.CriterionId, x.SkillId },
                        principalTable: "GradingCriteria",
                        principalColumns: new[] { "CriterionId", "SkillId" });
                    table.ForeignKey(
                        name: "FK_AGS_GradingSkill",
                        columns: x => new { x.GradingId, x.SkillId },
                        principalTable: "AiGradings",
                        principalColumns: new[] { "GradingId", "SkillId" });
                });

            migrationBuilder.CreateTable(
                name: "CreditTransactions",
                columns: table => new
                {
                    TxnId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Delta = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    AttemptId = table.Column<int>(type: "int", nullable: true),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    LearnerPathId = table.Column<int>(type: "int", nullable: true),
                    AdminUserId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CreditTr__C19608544ABB5611", x => x.TxnId);
                    table.UniqueConstraint("AK_CreditTransactions_TxnId_UserId", x => new { x.TxnId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CreditTxn_Admin",
                        column: x => x.AdminUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_CreditTxn_Attempt",
                        columns: x => new { x.AttemptId, x.UserId },
                        principalTable: "ExamAttempts",
                        principalColumns: new[] { "AttemptId", "UserId" });
                    table.ForeignKey(
                        name: "FK_CreditTxn_Booking",
                        columns: x => new { x.BookingId, x.UserId },
                        principalTable: "Bookings",
                        principalColumns: new[] { "BookingId", "UserId" });
                    table.ForeignKey(
                        name: "FK_CreditTxn_Order",
                        columns: x => new { x.OrderId, x.UserId },
                        principalTable: "Orders",
                        principalColumns: new[] { "OrderId", "UserId" });
                    table.ForeignKey(
                        name: "FK_CreditTxn_Path",
                        columns: x => new { x.LearnerPathId, x.UserId },
                        principalTable: "LearnerPaths",
                        principalColumns: new[] { "LearnerPathId", "UserId" });
                    table.ForeignKey(
                        name: "FK__CreditTra__UserI__39AD8A7F",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "LearnerPathSteps",
                columns: table => new
                {
                    StepId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LearnerPathId = table.Column<int>(type: "int", nullable: false),
                    PathModuleItemId = table.Column<int>(type: "int", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false, defaultValue: "locked"),
                    Score = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LearnerP__24343357410D371C", x => x.StepId);
                    table.ForeignKey(
                        name: "FK_Steps_ItemTemplate",
                        columns: x => new { x.PathModuleItemId, x.TemplateId },
                        principalTable: "PathModuleItems",
                        principalColumns: new[] { "ItemId", "TemplateId" });
                    table.ForeignKey(
                        name: "FK_Steps_PathTemplate",
                        columns: x => new { x.LearnerPathId, x.TemplateId },
                        principalTable: "LearnerPaths",
                        principalColumns: new[] { "LearnerPathId", "TemplateId" });
                });

            migrationBuilder.CreateTable(
                name: "Complaints",
                columns: table => new
                {
                    ComplaintId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    AttemptId = table.Column<int>(type: "int", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "open"),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InternalNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedByGrantTxnId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Complain__740D898F85AEDFE4", x => x.ComplaintId);
                    table.ForeignKey(
                        name: "FK_Complaints_AttemptUser",
                        columns: x => new { x.AttemptId, x.UserId },
                        principalTable: "ExamAttempts",
                        principalColumns: new[] { "AttemptId", "UserId" });
                    table.ForeignKey(
                        name: "FK_Complaints_BookingUser",
                        columns: x => new { x.BookingId, x.UserId },
                        principalTable: "Bookings",
                        principalColumns: new[] { "BookingId", "UserId" });
                    table.ForeignKey(
                        name: "FK_Complaints_GrantTxn",
                        columns: x => new { x.ResolvedByGrantTxnId, x.UserId },
                        principalTable: "CreditTransactions",
                        principalColumns: new[] { "TxnId", "UserId" });
                    table.ForeignKey(
                        name: "FK_Complaints_OrderUser",
                        columns: x => new { x.OrderId, x.UserId },
                        principalTable: "Orders",
                        principalColumns: new[] { "OrderId", "UserId" });
                    table.ForeignKey(
                        name: "FK_Complaints_ResolvedBy",
                        column: x => x.ResolvedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK__Complaint__UserI__7E8CC4B1",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "PracticeSessions",
                columns: table => new
                {
                    SessionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    Modality = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    QuestionCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false, defaultValue: "in_progress"),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    QuestionSource = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false, defaultValue: "all")
                        .Annotation("Relational:DefaultConstraintName", "DF_Practice_Source"),
                    FeedbackMode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "immediate")
                        .Annotation("Relational:DefaultConstraintName", "DF_Practice_Feedback"),
                    LearnerPathStepId = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())"),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Practice__C9F4929081A3986C", x => x.SessionId);
                    table.UniqueConstraint("AK_PracticeSessions_SessionId_SectionId", x => new { x.SessionId, x.SectionId });
                    table.UniqueConstraint("AK_PracticeSessions_SessionId_UserId", x => new { x.SessionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_Practice_PathStep",
                        column: x => x.LearnerPathStepId,
                        principalTable: "LearnerPathSteps",
                        principalColumn: "StepId");
                    table.ForeignKey(
                        name: "FK_Practice_SectionModality",
                        columns: x => new { x.SectionId, x.Modality },
                        principalTable: "Sections",
                        principalColumns: new[] { "SectionId", "Modality" });
                    table.ForeignKey(
                        name: "FK__PracticeS__Secti__5D95E53A",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "SectionId");
                    table.ForeignKey(
                        name: "FK__PracticeS__UserI__5CA1C101",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "AiInsights",
                columns: table => new
                {
                    InsightId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    PracticeSessionId = table.Column<int>(type: "int", nullable: true),
                    AttemptId = table.Column<int>(type: "int", nullable: true),
                    LearnerPathId = table.Column<int>(type: "int", nullable: true),
                    Narrative = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Trend = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    Status = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false, defaultValue: "pending")
                        .Annotation("Relational:DefaultConstraintName", "DF_AiInsights_Status"),
                    FailureReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PromptTokens = table.Column<int>(type: "int", nullable: true),
                    CompletionTokens = table.Column<int>(type: "int", nullable: true),
                    SourceFingerprint = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                        .Annotation("Relational:DefaultConstraintName", "DF_AiInsights_CreatedAt"),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__AiInsigh__6A3F54F4E73AE5FA", x => x.InsightId);
                    table.UniqueConstraint("AK_AiInsights_InsightId_UserId", x => new { x.InsightId, x.UserId });
                    table.ForeignKey(
                        name: "FK_AiInsights_AttemptUser",
                        columns: x => new { x.AttemptId, x.UserId },
                        principalTable: "ExamAttempts",
                        principalColumns: new[] { "AttemptId", "UserId" });
                    table.ForeignKey(
                        name: "FK_AiInsights_PathUser",
                        columns: x => new { x.LearnerPathId, x.UserId },
                        principalTable: "LearnerPaths",
                        principalColumns: new[] { "LearnerPathId", "UserId" });
                    table.ForeignKey(
                        name: "FK_AiInsights_SessionUser",
                        columns: x => new { x.PracticeSessionId, x.UserId },
                        principalTable: "PracticeSessions",
                        principalColumns: new[] { "SessionId", "UserId" });
                    table.ForeignKey(
                        name: "FK__AiInsight__CertI__53D770D6",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                    table.ForeignKey(
                        name: "FK__AiInsight__UserI__52E34C9D",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "PracticeAnswers",
                columns: table => new
                {
                    AnswerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    SelectedOptionId = table.Column<int>(type: "int", nullable: true),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: true),
                    HintUsed = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Practice__D48250043F262F2D", x => x.AnswerId);
                    table.ForeignKey(
                        name: "FK_PracticeAnswers_OptionQuestion",
                        columns: x => new { x.SelectedOptionId, x.QuestionId },
                        principalTable: "QuestionOptions",
                        principalColumns: new[] { "OptionId", "QuestionId" });
                    table.ForeignKey(
                        name: "FK_PracticeAnswers_QuestionSection",
                        columns: x => new { x.QuestionId, x.SectionId },
                        principalTable: "Questions",
                        principalColumns: new[] { "QuestionId", "SectionId" });
                    table.ForeignKey(
                        name: "FK_PracticeAnswers_SessionSection",
                        columns: x => new { x.SessionId, x.SectionId },
                        principalTable: "PracticeSessions",
                        principalColumns: new[] { "SessionId", "SectionId" });
                    table.ForeignKey(
                        name: "FK__PracticeA__Quest__6CD828CA",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "QuestionId");
                    table.ForeignKey(
                        name: "FK__PracticeA__Sessi__6BE40491",
                        column: x => x.SessionId,
                        principalTable: "PracticeSessions",
                        principalColumn: "SessionId");
                });

            migrationBuilder.CreateTable(
                name: "Recommendations",
                columns: table => new
                {
                    RecId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CertId = table.Column<int>(type: "int", nullable: false),
                    GradingId = table.Column<int>(type: "int", nullable: true),
                    InsightId = table.Column<int>(type: "int", nullable: true),
                    AttemptId = table.Column<int>(type: "int", nullable: true),
                    PracticeSessionId = table.Column<int>(type: "int", nullable: true),
                    SkillId = table.Column<int>(type: "int", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Severity = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false, defaultValue: "info"),
                    TargetBandCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "(sysdatetimeoffset())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Recommen__360414DF9A660680", x => x.RecId);
                    table.ForeignKey(
                        name: "FK_Reco_AttemptUser",
                        columns: x => new { x.AttemptId, x.UserId },
                        principalTable: "ExamAttempts",
                        principalColumns: new[] { "AttemptId", "UserId" });
                    table.ForeignKey(
                        name: "FK_Reco_InsightUser",
                        columns: x => new { x.InsightId, x.UserId },
                        principalTable: "AiInsights",
                        principalColumns: new[] { "InsightId", "UserId" });
                    table.ForeignKey(
                        name: "FK_Reco_SessionUser",
                        columns: x => new { x.PracticeSessionId, x.UserId },
                        principalTable: "PracticeSessions",
                        principalColumns: new[] { "SessionId", "UserId" });
                    table.ForeignKey(
                        name: "FK_Reco_SkillCert",
                        columns: x => new { x.SkillId, x.CertId },
                        principalTable: "Skills",
                        principalColumns: new[] { "SkillId", "CertId" });
                    table.ForeignKey(
                        name: "FK_Reco_TargetBand",
                        columns: x => new { x.CertId, x.TargetBandCode },
                        principalTable: "ScoreBands",
                        principalColumns: new[] { "CertId", "Code" });
                    table.ForeignKey(
                        name: "FK__Recommend__CertI__6225902D",
                        column: x => x.CertId,
                        principalTable: "Certificates",
                        principalColumn: "CertId");
                    table.ForeignKey(
                        name: "FK__Recommend__Gradi__6319B466",
                        column: x => x.GradingId,
                        principalTable: "AiGradings",
                        principalColumn: "GradingId");
                    table.ForeignKey(
                        name: "FK__Recommend__UserI__61316BF4",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiGradings_FreeResponseId_SkillId",
                table: "AiGradings",
                columns: new[] { "FreeResponseId", "SkillId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiGradings_Queue",
                table: "AiGradings",
                columns: new[] { "Status", "RequestedAt" },
                filter: "([Status] IN ('pending', 'failed'))");

            migrationBuilder.CreateIndex(
                name: "UQ_AiGradings_FreeResponse",
                table: "AiGradings",
                column: "FreeResponseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AiGradings_Id_Skill",
                table: "AiGradings",
                columns: new[] { "GradingId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiGradingScores_CriterionId_SkillId",
                table: "AiGradingScores",
                columns: new[] { "CriterionId", "SkillId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiGradingScores_GradingId_SkillId",
                table: "AiGradingScores",
                columns: new[] { "GradingId", "SkillId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiInsights_AttemptId_UserId",
                table: "AiInsights",
                columns: new[] { "AttemptId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiInsights_CertId",
                table: "AiInsights",
                column: "CertId");

            migrationBuilder.CreateIndex(
                name: "IX_AiInsights_Latest",
                table: "AiInsights",
                columns: new[] { "UserId", "CertId", "Scope", "CreatedAt" },
                descending: new[] { false, false, false, true },
                filter: "([Status]='done')");

            migrationBuilder.CreateIndex(
                name: "IX_AiInsights_LearnerPathId_UserId",
                table: "AiInsights",
                columns: new[] { "LearnerPathId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiInsights_PracticeSessionId_UserId",
                table: "AiInsights",
                columns: new[] { "PracticeSessionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiInsights_Queue",
                table: "AiInsights",
                columns: new[] { "Status", "CreatedAt" },
                filter: "([Status]='pending')");

            migrationBuilder.CreateIndex(
                name: "UQ_AiInsights_Id_User",
                table: "AiInsights",
                columns: new[] { "InsightId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AiInsights_Fingerprint",
                table: "AiInsights",
                columns: new[] { "UserId", "Scope", "SourceFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttemptAnswers_AttemptId_ExamId",
                table: "AttemptAnswers",
                columns: new[] { "AttemptId", "ExamId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttemptAnswers_Question",
                table: "AttemptAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttemptAnswers_QuestionId_ExamId",
                table: "AttemptAnswers",
                columns: new[] { "QuestionId", "ExamId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttemptAnswers_SelectedOptionId_QuestionId",
                table: "AttemptAnswers",
                columns: new[] { "SelectedOptionId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "UQ_AttemptAnswers",
                table: "AttemptAnswers",
                columns: new[] { "AttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttemptSkillScores_AttemptId_CertId",
                table: "AttemptSkillScores",
                columns: new[] { "AttemptId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttemptSkillScores_SkillId_CertId",
                table: "AttemptSkillScores",
                columns: new[] { "SkillId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Mentor",
                table: "Bookings",
                column: "MentorId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_MentorId_MentorType",
                table: "Bookings",
                columns: new[] { "MentorId", "MentorType" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_SlotId_MentorId",
                table: "Bookings",
                columns: new[] { "SlotId", "MentorId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_User",
                table: "Bookings",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_Bookings_Id_Mentor",
                table: "Bookings",
                columns: new[] { "BookingId", "MentorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Bookings_Id_User",
                table: "Bookings",
                columns: new[] { "BookingId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Bookings_OnePerSlot",
                table: "Bookings",
                column: "SlotId",
                unique: true,
                filter: "([SlotId] IS NOT NULL AND [Status]<>'cancelled')");

            migrationBuilder.CreateIndex(
                name: "UQ_Certificates_Code",
                table: "Certificates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_AttemptId_UserId",
                table: "Complaints",
                columns: new[] { "AttemptId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_BookingId_UserId",
                table: "Complaints",
                columns: new[] { "BookingId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_OrderId_UserId",
                table: "Complaints",
                columns: new[] { "OrderId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_ResolvedByGrantTxnId_UserId",
                table: "Complaints",
                columns: new[] { "ResolvedByGrantTxnId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_ResolvedByUserId",
                table: "Complaints",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_Status",
                table: "Complaints",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_User",
                table: "Complaints",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_AdminUserId",
                table: "CreditTransactions",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_AttemptId_UserId",
                table: "CreditTransactions",
                columns: new[] { "AttemptId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_BookingId_UserId",
                table: "CreditTransactions",
                columns: new[] { "BookingId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_LearnerPathId_UserId",
                table: "CreditTransactions",
                columns: new[] { "LearnerPathId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_OrderId_UserId",
                table: "CreditTransactions",
                columns: new[] { "OrderId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditTxn_User",
                table: "CreditTransactions",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_CreditTxn_Id_User",
                table: "CreditTransactions",
                columns: new[] { "TxnId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CreditTxn_Attempt_Reason",
                table: "CreditTransactions",
                columns: new[] { "AttemptId", "Reason" },
                unique: true,
                filter: "([AttemptId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "UX_CreditTxn_Booking",
                table: "CreditTransactions",
                columns: new[] { "BookingId", "Reason" },
                unique: true,
                filter: "([BookingId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "UX_CreditTxn_Order",
                table: "CreditTransactions",
                column: "OrderId",
                unique: true,
                filter: "([OrderId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "UX_CreditTxn_Path",
                table: "CreditTransactions",
                column: "LearnerPathId",
                unique: true,
                filter: "([LearnerPathId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_User",
                table: "EmailVerificationTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_Exam",
                table: "ExamAttempts",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_User",
                table: "ExamAttempts",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_CertId",
                table: "ExamAttempts",
                column: "CertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_ExamId_CertId",
                table: "ExamAttempts",
                columns: new[] { "ExamId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_LastQuestionId_ExamId",
                table: "ExamAttempts",
                columns: new[] { "LastQuestionId", "ExamId" });

            migrationBuilder.CreateIndex(
                name: "UQ_Attempts_Id_Cert",
                table: "ExamAttempts",
                columns: new[] { "AttemptId", "CertId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Attempts_Id_Exam",
                table: "ExamAttempts",
                columns: new[] { "AttemptId", "ExamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Attempts_Id_User",
                table: "ExamAttempts",
                columns: new[] { "AttemptId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exams_Cert_Status",
                table: "Exams",
                columns: new[] { "CertId", "Purpose", "Status" },
                filter: "([IsDeleted]=(0))");

            migrationBuilder.CreateIndex(
                name: "UQ_Exams_Id_Cert",
                table: "Exams",
                columns: new[] { "ExamId", "CertId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Exams_Id_Cert_Purpose",
                table: "Exams",
                columns: new[] { "ExamId", "CertId", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Exams_Cert_Name",
                table: "Exams",
                columns: new[] { "CertId", "Name" },
                unique: true,
                filter: "([IsDeleted]=(0))");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSectionAudio_ExamId_CertId",
                table: "ExamSectionAudio",
                columns: new[] { "ExamId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamSectionAudio_SectionId_CertId",
                table: "ExamSectionAudio",
                columns: new[] { "SectionId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeResponses_AttemptId_ExamId",
                table: "FreeResponses",
                columns: new[] { "AttemptId", "ExamId" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeResponses_Pending",
                table: "FreeResponses",
                column: "SubmittedAt",
                filter: "([Status]='pending_ai')");

            migrationBuilder.CreateIndex(
                name: "IX_FreeResponses_Question",
                table: "FreeResponses",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_FreeResponses_QuestionId_ExamId",
                table: "FreeResponses",
                columns: new[] { "QuestionId", "ExamId" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeResponses_QuestionId_QuestionType",
                table: "FreeResponses",
                columns: new[] { "QuestionId", "QuestionType" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeResponses_QuestionId_SkillId",
                table: "FreeResponses",
                columns: new[] { "QuestionId", "SkillId" });

            migrationBuilder.CreateIndex(
                name: "UQ_FreeResp_Id_Skill",
                table: "FreeResponses",
                columns: new[] { "FreeResponseId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_FreeResponses",
                table: "FreeResponses",
                columns: new[] { "AttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradingCriteria_CertId",
                table: "GradingCriteria",
                column: "CertId");

            migrationBuilder.CreateIndex(
                name: "IX_GradingCriteria_SkillId_CertId",
                table: "GradingCriteria",
                columns: new[] { "SkillId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "UQ_Criteria_Skill_Code",
                table: "GradingCriteria",
                columns: new[] { "SkillId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_GradingCriteria_Id_Skill",
                table: "GradingCriteria",
                columns: new[] { "CriterionId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_CertId",
                table: "ImportBatches",
                column: "CertId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_UploadedBy",
                table: "ImportBatches",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ImportLog_Batch",
                table: "ImportLog",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerLessonProgress_LastViewedCardId",
                table: "LearnerLessonProgress",
                column: "LastViewedCardId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerLessonProgress_LessonId_CertId",
                table: "LearnerLessonProgress",
                columns: new[] { "LessonId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "UQ_LLP_UserLesson",
                table: "LearnerLessonProgress",
                columns: new[] { "UserId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPaths_CertId",
                table: "LearnerPaths",
                column: "CertId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPaths_PlacementId_UserId",
                table: "LearnerPaths",
                columns: new[] { "PlacementId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPaths_TemplateId_CertId",
                table: "LearnerPaths",
                columns: new[] { "TemplateId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPaths_User",
                table: "LearnerPaths",
                columns: new[] { "UserId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "UQ_LearnerPaths_Id_Template",
                table: "LearnerPaths",
                columns: new[] { "LearnerPathId", "TemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_LearnerPaths_Id_User",
                table: "LearnerPaths",
                columns: new[] { "LearnerPathId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LearnerPaths_OneActive",
                table: "LearnerPaths",
                columns: new[] { "UserId", "CertId" },
                unique: true,
                filter: "([Status]='active')");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPathSteps_LearnerPathId_TemplateId",
                table: "LearnerPathSteps",
                columns: new[] { "LearnerPathId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPathSteps_PathModuleItemId_TemplateId",
                table: "LearnerPathSteps",
                columns: new[] { "PathModuleItemId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_Steps_LearnerPath",
                table: "LearnerPathSteps",
                column: "LearnerPathId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonCards_LessonId_CertId",
                table: "LessonCards",
                columns: new[] { "LessonId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "UQ_LessonCards_Id_Cert",
                table: "LessonCards",
                columns: new[] { "CardId", "CertId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_CategoryId",
                table: "Lessons",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_CertId",
                table: "Lessons",
                column: "CertId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_SkillId_CertId",
                table: "Lessons",
                columns: new[] { "SkillId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "UQ_Lessons_Id_Cert",
                table: "Lessons",
                columns: new[] { "LessonId", "CertId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MentorPayoutItems_BookingId_MentorId",
                table: "MentorPayoutItems",
                columns: new[] { "BookingId", "MentorId" });

            migrationBuilder.CreateIndex(
                name: "IX_MentorPayoutItems_PayoutId_MentorId",
                table: "MentorPayoutItems",
                columns: new[] { "PayoutId", "MentorId" });

            migrationBuilder.CreateIndex(
                name: "UQ_PayoutItems_Booking",
                table: "MentorPayoutItems",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MentorPayouts_Mentor",
                table: "MentorPayouts",
                column: "MentorId");

            migrationBuilder.CreateIndex(
                name: "UQ_MentorPayouts_Id_Mentor",
                table: "MentorPayouts",
                columns: new[] { "PayoutId", "MentorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MentorProfiles_SkillId_CertId",
                table: "MentorProfiles",
                columns: new[] { "SkillId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_MentorProfiles_UserId",
                table: "MentorProfiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Mentors_Cert",
                table: "MentorProfiles",
                column: "CertId",
                filter: "([IsActive]=(1))");

            migrationBuilder.CreateIndex(
                name: "UQ_MentorProfiles_Id_Type",
                table: "MentorProfiles",
                columns: new[] { "MentorId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MentorSlots_Mentor",
                table: "MentorSlots",
                columns: new[] { "MentorId", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_MentorSlots_Id_Mentor",
                table: "MentorSlots",
                columns: new[] { "SlotId", "MentorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Open",
                table: "Orders",
                column: "CreatedAt",
                filter: "([Status] IN ('pending', 'reconciling'))");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PackageId",
                table: "Orders",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_User",
                table: "Orders",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_Orders_Id_User",
                table: "Orders",
                columns: new[] { "OrderId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Orders_OrderCode",
                table: "Orders",
                column: "OrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Orders_PaymentNo",
                table: "Orders",
                column: "PaymentNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Orders_GatewayTxnRef",
                table: "Orders",
                columns: new[] { "Gateway", "GatewayTxnRef" },
                unique: true,
                filter: "([GatewayTxnRef] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "UX_Orders_OnePendingPerUser",
                table: "Orders",
                column: "UserId",
                unique: true,
                filter: "([Status] IN ('pending', 'reconciling'))");

            migrationBuilder.CreateIndex(
                name: "UX_Packages_OneFeatured",
                table: "Packages",
                column: "IsFeatured",
                unique: true,
                filter: "([IsFeatured]=(1))");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_User",
                table: "PasswordResetTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PathModuleItems_ModuleId_CertId",
                table: "PathModuleItems",
                columns: new[] { "ModuleId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_PathModuleItems_ModuleId_TemplateId",
                table: "PathModuleItems",
                columns: new[] { "ModuleId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_PathModuleItems_RefExamId_CertId",
                table: "PathModuleItems",
                columns: new[] { "RefExamId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_PathModuleItems_RefLessonId_CertId",
                table: "PathModuleItems",
                columns: new[] { "RefLessonId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_PathModuleItems_RefSectionId_CertId",
                table: "PathModuleItems",
                columns: new[] { "RefSectionId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_PMI_Module",
                table: "PathModuleItems",
                columns: new[] { "ModuleId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "UQ_PMI_Id_Template",
                table: "PathModuleItems",
                columns: new[] { "ItemId", "TemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PathModules_SkillId_CertId",
                table: "PathModules",
                columns: new[] { "SkillId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_PathModules_Template",
                table: "PathModules",
                columns: new[] { "TemplateId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PathModules_TemplateId_CertId",
                table: "PathModules",
                columns: new[] { "TemplateId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "UQ_PathModules_Id_Cert",
                table: "PathModules",
                columns: new[] { "ModuleId", "CertId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_PathModules_Id_Template",
                table: "PathModules",
                columns: new[] { "ModuleId", "TemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PathModuleTopics_CategoryId",
                table: "PathModuleTopics",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PMT_Module",
                table: "PathModuleTopics",
                columns: new[] { "ModuleId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PathTemplates_CertId_FromBandCode",
                table: "PathTemplates",
                columns: new[] { "CertId", "FromBandCode" });

            migrationBuilder.CreateIndex(
                name: "IX_PathTemplates_CertId_ToBandCode",
                table: "PathTemplates",
                columns: new[] { "CertId", "ToBandCode" });

            migrationBuilder.CreateIndex(
                name: "UQ_PathTemplates_Id_Cert",
                table: "PathTemplates",
                columns: new[] { "TemplateId", "CertId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_Order",
                table: "PaymentEvents",
                columns: new[] { "OrderId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Placement_User",
                table: "PlacementTests",
                columns: new[] { "UserId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlacementTests_AttemptId_UserId",
                table: "PlacementTests",
                columns: new[] { "AttemptId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlacementTests_CertId",
                table: "PlacementTests",
                column: "CertId");

            migrationBuilder.CreateIndex(
                name: "IX_PlacementTests_ExamId_CertId_ExamPurpose",
                table: "PlacementTests",
                columns: new[] { "ExamId", "CertId", "ExamPurpose" });

            migrationBuilder.CreateIndex(
                name: "UQ_PlacementTests_Id_User",
                table: "PlacementTests",
                columns: new[] { "PlacementId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PlacementTests_Attempt",
                table: "PlacementTests",
                column: "AttemptId",
                unique: true,
                filter: "([AttemptId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeAnswers_Question",
                table: "PracticeAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeAnswers_QuestionId_SectionId",
                table: "PracticeAnswers",
                columns: new[] { "QuestionId", "SectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeAnswers_SelectedOptionId_QuestionId",
                table: "PracticeAnswers",
                columns: new[] { "SelectedOptionId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeAnswers_SessionId_SectionId",
                table: "PracticeAnswers",
                columns: new[] { "SessionId", "SectionId" });

            migrationBuilder.CreateIndex(
                name: "UQ_PracticeAnswers",
                table: "PracticeAnswers",
                columns: new[] { "SessionId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Practice_PathStep",
                table: "PracticeSessions",
                column: "LearnerPathStepId",
                filter: "([LearnerPathStepId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Practice_User",
                table: "PracticeSessions",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_SectionId_Modality",
                table: "PracticeSessions",
                columns: new[] { "SectionId", "Modality" });

            migrationBuilder.CreateIndex(
                name: "UQ_PracticeSessions_Id_Section",
                table: "PracticeSessions",
                columns: new[] { "SessionId", "SectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_PracticeSessions_Id_User",
                table: "PracticeSessions",
                columns: new[] { "SessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_QuestionCategories_Cert_Code",
                table: "QuestionCategories",
                columns: new[] { "CertId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QGroups_Exam",
                table: "QuestionGroups",
                columns: new[] { "ExamId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_QGroups_Section",
                table: "QuestionGroups",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionGroups_ExamId_CertId",
                table: "QuestionGroups",
                columns: new[] { "ExamId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionGroups_SectionId_CertId",
                table: "QuestionGroups",
                columns: new[] { "SectionId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "UQ_QGroups_Id_Exam",
                table: "QuestionGroups",
                columns: new[] { "GroupId", "ExamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_QGroups_Id_Section",
                table: "QuestionGroups",
                columns: new[] { "GroupId", "SectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionOptions_QuestionId_QuestionType",
                table: "QuestionOptions",
                columns: new[] { "QuestionId", "QuestionType" });

            migrationBuilder.CreateIndex(
                name: "UQ_Options_Id_Question",
                table: "QuestionOptions",
                columns: new[] { "OptionId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Options_Question_Label",
                table: "QuestionOptions",
                columns: new[] { "QuestionId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Options_OneCorrect",
                table: "QuestionOptions",
                column: "QuestionId",
                unique: true,
                filter: "([IsCorrect]=(1))");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_Category",
                table: "Questions",
                column: "CategoryId",
                filter: "([CategoryId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_Group",
                table: "Questions",
                columns: new[] { "GroupId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_GroupId_ExamId",
                table: "Questions",
                columns: new[] { "GroupId", "ExamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_GroupId_SectionId",
                table: "Questions",
                columns: new[] { "GroupId", "SectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_Section",
                table: "Questions",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_SectionId_SkillId",
                table: "Questions",
                columns: new[] { "SectionId", "SkillId" });

            migrationBuilder.CreateIndex(
                name: "UQ_Questions_Id_Exam",
                table: "Questions",
                columns: new[] { "QuestionId", "ExamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Questions_Id_Section",
                table: "Questions",
                columns: new[] { "QuestionId", "SectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Questions_Id_Skill",
                table: "Questions",
                columns: new[] { "QuestionId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Questions_Id_Type",
                table: "Questions",
                columns: new[] { "QuestionId", "QuestionType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_Attempt",
                table: "Recommendations",
                column: "AttemptId",
                filter: "([AttemptId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_AttemptId_UserId",
                table: "Recommendations",
                columns: new[] { "AttemptId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_CertId_TargetBandCode",
                table: "Recommendations",
                columns: new[] { "CertId", "TargetBandCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_GradingId",
                table: "Recommendations",
                column: "GradingId");

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_InsightId_UserId",
                table: "Recommendations",
                columns: new[] { "InsightId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_PracticeSessionId_UserId",
                table: "Recommendations",
                columns: new[] { "PracticeSessionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_Session",
                table: "Recommendations",
                column: "PracticeSessionId",
                filter: "([PracticeSessionId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_SkillId_CertId",
                table: "Recommendations",
                columns: new[] { "SkillId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_User",
                table: "Recommendations",
                columns: new[] { "UserId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_BookingId_UserId",
                table: "Reviews",
                columns: new[] { "BookingId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_UserId",
                table: "Reviews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_Reviews_OnePerBooking",
                table: "Reviews",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Roles_Code",
                table: "Roles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedQuestions_QuestionId",
                table: "SavedQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "UQ_ScoreBands_Cert_Code",
                table: "ScoreBands",
                columns: new[] { "CertId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScoreScales_ExamId_CertId",
                table: "ScoreScales",
                columns: new[] { "ExamId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScoreScales_SkillId_CertId",
                table: "ScoreScales",
                columns: new[] { "SkillId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "UQ_ScoreScales_Cert_Skill_Exam_Raw",
                table: "ScoreScales",
                columns: new[] { "CertId", "SkillId", "ExamId", "RawScore" },
                unique: true,
                filter: "[SkillId] IS NOT NULL AND [ExamId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SectionCriteria_CriterionId_SkillId",
                table: "SectionCriteria",
                columns: new[] { "CriterionId", "SkillId" });

            migrationBuilder.CreateIndex(
                name: "IX_SectionCriteria_SectionId_SkillId",
                table: "SectionCriteria",
                columns: new[] { "SectionId", "SkillId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sections_SkillId_CertId",
                table: "Sections",
                columns: new[] { "SkillId", "CertId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sections_SkillId_Modality",
                table: "Sections",
                columns: new[] { "SkillId", "Modality" });

            migrationBuilder.CreateIndex(
                name: "UQ_Sections_Id_Cert",
                table: "Sections",
                columns: new[] { "SectionId", "CertId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Sections_Id_Modality",
                table: "Sections",
                columns: new[] { "SectionId", "Modality" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Sections_Id_Skill",
                table: "Sections",
                columns: new[] { "SectionId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Sections_Skill_Code",
                table: "Sections",
                columns: new[] { "SkillId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Skills_Cert_Code",
                table: "Skills",
                columns: new[] { "CertId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Skills_Id_Cert",
                table: "Skills",
                columns: new[] { "SkillId", "CertId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Skills_Id_Modality",
                table: "Skills",
                columns: new[] { "SkillId", "Modality" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TargetBands_CertId_TargetBandCode",
                table: "TargetBands",
                columns: new[] { "CertId", "TargetBandCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_ActiveCertId",
                table: "Users",
                column: "ActiveCertId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "UX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "([IsDeleted]=(0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiGradingScores");

            migrationBuilder.DropTable(
                name: "AttemptAnswers");

            migrationBuilder.DropTable(
                name: "AttemptSkillScores");

            migrationBuilder.DropTable(
                name: "Complaints");

            migrationBuilder.DropTable(
                name: "EmailVerificationTokens");

            migrationBuilder.DropTable(
                name: "ExamSectionAudio");

            migrationBuilder.DropTable(
                name: "ImportLog");

            migrationBuilder.DropTable(
                name: "LearnerLessonProgress");

            migrationBuilder.DropTable(
                name: "MentorPayoutItems");

            migrationBuilder.DropTable(
                name: "PasswordResetTokens");

            migrationBuilder.DropTable(
                name: "PathModuleTopics");

            migrationBuilder.DropTable(
                name: "PaymentEvents");

            migrationBuilder.DropTable(
                name: "PracticeAnswers");

            migrationBuilder.DropTable(
                name: "Recommendations");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropTable(
                name: "SavedQuestions");

            migrationBuilder.DropTable(
                name: "ScoreScales");

            migrationBuilder.DropTable(
                name: "SectionCriteria");

            migrationBuilder.DropTable(
                name: "TargetBands");

            migrationBuilder.DropTable(
                name: "CreditTransactions");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "LessonCards");

            migrationBuilder.DropTable(
                name: "MentorPayouts");

            migrationBuilder.DropTable(
                name: "QuestionOptions");

            migrationBuilder.DropTable(
                name: "AiInsights");

            migrationBuilder.DropTable(
                name: "AiGradings");

            migrationBuilder.DropTable(
                name: "GradingCriteria");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "PracticeSessions");

            migrationBuilder.DropTable(
                name: "FreeResponses");

            migrationBuilder.DropTable(
                name: "MentorSlots");

            migrationBuilder.DropTable(
                name: "Packages");

            migrationBuilder.DropTable(
                name: "LearnerPathSteps");

            migrationBuilder.DropTable(
                name: "MentorProfiles");

            migrationBuilder.DropTable(
                name: "PathModuleItems");

            migrationBuilder.DropTable(
                name: "LearnerPaths");

            migrationBuilder.DropTable(
                name: "Lessons");

            migrationBuilder.DropTable(
                name: "PathModules");

            migrationBuilder.DropTable(
                name: "PlacementTests");

            migrationBuilder.DropTable(
                name: "PathTemplates");

            migrationBuilder.DropTable(
                name: "ExamAttempts");

            migrationBuilder.DropTable(
                name: "ScoreBands");

            migrationBuilder.DropTable(
                name: "Questions");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "QuestionCategories");

            migrationBuilder.DropTable(
                name: "QuestionGroups");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Exams");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "Skills");

            migrationBuilder.DropTable(
                name: "Certificates");

            migrationBuilder.DropSequence(
                name: "SEQ_PaymentNo");
        }
    }
}
