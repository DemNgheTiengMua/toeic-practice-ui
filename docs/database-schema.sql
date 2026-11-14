-- docs/database-schema.sql
-- Multi-certificate mock-exam companion schema.
-- Spec: docs/superpowers/specs/2026-09-24-multi-cert-companion-design.md
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- ============ Domain 2: Catalog ============
CREATE TABLE Certificates (
  CertId               INT IDENTITY(1,1) PRIMARY KEY,
  Code                 VARCHAR(20)  NOT NULL,
  Name                 NVARCHAR(120) NOT NULL,
  Description          NVARCHAR(500) NULL,
  IsActive             BIT NOT NULL DEFAULT 1,
  -- #171: three rules, each of which produces a total. `independent` was removed: it returned a
  -- total of 0 for every attempt and the trigger that checks a total against its skills exempted it,
  -- so a certificate could be authored whose learners could never receive a score.
  ScoreAggregationRule NVARCHAR(30) NOT NULL CONSTRAINT DF_Certificates_ScoreAggregationRule DEFAULT 'sum'
    CONSTRAINT CK_Certificates_ScoreAggregationRule CHECK (ScoreAggregationRule IN ('sum', 'avg_half_step', 'avg_whole')),
  PresetCode           NVARCHAR(50) NULL,
  -- #161: the noun this certificate puts in front of a section code ("Part 3" on TOEIC, "Section 3" on
  -- anything else). A label, so it is data - the views used to switch on the certificate's code. The
  -- default is the domain's neutral word (CONTEXT.md: Section), so a new certificate reads correctly
  -- before anyone edits it.
  SectionNoun          NVARCHAR(30) NOT NULL CONSTRAINT DF_Certificates_SectionNoun DEFAULT 'Section',
  CONSTRAINT UQ_Certificates_Code UNIQUE (Code)
);
GO
CREATE TABLE Skills (
  SkillId      INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  Modality     VARCHAR(10) NOT NULL
    CONSTRAINT CK_Skills_Modality CHECK (Modality IN ('listening','reading','speaking','writing')),
  DisplayOrder INT NOT NULL DEFAULT 0,
  -- UI-18: "Listening 45 minutes, Reading 75 minutes" on exam instructions / exam editor
  DurationMinutes INT NULL CONSTRAINT CK_Skills_Duration CHECK (DurationMinutes IS NULL OR DurationMinutes > 0),
  -- E1.1 (#146): the skill's own scaled-score range and rounding grid - the scale is data, not a guess
  -- from the certificate's name. NULL until configured; scoring a skill with NULL parameters is a
  -- configuration error (never a guessed range). Backfilled from ScoreScales by docs/seed-demo.sql.
  MinScaledScore INT NULL CONSTRAINT CK_Skills_MinScaledScore CHECK (MinScaledScore IS NULL OR MinScaledScore >= 0),
  MaxScaledScore INT NULL CONSTRAINT CK_Skills_MaxScaledScore CHECK (MaxScaledScore IS NULL OR MaxScaledScore >= 0),
  ScoreStep      INT NULL CONSTRAINT CK_Skills_ScoreStep CHECK (ScoreStep IS NULL OR ScoreStep > 0),
  -- E1.4 (#149): a skill that can only be answered forwards (one listening pass, no rewinding) and
  -- therefore offers no review screen. Was `skillCode === 'LIST'` in navigation.js; the behaviour is
  -- now a property of the skill. Backfilled for the seeded listening skills in docs/seed-demo.sql.
  LockNavigationOneWay BIT NOT NULL CONSTRAINT DF_Skills_LockNavigationOneWay DEFAULT 0,
  CONSTRAINT CK_Skills_ScaleRange CHECK (
    MinScaledScore IS NULL OR MaxScaledScore IS NULL OR MinScaledScore < MaxScaledScore),
  CONSTRAINT UQ_Skills_Cert_Code UNIQUE (CertId, Code),
  -- composite target so Content can carry CertId down and pin isolation:
  CONSTRAINT UQ_Skills_Id_Cert UNIQUE (SkillId, CertId),
  -- M-18: composite target so Sections can carry Modality down from its skill
  CONSTRAINT UQ_Skills_Id_Modality UNIQUE (SkillId, Modality)
);
GO
CREATE TABLE Sections (
  SectionId                 INT IDENTITY(1,1) PRIMARY KEY,
  SkillId                   INT NOT NULL,
  CertId                    INT NOT NULL,
  -- M-18: carried down from the owning skill so PracticeSessions can pin listening/reading-only
  Modality                  VARCHAR(10) NOT NULL
    CONSTRAINT CK_Sections_Modality CHECK (Modality IN ('listening','reading','speaking','writing')),
  Code                      VARCHAR(20) NOT NULL,
  Name                      NVARCHAR(120) NOT NULL,
  ScoreWeight               DECIMAL(5,2) NOT NULL CONSTRAINT DF_Sections_ScoreWeight DEFAULT 1.0,
  NavigationMode            NVARCHAR(30) NOT NULL CONSTRAINT DF_Sections_NavigationMode DEFAULT 'FULL_SANDBOX'
    CONSTRAINT CK_Sections_NavigationMode CHECK (NavigationMode IN ('FULL_SANDBOX', 'UNIT_LOCKED_SANDBOX', 'AUDIO_DRIVEN', 'SYSTEM_PACED_AUTO', 'HYBRID_GROUP_LOCKED')),
  TimerPolicyJson           NVARCHAR(MAX) NULL,
  LayoutType                NVARCHAR(30) NOT NULL CONSTRAINT DF_Sections_LayoutType DEFAULT 'STANDARD_VERTICAL',
  OptionCount               INT NOT NULL CONSTRAINT DF_Sections_OptionCount DEFAULT 4
    CONSTRAINT CK_Sections_OptionCount CHECK (OptionCount BETWEEN 2 AND 6),
  ClusterSize               INT NOT NULL CONSTRAINT DF_Sections_ClusterSize DEFAULT 1,
  UnitCount                 INT NOT NULL CONSTRAINT DF_Sections_UnitCount DEFAULT 1,
  HideOptionText            BIT NOT NULL CONSTRAINT DF_Sections_HideOptionText DEFAULT 0,
  RequirePassage            BIT NOT NULL CONSTRAINT DF_Sections_RequirePassage DEFAULT 0,
  RequireAudio              BIT NOT NULL CONSTRAINT DF_Sections_RequireAudio DEFAULT 0,
  RequireImage              BIT NOT NULL CONSTRAINT DF_Sections_RequireImage DEFAULT 0,
  RequiredKeywordCount      INT NOT NULL CONSTRAINT DF_Sections_RequiredKeywordCount DEFAULT 0,
  AllowedComponentTypesJson NVARCHAR(MAX) NULL,
  ToolsConfigJson           NVARCHAR(MAX) NULL,
  -- UI-18: fixed per-Part question count ("Part 3 â€” 39 questions", editor progress "25 / 39")
  -- #157: NOT NULL, because a blank count made the publish audit skip SEC_QUESTION_COUNT_LOW and an
  -- empty part could go live. A section always knows how many questions it holds.
  ExpectedQuestionCount     INT NOT NULL CONSTRAINT CK_Sections_ExpectedCount CHECK (ExpectedQuestionCount > 0),
  DisplayOrder              INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Sections_Skill_Code UNIQUE (SkillId, Code),
  -- composite FK pins the section's cert to its skill's cert (isolation, enforced because CertId is NOT NULL):
  CONSTRAINT FK_Sections_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- M-18: Modality must be the skill's own (pins the literal via the FK)
  CONSTRAINT FK_Sections_SkillModality FOREIGN KEY (SkillId, Modality) REFERENCES Skills(SkillId, Modality),
  -- composite target so QuestionGroups can require exam-cert = section-cert:
  CONSTRAINT UQ_Sections_Id_Cert UNIQUE (SectionId, CertId),
  -- M-18: composite target so PracticeSessions can carry Modality down
  CONSTRAINT UQ_Sections_Id_Modality UNIQUE (SectionId, Modality),
  -- M-3: composite target so Questions can carry SkillId down (pins AI grading to the right skill)
  CONSTRAINT UQ_Sections_Id_Skill UNIQUE (SectionId, SkillId)
);
GO
-- ============ Domain 1: Identity ============
CREATE TABLE Roles (
  RoleId INT IDENTITY(1,1) PRIMARY KEY,
  Code   VARCHAR(20) NOT NULL CONSTRAINT UQ_Roles_Code UNIQUE,
  Name   NVARCHAR(60) NOT NULL
);
GO
-- D16 / ADM-01: staff authority is five composable namespaces, not job titles. The list is closed:
-- a sixth code would be a namespace nobody designed, and a typo in a policy name would otherwise
-- look exactly like "no account holds this". A role holds any combination of the five.
CREATE TABLE Permissions (
  Code VARCHAR(40) NOT NULL PRIMARY KEY
    CONSTRAINT CK_Permissions_Code CHECK (Code IN (
      'pool.content.author', 'pool.content.review', 'assessment.blueprint.design',
      'operations.support.triage', 'system.governance.admin'))
);
GO
CREATE TABLE RolePermissions (
  RoleId         INT NOT NULL CONSTRAINT FK_RolePermissions_Role REFERENCES Roles(RoleId),
  PermissionCode VARCHAR(40) NOT NULL CONSTRAINT FK_RolePermissions_Permission REFERENCES Permissions(Code),
  CONSTRAINT PK_RolePermissions PRIMARY KEY (RoleId, PermissionCode)
);
GO
CREATE TABLE Users (
  UserId       INT IDENTITY(1,1) PRIMARY KEY,
  RoleId       INT NOT NULL CONSTRAINT FK_Users_Role REFERENCES Roles(RoleId),
  Email        VARCHAR(255) NOT NULL,
  PasswordHash VARBINARY(256) NOT NULL,
  ActiveCertId INT NULL CONSTRAINT FK_Users_ActiveCert REFERENCES Certificates(CertId),
  -- UI-1: register/profile collect a name and phone; admin users list shows them
  FullName     NVARCHAR(120) NULL,
  Phone        VARCHAR(20)   NULL,
  -- UI-2: admin users list filters Active / Locked / Not activated and shows last sign-in
  IsLocked        BIT NOT NULL CONSTRAINT DF_Users_IsLocked DEFAULT 0,
  SecurityStamp   VARCHAR(36) NOT NULL CONSTRAINT DF_Users_SecurityStamp DEFAULT CONVERT(VARCHAR(36), NEWID()),
  FailedAttempts  INT NOT NULL CONSTRAINT DF_Users_FailedAttempts DEFAULT 0,
  LockoutEnd      DATETIMEOFFSET NULL,
  EmailVerifiedAt DATETIMEOFFSET NULL,   -- NULL = "Not activated"
  LastLoginAt     DATETIMEOFFSET NULL,   -- NULL = "Never signed in"
  TermsAcceptedAt DATETIMEOFFSET NULL,   -- UI-1: register "I agree to the terms of use"
  -- UI-52: profile picks a time zone; every screen renders stored UTC instants in it.
  -- IANA id (e.g. 'Asia/Ho_Chi_Minh'); the app offers a shortlist, not the full tz database.
  TimeZoneId   VARCHAR(64) NOT NULL CONSTRAINT DF_Users_TimeZoneId DEFAULT 'Asia/Ho_Chi_Minh',
  IsDeleted    BIT NOT NULL CONSTRAINT DF_Users_IsDeleted DEFAULT 0,
  CreatedAt    DATETIMEOFFSET NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSDATETIMEOFFSET()
);
GO
-- one account per live email (filtered so soft-deleted emails can be reused)
CREATE UNIQUE INDEX UX_Users_Email ON Users(Email) WHERE IsDeleted = 0;
GO
-- UI-2: admin "Resend email" re-issues an activation link (same shape as password reset)
CREATE TABLE EmailVerificationTokens (
  TokenId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId     INT NOT NULL CONSTRAINT FK_EmailVerificationTokens_User REFERENCES Users(UserId),
  TokenHash  VARBINARY(256) NOT NULL,
  ExpiresAt  DATETIMEOFFSET NOT NULL,
  UsedAt     DATETIMEOFFSET NULL,
  CreatedAt  DATETIMEOFFSET NOT NULL CONSTRAINT DF_EmailVerificationTokens_CreatedAt DEFAULT SYSDATETIMEOFFSET()
);
GO
CREATE TABLE PasswordResetTokens (
  TokenId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId     INT NOT NULL CONSTRAINT FK_PasswordResetTokens_User REFERENCES Users(UserId),
  TokenHash  VARBINARY(256) NOT NULL,
  ExpiresAt  DATETIMEOFFSET NOT NULL,
  UsedAt     DATETIMEOFFSET NULL,
  CreatedAt  DATETIMEOFFSET NOT NULL CONSTRAINT DF_PasswordResetTokens_CreatedAt DEFAULT SYSDATETIMEOFFSET()
);
GO
-- ============ Domain 4: Scoring ============
CREATE TABLE ScoreBands (
  BandId       INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(60) NOT NULL,
  MinTotal     INT NOT NULL,
  MaxTotal     INT NOT NULL,
  DisplayOrder INT NOT NULL DEFAULT 0,
  -- #153: hours of study it takes to reach this band, measured from zero. The goal formula subtracts
  -- two of these. NULL for certificates whose bands are not CEFR levels (CAT_B1, SW_B2, ...): the
  -- feasibility of a goal on such a certificate cannot be judged, and must not be invented.
  CumulativeStudyHours INT NULL
    CONSTRAINT CK_ScoreBands_StudyHours CHECK (CumulativeStudyHours IS NULL OR CumulativeStudyHours >= 0),
  CONSTRAINT CK_ScoreBands_Range CHECK (MaxTotal >= MinTotal),
  CONSTRAINT UQ_ScoreBands_Cert_Code UNIQUE (CertId, Code)
);
GO
-- M-10: no two bands within the same certificate may have overlapping [MinTotal,MaxTotal] ranges
-- (a CHECK can't compare across rows/tables, so this is a trigger).
CREATE TRIGGER trg_ScoreBands_NoOverlap
ON ScoreBands AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN ScoreBands b
      ON b.CertId = i.CertId
     AND b.BandId <> i.BandId
     AND i.MinTotal <= b.MaxTotal
     AND i.MaxTotal >= b.MinTotal
  )
    THROW 50027, 'ScoreBands ranges may not overlap within a certificate.', 1;
END;
GO
CREATE TABLE ScoreScales (
  ScaleId     INT IDENTITY(1,1) PRIMARY KEY,
  CertId      INT NOT NULL REFERENCES Certificates(CertId),
  SkillId     INT NULL,   -- NULL = total
  ExamId      INT NULL,   -- NULL = default; FK added below
  RawScore    INT NOT NULL,
  ScaledScore INT NOT NULL,
  -- M-11: a (cert, skill, exam, raw score) mapping must be unambiguous
  CONSTRAINT UQ_ScoreScales_Cert_Skill_Exam_Raw UNIQUE (CertId, SkillId, ExamId, RawScore),
  -- M-11: the skill must belong to this same certificate (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_ScoreScales_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId)
);
GO
-- ============ Domain 3: Content ============
-- (Sections already carries CertId + composite FK from Task 1)
CREATE TABLE Exams (
  ExamId          INT IDENTITY(1,1) PRIMARY KEY,
  CertId          INT NOT NULL REFERENCES Certificates(CertId),
  Name            NVARCHAR(160) NOT NULL,
  Status          VARCHAR(12) NOT NULL DEFAULT 'draft'
    CONSTRAINT CK_Exams_Status CHECK (Status IN ('draft','published','hidden')),  -- UI-4: admin labels
  DurationMinutes INT NOT NULL CONSTRAINT CK_Exams_Duration CHECK (DurationMinutes > 0),
  -- UI-3: placement-select lists placement tests apart from mock exams; speaking practice is its own kind
  -- UI-38: so is writing practice (writing-instructions / exam-writing / writing-review)
  Purpose         VARCHAR(20) NOT NULL CONSTRAINT DF_Exams_Purpose DEFAULT 'mock'
    CONSTRAINT CK_Exams_Purpose CHECK (Purpose IN ('mock','placement','speaking_practice','writing_practice')),
  DeliveryMode    NVARCHAR(30) NOT NULL CONSTRAINT DF_Exams_DeliveryMode DEFAULT 'strict_exam'
    CONSTRAINT CK_Exams_DeliveryMode CHECK (DeliveryMode IN ('strict_exam', 'practice')),
  ExamFamily      NVARCHAR(50) NULL,
  Description     NVARCHAR(500) NULL,     -- UI-3: placement card copy (see the seeded exam's description)
  SourceName      NVARCHAR(120) NULL,     -- UI-4: exam editor "Test set / source" (e.g. ETS 2024)
  InternalNote    NVARCHAR(1000) NULL,    -- UI-4: exam editor note, admin only
  -- UI-45: placement-select card meta "40 questions Â· 30 minutes"; kept on the exam because the
  -- question bank can grow past the fixed count (an adaptive form draws a subset)
  QuestionCount   INT NULL CONSTRAINT CK_Exams_QuestionCount CHECK (QuestionCount IS NULL OR QuestionCount > 0),
  -- #185 / INV-6: which tier of stock this paper may draw from. Defaults to 'demo' for the same
  -- reason Items.ContentTier does: an exam is not production until someone promotes it, and the
  -- wrong default here is what lets an unreviewed paper go on sale.
  ContentTier     VARCHAR(12) NOT NULL CONSTRAINT DF_Exams_ContentTier DEFAULT 'demo'
    CONSTRAINT CK_Exams_ContentTier CHECK (ContentTier IN ('production','demo')),
  -- #227: which edition of this paper this row IS. One row is one edition - a structural
  -- repair forks a new Exam row rather than mutating this one, which is what makes every
  -- historical attempt's structure, timers and chrome stay exactly as they were (see
  -- ExamEditions below for the facts that live above the exam and therefore had to be stored).
  EditionNumber   INT NOT NULL CONSTRAINT DF_Exams_EditionNumber DEFAULT 1
    CONSTRAINT CK_Exams_EditionNumber CHECK (EditionNumber > 0),
  -- The edition this one replaced. NULL for a paper that was never forked.
  SupersedesExamId INT NULL CONSTRAINT FK_Exams_Supersedes REFERENCES Exams(ExamId),
  IsDeleted       BIT NOT NULL DEFAULT 0,
  CreatedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  UpdatedAt       DATETIMEOFFSET NULL,    -- UI-4: admin exam list "Update" column
  CONSTRAINT UQ_Exams_Id_Cert UNIQUE (ExamId, CertId),   -- composite target for isolation
  -- UI-3: composite target so PlacementTests can require a placement-purpose exam
  CONSTRAINT UQ_Exams_Id_Cert_Purpose UNIQUE (ExamId, CertId, Purpose)
);
GO
-- UI-4: exam editor rejects "That test name already exists"
CREATE UNIQUE INDEX UX_Exams_Cert_Name ON Exams(CertId, Name) WHERE IsDeleted = 0;
GO
-- now that Exams exists, pin ScoreScales.ExamId
-- M-11: composite FK so the exam must belong to the same certificate (replaces the single-column FK)
ALTER TABLE ScoreScales ADD CONSTRAINT FK_ScoreScales_Exam FOREIGN KEY (ExamId, CertId) REFERENCES Exams(ExamId, CertId);
GO
-- UI-6: exam editor uploads one audio file per Listening Part (4 files, "Uploaded 3 / 4")
CREATE TABLE ExamSectionAudio (
  ExamId       INT NOT NULL,
  SectionId    INT NOT NULL,
  CertId       INT NOT NULL,
  AudioPath    NVARCHAR(400) NOT NULL,
  DurationSec  INT NOT NULL CONSTRAINT CK_ExamSectionAudio_Duration CHECK (DurationSec > 0),
  SizeBytes    BIGINT NOT NULL CONSTRAINT CK_ExamSectionAudio_Size CHECK (SizeBytes > 0 AND SizeBytes <= 62914560), -- 60 MB
  UploadedAt   DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT PK_ExamSectionAudio PRIMARY KEY (ExamId, SectionId),
  -- exam and section must share one certificate
  CONSTRAINT FK_ExamSectionAudio_ExamCert    FOREIGN KEY (ExamId, CertId)    REFERENCES Exams(ExamId, CertId),
  CONSTRAINT FK_ExamSectionAudio_SectionCert FOREIGN KEY (SectionId, CertId) REFERENCES Sections(SectionId, CertId)
);
GO
-- #183: the topic axis of the warehouse. A topic is a communicative micro-skill within a skill -
-- six listening, six reading, four speaking, four writing - and it belongs to the language, not to
-- a certificate. Domain flavour (workplace, academic, general) is carried per stimulus instead, so
-- one topic serves every certificate that examines its skill.
--
-- No CertId: QuestionCategories below is per-certificate and unlevelled, which is why twelve of its
-- fifteen buckets held no items and "your weakest topic" was a claim about an empty set.
--
-- The vocabulary is a closed set. A twenty-first topic is a defect rather than a feature: coverage
-- scales by depth (how much content a cell holds), never by breadth, and every mastery figure
-- divides by the number of topics, so growing the set silently changes the denominator.
CREATE TABLE Topics (
  TopicId  INT IDENTITY(1,1) PRIMARY KEY,
  Modality VARCHAR(10) NOT NULL
    CONSTRAINT CK_Topics_Modality CHECK (Modality IN ('listening','reading','speaking','writing')),
  Code     VARCHAR(20) NOT NULL,
  Name     NVARCHAR(160) NOT NULL,
  -- one code per modality: 'L-GIST' and 'R-GIST' are different topics, and neither may repeat
  CONSTRAINT UQ_Topics_Modality_Code UNIQUE (Modality, Code)
);
GO
-- exactly twenty, six listening / six reading / four speaking / four writing, and each one named:
-- 'R-DET' is a topic, 'R-ANYTHING' is not. Counting alone was not enough - six invented listening
-- codes passed every count check and would have made "every (modality, code) pair matches the
-- canonical list" false while the guard looked satisfied.
CREATE TRIGGER trg_Topics_ClosedVocabulary
ON Topics AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF (SELECT COUNT(*) FROM Topics) > 20
    THROW 50030, 'The topic vocabulary is a closed set of twenty; a twenty-first topic is refused.', 1;
  IF EXISTS (
    SELECT 1 FROM Topics t
    WHERE NOT EXISTS (
      SELECT 1 FROM (VALUES
        ('listening','L-GIST'),('listening','L-DET'),('listening','L-INF'),
        ('listening','L-ATT'),('listening','L-FUNC'),('listening','L-ORG'),
        ('reading','R-GIST'),('reading','R-DET'),('reading','R-INF'),
        ('reading','R-ATT'),('reading','R-COH'),('reading','R-GRAM'),
        ('speaking','S-DESC'),('speaking','S-INT'),('speaking','S-ARG'),('speaking','S-SYN'),
        ('writing','W-CORR'),('writing','W-DATA'),('writing','W-ARG'),('writing','W-DISC')
      ) AS canonical(Modality, Code)
      WHERE canonical.Modality = t.Modality AND canonical.Code = t.Code
    )
  )
    THROW 50032, 'A topic must be one of the canonical twenty; an invented code is refused.', 1;
  IF EXISTS (
    SELECT Modality, COUNT(*) AS n FROM Topics GROUP BY Modality HAVING COUNT(*) > 6
  )
    THROW 50031, 'A modality may hold at most six topics.', 1;
END;
GO
-- ============ #184: The pool - stimulus, item, item version ============
-- The warehouse stops being an exam's property. These three tables carry no ExamId, no SectionId and
-- no CertId, and a schema test inserts all three with zero certificates, skills, sections and exams in
-- the database - because PRN-05 ("content is certificate-agnostic") is a foreign-key fact, not a
-- service-layer one. QuestionGroups/Questions/QuestionOptions below are NOT deleted by #231: they still
-- carry the paper chrome the pool does not model (question format, word limits, the group's audio
-- slice), and 4 NOT NULL foreign keys still point at Questions. They go when the papers they hold are
-- re-assembled from bound versions.
--
-- The skill axis is `Modality`. CONTEXT.md defines a Skill as exactly one modality
-- (listening/reading/speaking/writing), and the certificate-scoped Skills table cannot be referenced
-- from a table that must exist with no certificate. Items therefore carry the modality directly and
-- pin it to the topic's own modality by composite foreign key, the same idiom Sections already uses
-- (FK_Sections_SkillModality).
CREATE TABLE Stimuli (
  StimulusId   INT IDENTITY(1,1) PRIMARY KEY,
  -- The stimulus's own level (L_s). Authored, so the reportable floor 'pre_a1' is refused here; a
  -- result may report the floor, content may not be written at it (BusinessObjects.CefrLevel).
  CefrLevel    VARCHAR(10) NOT NULL
    CONSTRAINT CK_Stimuli_Level CHECK (CefrLevel IN ('A1','A2','B1','B2','C1','C2')),
  Passage      NVARCHAR(MAX) NULL,       -- a reading stimulus
  AudioPath    NVARCHAR(400) NULL,       -- a listening stimulus
  -- #250: the group's slice of the section's master track. UI-6's exam-review "Replay this question"
  -- and the sitting both play a section's upload cut to this window; without it a pool paper can only
  -- replay a whole Part. Migrated from QuestionGroups.AudioStartMs/AudioEndMs, which is where the
  -- concept lived. Both NULL means "the whole track", which is why the CHECK is a pair.
  AudioStartMs INT NULL,
  AudioEndMs   INT NULL,
  Transcript   NVARCHAR(MAX) NULL,       -- the audio script, shown after the sitting
  -- PRN: workplace / academic / general. Orthogonal to the construct: it varies register, never what
  -- the item measures (LVL-09), so it lives on the stimulus rather than on the item.
  DomainContext VARCHAR(20) NOT NULL CONSTRAINT DF_Stimuli_Domain DEFAULT 'general'
    CONSTRAINT CK_Stimuli_Domain CHECK (DomainContext IN ('workplace','academic','general')),
  -- A productive task's instruction copy. Speaking/writing stimuli have no passage and no audio.
  PromptCopy   NVARCHAR(MAX) NULL,
  ImagePath    NVARCHAR(400) NULL,
  -- UI-5: alt text is required wherever an image is, in this table as in QuestionGroups
  ImageAlt     NVARCHAR(400) NULL,
  PrepSeconds  INT NULL CONSTRAINT CK_Stimuli_Prep CHECK (PrepSeconds IS NULL OR PrepSeconds >= 0),
  ResponseSeconds INT NULL CONSTRAINT CK_Stimuli_Response CHECK (ResponseSeconds IS NULL OR ResponseSeconds > 0),
  DisplayOrder INT NOT NULL DEFAULT 0,
  CreatedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT CK_Stimuli_ImageAlt CHECK (ImagePath IS NULL OR ImageAlt IS NOT NULL),
  -- #250: a slice with one end missing, or an inverted one, is a window nobody can play. Both NULL is
  -- "the whole track", which is not the same fact as a zero-length slice.
  --
  -- Written with explicit IS NULL tests rather than ISNULL on a comparison: a CHECK whose expression
  -- evaluates to UNKNOWN is satisfied, so `(AudioStartMs >= 0 AND AudioEndMs > AudioStartMs)` with a
  -- NULL end is not false - it is unknown, and the row is accepted. That exact hole was found by
  -- domain45 before this comment existed.
  CONSTRAINT CK_Stimuli_AudioSlice CHECK (
       (AudioStartMs IS NULL AND AudioEndMs IS NULL)
    OR (AudioStartMs IS NOT NULL AND AudioEndMs IS NOT NULL
        AND AudioStartMs >= 0 AND AudioEndMs > AudioStartMs)
  )
);
GO
-- A topic is addressed by (TopicId, Modality): TopicId is already the primary key, so this constraint
-- adds no uniqueness, it exists so Items' composite foreign key has a target. It is declared before
-- Items for that reason.
ALTER TABLE Topics ADD CONSTRAINT UQ_Topics_Id_Modality UNIQUE (TopicId, Modality);
GO
CREATE TABLE Items (
  ItemId           INT IDENTITY(1,1) PRIMARY KEY,
  -- NULL is "authored before its stimulus is attached". Every one of the fourteen types in D0.4 has a
  -- stimulus in practice; requiring it here would only couple the order of two inserts.
  StimulusId       INT NULL REFERENCES Stimuli(StimulusId),
  -- The skill axis. CONTEXT.md: a Skill is one modality.
  Modality         VARCHAR(10) NOT NULL
    CONSTRAINT CK_Items_Modality CHECK (Modality IN ('listening','reading','speaking','writing')),
  -- #183: the canonical twenty. No twenty-first topic exists, so an item cannot invent one.
  TopicId          INT NOT NULL REFERENCES Topics(TopicId),
  CefrLevel        VARCHAR(10) NOT NULL
    CONSTRAINT CK_Items_Level CHECK (CefrLevel IN ('A1','A2','B1','B2','C1','C2')),
  -- D0.4: fourteen authored types across four skills. 'visual_data_synthesis' deliberately appears
  -- twice - spoken synthesis and written synthesis are different tasks with different rubrics - so
  -- the vocabulary is 13 codes and 14 (modality, type) rows. A CHECK on ItemType alone could not say
  -- that, and would let a reading item be authored as a persuasive essay.
  ItemType         VARCHAR(40) NOT NULL,
  -- #250: the render format - which widget the sitting draws. This is a SECOND axis, not a rename of
  -- ItemType. ItemType answers "what does this item measure" (one of D0.4's fourteen semantic types,
  -- e.g. 'passage_comprehension'); RenderFormat answers "how is it drawn" (radio, checkbox, select,
  -- drag target, blank, text area). They are not interchangeable: 'mcq_3' and 'dropdown' carry the
  -- same three options and one key but render as a radio group versus a select, while two
  -- 'passage_comprehension' items can render as 'mcq' and 'multi_select'. Collapsing the two would
  -- make "which widget" and "what skill" the same question and lose seven of the seven seed formats
  -- (only two ItemTypes appear in the seed but all seven render formats do).
  --
  -- It lives on Items, not ItemVersions, because the widget is the authored shape of the item - the
  -- same reason ItemType does - and a change of widget is re-authoring rather than the cosmetic
  -- correction copy-on-write exists for. Option COUNT and MaxSelections, which do vary by version,
  -- live on the version below.
  --
  -- The default is 'mcq' because it is the common authored shape. A productive item that names no
  -- widget is the trap this column exists to prevent, and CK_Items_Modality_RenderFormat below is
  -- what turns that omission into a loud failure instead of a writing task drawn as a radio group.
  RenderFormat     VARCHAR(30) NOT NULL
    CONSTRAINT DF_Items_RenderFormat DEFAULT 'mcq'
    CONSTRAINT CK_Items_RenderFormat CHECK (RenderFormat IN
      ('mcq','mcq_3','multi_select','inline_completion','dropdown','drag_drop','free_response')),
  PlacementEligible BIT NOT NULL CONSTRAINT DF_Items_PlacementEligible DEFAULT 0,
  -- D0.5/CNT-03. Defaults to 'demo' because a new item is not production until it passes its gates:
  -- the wrong default here lets an unreviewed item into a paid paper (INV-6).
  ContentTier      VARCHAR(12) NOT NULL CONSTRAINT DF_Items_ContentTier DEFAULT 'demo'
    CONSTRAINT CK_Items_ContentTier CHECK (ContentTier IN ('production','demo')),
  -- CNT-42: draft -> validated -> active, plus quarantined, retired, retired_defective. Selection
  -- filters on 'active'; review does not filter at all, so a past attempt renders what was sat.
  LifecycleStatus  VARCHAR(20) NOT NULL CONSTRAINT DF_Items_Lifecycle DEFAULT 'draft'
    CONSTRAINT CK_Items_Lifecycle CHECK (LifecycleStatus IN
      ('draft','validated','active','quarantined','retired','retired_defective')),
  -- CNT-03: the upload batch this item arrived in. NULL for hand-authored items.
  BatchTag         NVARCHAR(60) NULL,
  -- #309: comma-separated target band codes (e.g. 'A2,B1,B2') or 'ALL' for general knowledge.
  TargetBands      VARCHAR(100) NOT NULL CONSTRAINT DF_Items_TargetBands DEFAULT 'ALL',
  CreatedAt        DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- the item's skill must be its topic's skill: a listening item on a reading topic is the defect
  CONSTRAINT FK_Items_TopicModality FOREIGN KEY (TopicId, Modality) REFERENCES Topics(TopicId, Modality),
  -- #185: composite target so ExamItems can pin a bound item's skill to its section's own. ItemId is
  -- already the primary key, so this adds no uniqueness - it exists only as an FK target.
  CONSTRAINT UQ_Items_Id_Modality UNIQUE (ItemId, Modality),
  CONSTRAINT CK_Items_Modality_Type CHECK (
       (Modality = 'listening' AND ItemType IN ('short_exchange','extended_dialogue','monologue_talk'))
    OR (Modality = 'reading'   AND ItemType IN ('sentence_cloze','passage_comprehension','text_insertion_cohesion'))
    OR (Modality = 'speaking'  AND ItemType IN ('descriptive_monologue','situational_negotiation','argumentative_monologue','visual_data_synthesis'))
    OR (Modality = 'writing'   AND ItemType IN ('transactional_correspondence','visual_data_synthesis','persuasive_essay','analytical_evaluation_essay'))
  ),
  -- #250: a productive skill renders as a free response and nothing else - a writing task drawn as a
  -- radio group measures nothing. The pairing is checked here for the same reason Modality/ItemType is:
  -- it is a fact about the authored shape, and a CHECK is what stops the DF below turning "an author
  -- forgot the widget" into a productive item silently labelled 'mcq'. The default is 'mcq' because it
  -- is the common shape; an objective-only default plus this CHECK means an unspecified productive item
  -- fails loudly at insert instead of rendering wrong.
  CONSTRAINT CK_Items_Modality_RenderFormat CHECK (
       (Modality IN ('listening','reading')
        AND RenderFormat IN ('mcq','mcq_3','multi_select','inline_completion','dropdown','drag_drop'))
    OR (Modality IN ('speaking','writing') AND RenderFormat = 'free_response')
  )
);
GO
-- CNT-42: the six states, and the transitions the specification names. Nothing else is reachable.
--
--   draft     -> validated                        the automated gates promoted it
--   draft     -> retired                          it can no longer be deleted (a paper or a learner
--                                                  reached it) so retirement is its only disposal
--   validated -> active                           the 10% audit cleared the stratum
--   validated -> quarantined                      the zero-tolerance key rule quarantined the
--                                                  stratum awaiting audit (W-01), which is validated
--   validated -> retired                          not promoted, and no longer deletable
--   active    -> quarantined                      telemetry (CNT-44) or a reviewer's key error
--   active    -> retired                          ordinary pool rotation
--   quarantined -> active                         re-verification cleared it
--   quarantined -> retired                        withdrawn rather than fixed
--   quarantined -> retired_defective              confirmed defective (CNT-45)
--
-- retired and retired_defective are terminal. Three of these edges are load-bearing and each was
-- nearly missed:
--   * validated -> quarantined, because the stratum W-01 quarantines is the validated one, not the
--     live one.
--   * draft -> retired, because CNT-43 makes a draft item with a reference undeletable, and an item
--     with no way out is a row nobody can dispose of.
--   * quarantined -> active, because "before any item in it can become active" is the sentence that
--     says re-verification returns an item to service.
--
-- The pair table is written out rather than derived from a rank. A rank accepts draft -> quarantined
-- and validated -> retired_defective, which nothing names.
--
-- A write that leaves the state where it was is not a transition, so CNT-46's bulk demo
-- decommission (one UPDATE over many rows, re-runnable) is accepted. Every row of a multi-row
-- update is judged, not the first: a trigger reading one row would part-apply a mixed batch.
-- #309: trg_Items_LifecycleTransitions removed (Expand phase).
-- Everything that can be scored. Copy-on-write (CNT-43): a new row per change, so an attempt that
-- pinned version 3 renders version 3 forever. Only a cosmetic explanation fix may be edited in place,
-- and trg_ItemVersions_ScoredFieldsImmutable is what refuses the rest - a convention in a service
-- would not survive the second caller.
CREATE TABLE ItemVersions (
  ItemVersionId  INT IDENTITY(1,1) PRIMARY KEY,
  ItemId         INT NOT NULL CONSTRAINT FK_ItemVersions_Item REFERENCES Items(ItemId),
  -- #254: the ordering is NOT constrained to rise. "Newest" is the highest VersionNumber, and
  -- GateContext.Newest is its only reader, so a lower number written after a higher one cannot make
  -- two readers disagree. A trigger refusing it was considered and skipped: correcting data - the
  -- issue's own rollback-and-reapply - needs to write a lower number, and a guard that blocked the
  -- repair would be worse than the latent divergence it prevents.
  VersionNumber  INT NOT NULL CONSTRAINT CK_ItemVersions_Number CHECK (VersionNumber > 0),
  Stem           NVARCHAR(MAX) NOT NULL,
  -- CNT-06: four options, one key, a 25% guessing floor. A productive task is the other legal shape
  -- (all four NULL, no key), and CK_ItemVersions_OptionsComplete refuses the half-filled middle.
  OptionA        NVARCHAR(MAX) NULL,
  OptionB        NVARCHAR(MAX) NULL,
  OptionC        NVARCHAR(MAX) NULL,
  OptionD        NVARCHAR(MAX) NULL,
  -- #250: the key is a SET of labels, not one label. 'A' is the four-option shape; 'A,C' is
  -- multi_select's "these two are correct". A separate column was considered and rejected: every
  -- reader that asks "is this option correct" becomes a set membership test either way, and two
  -- columns would need a rule for which one wins when both are set.
  --
  -- The canonical form is comma-separated, ascending, no spaces ('A', 'A,C', 'A,B,D'). Encoded in
  -- the CHECK rather than a trigger so a malformed key is refused by the table that holds it:
  --   only A-D and commas, no leading/trailing/double comma, no repeated letter, odd length.
  -- Scored, so trg_ItemVersions_ScoredFieldsImmutable compares it (CNT-43).
  KeyLabel       VARCHAR(7) NULL
    CONSTRAINT CK_ItemVersions_KeyLabel CHECK (
      KeyLabel IS NULL OR (
            KeyLabel NOT LIKE '%[^A-D,]%'
        AND KeyLabel NOT LIKE ',%'
        AND KeyLabel NOT LIKE '%,'
        AND KeyLabel NOT LIKE '%,,%'
        AND LEN(KeyLabel) % 2 = 1
        AND LEN(KeyLabel) - LEN(REPLACE(KeyLabel,'A','')) <= 1
        AND LEN(KeyLabel) - LEN(REPLACE(KeyLabel,'B','')) <= 1
        AND LEN(KeyLabel) - LEN(REPLACE(KeyLabel,'C','')) <= 1
        AND LEN(KeyLabel) - LEN(REPLACE(KeyLabel,'D','')) <= 1
      )
    ),
  -- CNT-43: the one editable field
  Explanation    NVARCHAR(MAX) NULL,
  -- #194: gate one requires a rationale per option, so an objective item carries one beside each
  -- option. They are versioned with the option they explain rather than being editable in place: the
  -- review screen shows the learner the rationale for the option they picked, so rewriting one would
  -- change what a past attempt renders - the same reason option text is immutable. A rationale with
  -- no option to explain is a row no reader can render, hence the CHECK below.
  RationaleA     NVARCHAR(MAX) NULL,
  RationaleB     NVARCHAR(MAX) NULL,
  RationaleC     NVARCHAR(MAX) NULL,
  RationaleD     NVARCHAR(MAX) NULL,
  -- CNT-11: (start, end, verbatim quote) - seconds for audio, characters for text. The stem may not
  -- depend on a timestamp during the sitting, but the review screen and the explanation do.
  EvidenceStart  INT NULL,
  EvidenceEnd    INT NULL,
  EvidenceUnit   VARCHAR(8) NULL
    CONSTRAINT CK_ItemVersions_EvidenceUnit CHECK (EvidenceUnit IN ('char','sec')),
  EvidenceQuote  NVARCHAR(MAX) NULL,
  -- #152: the text a spoken answer is judged against (a Read Aloud script, a picture description) and
  -- what that text is. Text with no kind is not a reference this system knows how to use.
  --
  -- #277: NVARCHAR(MAX), not NVARCHAR(1000). Measured: a B2 writing task asks for 180-220 words, and its
  -- model answer is 1,220 characters - so 1000 refused every realistic essay model answer at the write,
  -- with "String or binary data would be truncated" rather than a sentence the author could act on. The
  -- column holds a model answer, which is prose of the same length as the answer it models; a 1000-char
  -- ceiling silently caps it below the task it describes.
  ReferenceText  NVARCHAR(MAX) NULL,
  ReferenceKind  VARCHAR(12) NULL
    CONSTRAINT CK_ItemVersions_ReferenceKind CHECK (ReferenceKind IN ('script','image_desc')),
  -- #204: a productive task's output demand and its two timed windows.
  --
  -- They live on the version, not on the stimulus, because a writing task may be authored as two
  -- sub-tasks and each carries its OWN word range and time budget (D0.7's two-task sitting weights
  -- the extended one twice). A single pair on Stimuli could describe only one of the two.
  --
  -- They are scored fields: the sitting renders them and the under-length penalty reads MinWords, so
  -- an in-place change would rewrite what a past attempt was asked for - the trigger below refuses it.
  --
  -- D0.4 states a word range for writing and timed windows for both productive skills, and nothing
  -- for a speaking task's length: a spoken answer is bounded by its recording window, and a word
  -- count over a transcript of speech is not an authored demand.
  MinWords        INT NULL,
  MaxWords        INT NULL,
  PrepSeconds     INT NULL,   -- planning (writing) or preparation (speaking)
  ResponseSeconds INT NULL,   -- composition budget (writing) or recording window (speaking)
  -- #250: the answer-shape facts the paper used to keep on Questions. They live on the version for
  -- the same reason MinWords does, and they are scored for the same reason: the sitting renders the
  -- hint, enforces the selection count and grades the blank from these columns, so an in-place change
  -- would rewrite what a past attempt shows or was marked against. The trigger below compares them.
  --
  -- They are NOT on Items because they vary with the version: correcting a multi_select from "two
  -- answers" to "three" is a new version of the same item, not a different item.
  --
  -- MaxSelections: multi_select's "select TWO". NULL is "exactly one key", the four-option shape.
  MaxSelections  INT NULL
    CONSTRAINT CK_ItemVersions_MaxSelections CHECK (MaxSelections IS NULL OR MaxSelections > 1),
  -- AllowNumber: inline_completion's "may the blank hold a digit". Defaults to 1 to match the legacy
  -- column's default, because a blank that silently stopped accepting "15" would change the answer.
  AllowNumber    BIT NOT NULL CONSTRAINT DF_ItemVersions_AllowNumber DEFAULT 1,
  -- inline_completion's accepted answer set, a JSON array of strings ("[\"15\",\"fifteen\"]"). The key
  -- for a blank is a set rather than a label, which is why it is not KeyLabel.
  AcceptedAnswersJson NVARCHAR(MAX) NULL,
  -- free_response grading: the words a passing answer must contain. NULL is "no keyword requirement".
  RequiredKeywords    NVARCHAR(MAX) NULL,
  -- UI-42: the practice-take nudge. Explanation stays the after-answer why; the hint is shown before.
  Hint           NVARCHAR(1000) NULL,
  CreatedAt      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT UQ_ItemVersions_Item_Number UNIQUE (ItemId, VersionNumber),
  -- #185: composite target so a version's identity is (version, item). A binding carries both, so
  -- the version it pins cannot be a version of some other item than the one it names.
  CONSTRAINT UQ_ItemVersions_Id_Item UNIQUE (ItemVersionId, ItemId),
  CONSTRAINT CK_ItemVersions_OptionsComplete CHECK (
       -- No options: a productive task (writing/speaking) or a blank (inline_completion, whose key is
       -- AcceptedAnswersJson rather than a label). Both are legal no-option shapes, so this branch
       -- constrains only the option columns.
       (OptionA IS NULL AND OptionB IS NULL AND OptionC IS NULL AND OptionD IS NULL
        AND KeyLabel IS NULL)
    -- #250: two to four options, filled contiguously from A, with a key naming only filled options.
    -- The pool previously demanded exactly four or none, which could not express the paper: the seed's
    -- 17 three-option 'mcq' items and its 'mcq_3' item are real renderings, not defects. Widening is
    -- not a convenience - criterion 3 (all seven render formats representable and rendering
    -- identically) is unreachable without it.
    --
    -- The half-filled guard is kept, and made explicit rather than left to "the count is 3 or 4": D
    -- cannot be set without C, and every label in KeyLabel must name an option that actually exists. A
    -- row with options A,B and key 'D' would otherwise parse as four-with-a-gap and render a leader to
    -- a letter the learner can never pick. A and B always exist in this branch, so only C and D are
    -- checked - and they are checked with LIKE because the key is a set ('A,C'), not one letter.
    OR (OptionA IS NOT NULL AND OptionB IS NOT NULL AND KeyLabel IS NOT NULL
        AND (OptionC IS NOT NULL OR OptionD IS NULL)
        AND (OptionC IS NOT NULL OR KeyLabel NOT LIKE '%C%')
        AND (OptionD IS NOT NULL OR KeyLabel NOT LIKE '%D%')
        -- a keyed item is not also a blank: AcceptedAnswersJson is inline_completion's answer set
        AND AcceptedAnswersJson IS NULL)
  ),
  -- #194: a rationale explains an option, so it may exist only where its option does. A distractor
  -- rationale on a productive task has no option to render beside, and would reach review as copy
  -- above nothing.
  CONSTRAINT CK_ItemVersions_RationalesNeedOptions CHECK (
       (RationaleA IS NULL OR OptionA IS NOT NULL)
   AND (RationaleB IS NULL OR OptionB IS NOT NULL)
   AND (RationaleC IS NULL OR OptionC IS NOT NULL)
   AND (RationaleD IS NULL OR OptionD IS NOT NULL)
  ),
  CONSTRAINT CK_ItemVersions_ReferencePair CHECK (
       (ReferenceText IS NULL AND ReferenceKind IS NULL)
    OR (ReferenceText IS NOT NULL AND ReferenceKind IS NOT NULL)
  ),
  CONSTRAINT CK_ItemVersions_EvidenceSpan CHECK (
       (EvidenceStart IS NULL AND EvidenceEnd IS NULL AND EvidenceUnit IS NULL)
    OR (EvidenceStart >= 0 AND EvidenceEnd > EvidenceStart AND EvidenceUnit IS NOT NULL)
  ),
  -- #204: a declared window is a real window. Zero is not "no preparation" - that is NULL - and a
  -- negative or inverted one is a sitting the learner cannot finish.
  --
  -- #250: a FLOOR WITH NO CEILING is a real exam demand, so it is legal. IELTS Writing Task 1 says
  -- "at least 150 words" and Task 2 "at least 250", and neither states a maximum - a candidate who
  -- writes 400 words has met the task. Requiring both ends made those tasks unrepresentable, which is
  -- the content this rebuild exists to serve. The previous rule's own comment said "a word range with
  -- no ceiling is a range nobody can satisfy"; that is true of a RANGE and false of a MINIMUM, and
  -- every reader already treats the ceiling as optional:
  --   AttemptEngine.Evaluation   `if (question.MaxWords.HasValue && words.Length > ...)`
  --   AiGrader                   `if (request.MaxWords.HasValue && request.MaxWords.Value > 0 && ...)`
  --   ClaudeAiGrader             `request.MaxWords is null or <= 0 || words <= request.MaxWords`
  -- so the reader was already tolerant and only the constraint was strict.
  --
  -- A ceiling with no floor stays refused: "at most 300 words" alone is not a demand this product can
  -- score, because the under-length penalty reads MinWords and would have nothing to read.
  CONSTRAINT CK_ItemVersions_WordRange CHECK (
       (MinWords IS NULL AND MaxWords IS NULL)
    OR (MinWords IS NOT NULL AND MinWords > 0 AND (MaxWords IS NULL OR MaxWords >= MinWords))
  ),
  CONSTRAINT CK_ItemVersions_Timing CHECK (
       (PrepSeconds IS NULL OR PrepSeconds > 0)
   AND (ResponseSeconds IS NULL OR ResponseSeconds > 0)
  )
);
GO
-- CNT-43: copy-on-write. INSERT is untouched - a new version row is the approved way to change
-- anything scored. An UPDATE that moves a scored column is refused by name, so the caller is told
-- which field brought it here. Explanation is not compared: a typo in it is the one in-place edit.
CREATE TRIGGER trg_ItemVersions_ScoredFieldsImmutable
ON ItemVersions AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN deleted d ON d.ItemVersionId = i.ItemVersionId
    WHERE i.ItemId        <> d.ItemId
       OR i.VersionNumber <> d.VersionNumber
       OR i.Stem          <> d.Stem
       OR ISNULL(i.OptionA,   N'') <> ISNULL(d.OptionA,   N'')
       OR ISNULL(i.OptionB,   N'') <> ISNULL(d.OptionB,   N'')
       OR ISNULL(i.OptionC,   N'') <> ISNULL(d.OptionC,   N'')
       OR ISNULL(i.OptionD,   N'') <> ISNULL(d.OptionD,   N'')
       OR ISNULL(i.RationaleA, N'') <> ISNULL(d.RationaleA, N'')
       OR ISNULL(i.RationaleB, N'') <> ISNULL(d.RationaleB, N'')
       OR ISNULL(i.RationaleC, N'') <> ISNULL(d.RationaleC, N'')
       OR ISNULL(i.RationaleD, N'') <> ISNULL(d.RationaleD, N'')
       OR ISNULL(i.KeyLabel,  '') <> ISNULL(d.KeyLabel,  '')
       OR ISNULL(i.EvidenceStart, -1) <> ISNULL(d.EvidenceStart, -1)
       OR ISNULL(i.EvidenceEnd,   -1) <> ISNULL(d.EvidenceEnd,   -1)
       OR ISNULL(i.EvidenceUnit, '')  <> ISNULL(d.EvidenceUnit, '')
       OR ISNULL(i.EvidenceQuote, N'') <> ISNULL(d.EvidenceQuote, N'')
       OR ISNULL(i.ReferenceText, N'') <> ISNULL(d.ReferenceText, N'')
       OR ISNULL(i.ReferenceKind, '')  <> ISNULL(d.ReferenceKind, '')
       -- #204: the productive task's word range and timed windows are what the sitting renders and
       -- what the under-length penalty reads, so an in-place move rewrites a past attempt.
       OR ISNULL(i.MinWords,        -1) <> ISNULL(d.MinWords,        -1)
       OR ISNULL(i.MaxWords,        -1) <> ISNULL(d.MaxWords,        -1)
       OR ISNULL(i.PrepSeconds,     -1) <> ISNULL(d.PrepSeconds,     -1)
       OR ISNULL(i.ResponseSeconds, -1) <> ISNULL(d.ResponseSeconds, -1)
       -- #250: the answer-shape facts moved off Questions. The sitting renders the hint, enforces
       -- the selection count and grades the blank from them, so an in-place move rewrites a past
       -- attempt exactly as a word-range edit does.
       OR ISNULL(i.MaxSelections,   -1) <> ISNULL(d.MaxSelections,   -1)
       OR i.AllowNumber <> d.AllowNumber
       OR ISNULL(i.AcceptedAnswersJson, N'') <> ISNULL(d.AcceptedAnswersJson, N'')
       OR ISNULL(i.RequiredKeywords,    N'') <> ISNULL(d.RequiredKeywords,    N'')
       OR ISNULL(i.Hint,                N'') <> ISNULL(d.Hint,                N'')
  )
    THROW 50048, 'An ItemVersions scored field is immutable; a scored change requires a new version row (copy-on-write, CNT-43).', 1;
END;
GO
-- ============ #185: the assembly binding ============
-- Assembly is a query over the pool, not an ownership relation. One row per item an exam puts on a
-- paper: which version, in which section, at which position, in which unit. This is the ONLY
-- relationship between a paper and the warehouse - and it is the first place INV-6 ("a production
-- exam binds no demo item") can actually be enforced, because it is the first place an exam and an
-- item meet.
--
-- CertId and Modality are carried here for the reason ExamSectionAudio carries CertId: both
-- composite foreign keys on each read the same column, so an exam's certificate and its section's
-- must agree, and an item's skill and its section's must agree, on every row. Isolation therefore
-- costs no trigger and cannot drift.
--
-- The binding points at ItemVersionId, not at the stem. CNT-43 copy-on-write means a re-versioned
-- item leaves this row - and therefore a published paper's rendering - untouched.
CREATE TABLE ExamItems (
  ExamItemId    INT IDENTITY(1,1) PRIMARY KEY,
  ExamId        INT NOT NULL,
  SectionId     INT NOT NULL,
  CertId        INT NOT NULL,
  ItemVersionId INT NOT NULL,
  -- Carried down so the pinned version can be tied to this item by a composite key: a binding
  -- cannot name an item's version and a different item in one row.
  ItemId        INT NOT NULL,
  -- Carried down from the section, exactly as CertId is, so the item's skill is pinned to the
  -- section's by a foreign key rather than by a trigger. A listening section therefore cannot bind
  -- a reading item's version: the pair has no target row.
  Modality      VARCHAR(10) NOT NULL
    CONSTRAINT CK_ExamItems_Modality CHECK (Modality IN ('listening','reading','speaking','writing')),
  -- The paper's position, 1-based within its section. It belongs to the binding, not the item: the
  -- same item on two papers, or twice on one paper, gets two positions.
  DisplayOrder  INT NOT NULL CONSTRAINT CK_ExamItems_DisplayOrder CHECK (DisplayOrder > 0),
  -- D9: a multistage sitting addresses a unit. Same meaning as QuestionGroups.UnitNumber.
  UnitNumber    INT NOT NULL CONSTRAINT DF_ExamItems_UnitNumber DEFAULT 1
    CONSTRAINT CK_ExamItems_UnitNumber CHECK (UnitNumber > 0),
  CreatedAt     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- the exam and the section must share one certificate (the ExamSectionAudio idiom)
  CONSTRAINT FK_ExamItems_ExamCert     FOREIGN KEY (ExamId, CertId)    REFERENCES Exams(ExamId, CertId),
  CONSTRAINT FK_ExamItems_SectionCert  FOREIGN KEY (SectionId, CertId) REFERENCES Sections(SectionId, CertId),
  -- the bound item must be of the section's own skill (both FKs read the one Modality column)
  CONSTRAINT FK_ExamItems_SectionSkill FOREIGN KEY (SectionId, Modality) REFERENCES Sections(SectionId, Modality),
  CONSTRAINT FK_ExamItems_ItemSkill    FOREIGN KEY (ItemId, Modality)    REFERENCES Items(ItemId, Modality),
  -- the pinned version must belong to the item the binding names
  CONSTRAINT FK_ExamItems_VersionItem  FOREIGN KEY (ItemVersionId, ItemId)
    REFERENCES ItemVersions(ItemVersionId, ItemId),
  -- one position per paper per section: two items cannot both be "question 7" of one section
  CONSTRAINT UQ_ExamItems_Exam_Section_Order UNIQUE (ExamId, SectionId, DisplayOrder),
  -- #269: composite target so a free response can require its position to belong to its own exam.
  CONSTRAINT UQ_ExamItems_Id_Exam UNIQUE (ExamItemId, ExamId),
  -- #269: composite target so a free response's SkillId must be its position's own skill. Without this
  -- a response on a READING position could claim the SPEAKING skill and AiGradings accepted it, so the
  -- rubric the answer was graded against belonged to a different skill.
  CONSTRAINT UQ_ExamItems_Id_Section UNIQUE (ExamItemId, SectionId)
);
GO
-- INV-6 is the one rule no foreign key can express: "this paper may draw this tier of stock" relates
-- two rows on opposite sides, so no single column can pin it - which is why the tier vocabulary #184
-- closed had no enforcement anywhere until this table existed. The binding is the first place an
-- exam and an item meet, so it is the first place the rule can be checked.
--
-- A demo exam may bind either tier: demo stock is what a demo exam is for, and promoting stock must
-- not strand a working paper. Only production drawing demo stock is refused.
CREATE TRIGGER trg_ExamItems_ProductionBindsProduction
ON ExamItems AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted b
    JOIN Exams e ON e.ExamId = b.ExamId
    JOIN Items i ON i.ItemId = b.ItemId
    WHERE e.ContentTier = 'production' AND i.ContentTier = 'demo'
  )
    THROW 50049, 'INV-6: a production exam cannot bind a demo-tier item.', 1;
END;
GO
-- The same rule from the other side, for the reason trg_Exams_PurposeKeepsContent exists: a rule that
-- only fires on the child table is bypassed by editing the parent. Promoting an exam to production
-- while its paper still binds demo stock is exactly the edit that would slip INV-6 through, so it is
-- refused at the moment of promotion rather than left to a later read.
CREATE TRIGGER trg_Exams_TierKeepsContent
ON Exams AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted e
    JOIN ExamItems b ON b.ExamId = e.ExamId
    JOIN Items i ON i.ItemId = b.ItemId
    WHERE e.ContentTier = 'production' AND i.ContentTier = 'demo'
  )
    THROW 50049, 'INV-6: a production exam cannot bind a demo-tier item.', 1;
END;
GO
-- UI-8: question-type labels ("Verb tense", "Part 7 â€” inference") per certificate.
-- #183: retained while items/lessons still read it; the topic axis above replaces it once every
-- reader has moved. It is not deleted here, because deleting it now would break the readers the
-- next ticket rewrites.
CREATE TABLE QuestionCategories (
  CategoryId INT IDENTITY(1,1) PRIMARY KEY,
  CertId     INT NOT NULL REFERENCES Certificates(CertId),
  Code       VARCHAR(40) NOT NULL,
  Name       NVARCHAR(120) NOT NULL,
  CONSTRAINT UQ_QuestionCategories_Cert_Code UNIQUE (CertId, Code),
  -- #155: composite target so UserCategoryMasteries can pin a learner's mastery of a category to
  -- that category's own certificate (no mastery row can straddle two certificates).
  CONSTRAINT UQ_QuestionCategories_Id_Cert UNIQUE (CategoryId, CertId)
);
GO
-- #269: the legacy paper content model is GONE - QuestionGroups, Questions and
-- QuestionOptions. A paper is now a roster of positions (`ExamItems`) over pool items, and a
-- position's shared content (passage, audio slice, image, transcript) lives on `Stimuli`.
--
-- Removed with them: the three tables' composite targets that let an answer carry ExamId,
-- SectionId, SkillId and QuestionType down from its question, the `Questions.SectionId` that
-- attributed an answer to a section, and the three per-table indexes. Nothing in this script
-- references a removed object; `LegacyContentModelIsGoneTests` asserts the absence, and
-- `NoLegacyTableReferencesTests` asserts no source file names one.
--
-- What replaced each thing they held:
--   Stem, CefrLevel, type, word limits   -> ItemVersions (per version, never re-read from an item)
--   the four options with rationale      -> ItemVersions.OptionA..D / RationaleA..D
--   AcceptedAnswersJson, RequiredKeywords-> ItemVersions (the graded content)
--   Passage, Transcript, Image/Audio     -> Stimuli, reached through Items.StimulusId
--   the group's unit and time budget     -> ExamItems + ItemVersions.PrepSeconds/ResponseSeconds
CREATE TABLE ImportBatches (
  BatchId     INT IDENTITY(1,1) PRIMARY KEY,
  CertId      INT NOT NULL REFERENCES Certificates(CertId),
  SourceName  NVARCHAR(260) NOT NULL,
  Status      VARCHAR(10) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_ImportBatches_Status CHECK (Status IN ('pending','done','failed')),
  UploadedBy  INT NOT NULL REFERENCES Users(UserId),
  CreatedAt   DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CompletedAt DATETIMEOFFSET NULL
);
GO
CREATE TABLE ImportLog (
  LogId   INT IDENTITY(1,1) PRIMARY KEY,
  BatchId INT NOT NULL REFERENCES ImportBatches(BatchId),
  RowNo   INT NULL,
  Level   VARCHAR(5) NOT NULL CONSTRAINT CK_ImportLog_Level CHECK (Level IN ('info','warn','error')),
  Message NVARCHAR(1000) NOT NULL,
  LoggedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
-- UI-36 / UI-38: a speaking_practice exam holds speaking sections only, a writing_practice exam
-- writing sections only (a one-skill result never mixes skills).
--
-- #269: this was a trigger on QuestionGroups, which is gone. The rule is about the paper's POSITIONS,
-- so it moves to the roster - the same table the sitting actually renders.
CREATE TRIGGER trg_ExamItems_SkillPracticeOneModality
ON ExamItems AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN Exams e    ON e.ExamId = i.ExamId
    JOIN Sections s ON s.SectionId = i.SectionId
    WHERE (e.Purpose = 'speaking_practice' AND s.Modality <> 'speaking')
       OR (e.Purpose = 'writing_practice'  AND s.Modality <> 'writing')
  )
    THROW 50037, 'A speaking- or writing-practice exam can only hold sections of that skill.', 1;
END;
GO
CREATE TRIGGER trg_Exams_PurposeKeepsContent
ON Exams AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN ExamItems ei ON ei.ExamId = i.ExamId
    JOIN Sections s ON s.SectionId = ei.SectionId
    WHERE (i.Purpose = 'speaking_practice' AND s.Modality <> 'speaking')
       OR (i.Purpose = 'writing_practice'  AND s.Modality <> 'writing')
  )
    THROW 50037, 'A speaking- or writing-practice exam can only hold sections of that skill.', 1;
END;
GO
-- ============ Domain 6: Practice (free) ============
CREATE TABLE PracticeSessions (
  SessionId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId       INT NOT NULL REFERENCES Users(UserId),
  -- #187: the certificate is the session's own fact, not something read back through a section.
  -- A pool drill has no section to derive it from, and a session that cannot name its certificate
  -- cannot be pruned, counted or resumed. Pinned to the section's own when there is a section, so
  -- the two can never disagree.
  CertId       INT NOT NULL,
  -- #187: NULL for a pool drill. An item is certificate-agnostic and section-free; the drill is a
  -- cell (topic, level), so a session drawn from the pool has no section to point at. Every section
  -- drill still names one, and CK_PracticeSessions_Cell keeps the two shapes from mixing.
  SectionId    INT NULL REFERENCES Sections(SectionId),
  -- #187: the cell a pool session drilled. NULL for a section session, which takes its cell from
  -- the section. The pair is what a daily ceiling and a resume both need.
  TopicId      INT NULL,
  CefrLevel    VARCHAR(10) NULL
    CONSTRAINT CK_Practice_Level CHECK (CefrLevel IN ('A1','A2','B1','B2','C1','C2')),
  -- M-18: carried down from the section so a session is pinned to its section's own skill.
  -- #150: the CHECK admits all four so the column cannot be the thing that blocks a certificate
  -- whose only skill is Speaking or Writing. The free drill builds sessions for listening, reading
  -- and writing; speaking has no typed drill and links to its recorded, AI-graded practice instead,
  -- which is a service rule, not a schema one - the FK below is what pins the pair.
  Modality     VARCHAR(10) NOT NULL
    CONSTRAINT CK_Practice_Modality CHECK (Modality IN ('listening','reading','speaking','writing')),
  QuestionCount INT NOT NULL CONSTRAINT CK_Practice_Count CHECK (QuestionCount > 0),
  Status       VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Practice_Status CHECK (Status IN ('in_progress','finished','abandoned')),
  CorrectCount INT NOT NULL DEFAULT 0,
  -- UI-11: practice-select option "Question source"
  -- #155: no longer a learner choice - a standalone session is drawn by mastery state and written as
  -- 'all'. The old codes stay because existing rows hold them; #162 removes them, not this ticket.
  QuestionSource VARCHAR(12) NOT NULL CONSTRAINT DF_Practice_Source DEFAULT 'all'
    CONSTRAINT CK_Practice_Source CHECK (QuestionSource IN ('all','wrong_before','unseen')),
  -- UI-12: a learning-path module runs as practice sessions; module progress/accuracy/time derive from them.
  -- FK added after LearnerPathSteps exists.
  LearnerPathStepId INT NULL,
  StartedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  FinishedAt   DATETIMEOFFSET NULL,
  -- M-18: Modality must be the section's own (pins the literal via the FK). This is the guard #150
  -- kept: widening the CHECK must not create a way to make a listening session on a writing section.
  -- #187: MATCH SIMPLE means both foreign keys below are skipped when SectionId is NULL, which is
  -- exactly a pool session - it is pinned to a topic's modality instead.
  CONSTRAINT FK_Practice_SectionModality FOREIGN KEY (SectionId, Modality) REFERENCES Sections(SectionId, Modality),
  CONSTRAINT FK_Practice_Cert FOREIGN KEY (CertId) REFERENCES Certificates(CertId),
  -- #187: a section session's certificate must be that section's own, the same containment
  -- Sections already uses. MATCH SIMPLE skips it for a pool session.
  CONSTRAINT FK_Practice_SectionCert FOREIGN KEY (SectionId, CertId) REFERENCES Sections(SectionId, CertId),
  -- #187: a pool session's topic must be its own modality's, the same idiom Items uses.
  CONSTRAINT FK_Practice_TopicModality FOREIGN KEY (TopicId, Modality) REFERENCES Topics(TopicId, Modality),
  -- #187: a session is one shape or the other. A section session has no cell; a pool session has a
  -- cell and no section. Without this, an id, a section and a cell could all disagree in one row and
  -- "which store does this session read?" would have no single answer.
  CONSTRAINT CK_PracticeSessions_Shape CHECK (
       (SectionId IS NOT NULL AND TopicId IS NULL AND CefrLevel IS NULL)
    OR (SectionId IS NULL AND TopicId IS NOT NULL AND CefrLevel IS NOT NULL)
  ),
  -- Low: composite target so PracticeAnswers can require QuestionId's SectionId = the session's own
  CONSTRAINT UQ_PracticeSessions_Id_Section UNIQUE (SessionId, SectionId),
  -- UI-43: composite target so a recommendation can name the learner's own session
  CONSTRAINT UQ_PracticeSessions_Id_User UNIQUE (SessionId, UserId)
);
GO
-- #269 audit: a practice session's state machine was unguarded on the server side. Nothing stopped
-- 'finished' from becoming 'in_progress' again, so a finished drill became sittable and its already
-- counted answers could be answered a second time (SubmitAnswerAsync refuses a re-answer by asking
-- whether the row already holds content, and that guard only reads the ANSWER - it trusted the
-- session to still be open). The legal moves are the sitting's:
--   in_progress -> finished   (the last question was answered, or the lesson micro-check closed)
--   in_progress -> abandoned  (a later session superseded it, or the path moved on)
-- and both are terminal. There is no writer that reopens one, so this refuses none of them.
-- #309: trg_PracticeSessions_StatusTransitions removed (Expand phase).
CREATE TABLE PracticeAnswers (
  AnswerId         INT IDENTITY(1,1) PRIMARY KEY,
  SessionId        INT NOT NULL REFERENCES PracticeSessions(SessionId),
  -- #269: a practice answer names its item and version, OR its lesson micro-check card. Both are
  -- nullable because CK_PracticeAnswers_Shape below refuses a row that names neither or both - the
  -- micro-check arm requires both item columns NULL, so they cannot be NOT NULL.
  ItemId           INT NULL,
  ItemVersionId    INT NULL,
  -- #186's idiom at practice: an ItemVersion stores labels A-D rather than option rows, so a pool
  -- answer records the label it chose and leaves SelectedOptionId NULL.
  SelectedLabel    VARCHAR(1) NULL
    CONSTRAINT CK_PracticeAnswers_SelectedLabel CHECK (SelectedLabel IN ('A','B','C','D')),
  SelectedOptionId      INT NULL,
  ResponseText          NVARCHAR(MAX) NULL,
  SelectedOptionIdsJson NVARCHAR(MAX) NULL,
  IsCorrect             BIT NULL,
  HintUsed         BIT NOT NULL CONSTRAINT DF_PracticeAnswers_HintUsed DEFAULT 0,  -- UI-42: summary "2 with a hint"
  DisplayOrder     INT NOT NULL DEFAULT 0,
  -- #156: when this answer was last written, so the gap to the previous answer is the response
  -- time and a rushed answer can be excluded from the calibration sample. NULL, never backfilled:
  -- a row from before this column has no response time, and inventing one would make the filter
  -- meaningless.
  AnsweredAt       DATETIME2(3) NULL,
  -- #187: the answered version must be a version of the answered item - the same composite idiom
  -- ExamItems and AttemptAnswers use.
  CONSTRAINT FK_PracticeAnswers_VersionItem FOREIGN KEY (ItemVersionId, ItemId) REFERENCES ItemVersions(ItemVersionId, ItemId)
);
GO
-- #187: one answer per item version per session - "one question, one grade". #269: filtered, because a
-- lesson micro-check answer names a card and has no version; without the filter SQL Server treats every
-- NULL as equal and a session could hold exactly one micro-check answer.
CREATE UNIQUE INDEX UQ_PracticeAnswers_Version ON PracticeAnswers(SessionId, ItemVersionId)
  WHERE ItemVersionId IS NOT NULL;
GO
-- UI-13: "Save this question for review" / "saved to your incorrect-answer bank"
--
-- #224: a saved link names the ITEM and the exact VERSION that was on screen when it was saved, so
-- reopening it renders what the learner saved even after a correction, a retirement, or a withdrawal
-- as defective (D21). It is not "a link to an item": a re-versioned item must leave an existing link
-- alone, which only the pinned version can do.
--
-- Two shapes live here. A pool link names (ItemId, ItemVersionId); the legacy link names QuestionId,
-- because the exam-owned Questions row it points at survived #231.
-- CK_SavedQuestions_Shape refuses a row that is half of either or both at once, and the two filtered
-- unique indexes below carry the "one bookmark per thing" rule for whichever shape a row is.
CREATE TABLE SavedQuestions (
  -- The row's own identity, and the address the bank's review and remove routes use: an item id and
  -- a question id are two counters that can collide, so neither can address a row on its own.
  SavedQuestionId INT IDENTITY(1,1) PRIMARY KEY,
  UserId     INT NOT NULL REFERENCES Users(UserId),
  -- #224: which certificate's bank this link lives in. An item is certificate-agnostic (D2) and has
  -- no section to climb to one, so a demo VSTEP item would otherwise appear in every certificate's
  -- bank. #269: this is now always required, because the legacy link that could omit it is gone.
  CertId     INT NOT NULL REFERENCES Certificates(CertId),
  -- #224: the pool item saved, and the version that was rendered when it was saved.
  ItemId        INT NOT NULL,
  ItemVersionId INT NOT NULL,
  Source     VARCHAR(10) NOT NULL CONSTRAINT CK_SavedQ_Source CHECK (Source IN ('manual','incorrect')),
  SavedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- #224: the saved version must be a version of the saved item - the same composite idiom
  -- ExamItems, AttemptAnswers and PracticeAnswers use, so a link can never name one item and another
  -- item's version.
  CONSTRAINT FK_SavedQuestions_VersionItem FOREIGN KEY (ItemVersionId, ItemId)
    REFERENCES ItemVersions(ItemVersionId, ItemId)
);
GO
-- One bookmark per item, not per version: re-saving an item at a newer version is the same bookmark,
-- and the row keeps the version it already pinned. #269: the filtered legacy index and the now-always-
-- present CertId make this the only uniqueness the bank needs.
CREATE UNIQUE INDEX UQ_SavedQuestions_Item ON SavedQuestions(UserId, ItemId);
GO
-- The bank lists and counts a learner's links per certificate.
CREATE INDEX IX_SavedQuestions_User_Cert ON SavedQuestions(UserId, CertId);
GO
-- #155: mastery is a fact about a Category, never about a single Question. Spaced repetition over
-- one question teaches that question's answer, not the grammar behind it, so one row per
-- (learner, category) is the unit the scheduler moves through the four states.
CREATE TABLE UserCategoryMasteries (
  UserId         INT NOT NULL REFERENCES Users(UserId),
  CategoryId     INT NOT NULL,
  -- carried down so a mastery row can never straddle two certificates (CRITICAL invariant #4)
  CertId         INT NOT NULL,
  State          VARCHAR(12) NOT NULL CONSTRAINT DF_UCM_State DEFAULT 'untested'
    CONSTRAINT CK_UCM_State CHECK (State IN ('untested','learning','reviewing','mastered')),
  -- the same numbers vw_UserTopicAccuracy now gives, kept here so the scheduler needs no join
  -- and so a pruned practice session cannot silently rewind a state the learner already earned
  AnsweredCount  INT NOT NULL CONSTRAINT DF_UCM_Answered DEFAULT 0 CONSTRAINT CK_UCM_Answered CHECK (AnsweredCount >= 0),
  CorrectCount   INT NOT NULL CONSTRAINT DF_UCM_Correct DEFAULT 0,
  -- consecutive correct answers within the category; any wrong answer resets it to 0
  Streak         INT NOT NULL CONSTRAINT DF_UCM_Streak DEFAULT 0 CONSTRAINT CK_UCM_Streak CHECK (Streak >= 0),
  -- 1 while the most recent answers are wrong: two in a row while Mastered is a lapse
  ConsecutiveWrong INT NOT NULL CONSTRAINT DF_UCM_ConsecutiveWrong DEFAULT 0
    CONSTRAINT CK_UCM_ConsecutiveWrong CHECK (ConsecutiveWrong >= 0),
  -- when this category may next be offered. A wrong answer sets now + 24h; a right one spaces by state.
  NextReviewDueAt DATETIMEOFFSET NULL,
  LastAnsweredAt  DATETIMEOFFSET NULL,
  -- how many distinct days have touched this category. Mastered needs ">= 2 sessions on different
  -- days"; one column counts them, the other remembers today so it is counted only once per day.
  SessionDays    INT NOT NULL CONSTRAINT DF_UCM_SessionDays DEFAULT 0
    CONSTRAINT CK_UCM_SessionDays CHECK (SessionDays >= 0),
  LastSessionDay DATE NULL,
  -- ">= 85% over >= 10 answers across >= 2 sessions on different days" -> Mastered. Kept explicitly
  -- so a later wrong answer cannot retroactively claim the learner never qualified.
  MasteredAt     DATETIMEOFFSET NULL,
  UpdatedAt      DATETIMEOFFSET NOT NULL CONSTRAINT DF_UCM_Updated DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT PK_UserCategoryMasteries PRIMARY KEY (UserId, CategoryId),
  -- the counters must agree: a category cannot have more correct answers than answers
  CONSTRAINT CK_UCM_CorrectLeAnswered CHECK (CorrectCount <= AnsweredCount),
  CONSTRAINT FK_UCM_CategoryCert FOREIGN KEY (CategoryId, CertId) REFERENCES QuestionCategories(CategoryId, CertId)
);
GO
-- The scheduler asks "which due-or-unseen categories belong to this certificate" on every start.
CREATE INDEX IX_UCM_User_Due ON UserCategoryMasteries(UserId, CertId, NextReviewDueAt);
GO
-- ============ Domain 7: MockExam ============
CREATE TABLE ExamAttempts (
  AttemptId       INT IDENTITY(1,1) PRIMARY KEY,
  UserId          INT NOT NULL REFERENCES Users(UserId),
  ExamId          INT NOT NULL REFERENCES Exams(ExamId),
  -- H-5: carried down from the exam so children can be pinned to the same certificate
  CertId              INT NOT NULL REFERENCES Certificates(CertId),
  ActiveSkillId       INT NULL,
  ActiveUnitNumber    INT NOT NULL CONSTRAINT DF_Attempts_ActiveUnitNumber DEFAULT 1,
  ActiveGroupId       INT NULL,
  AuthorizationCode   NVARCHAR(64) NULL,
  PhaseDeadlineAt     DATETIME2 NULL,
  LastAudioOffsetMs   INT NOT NULL CONSTRAINT DF_Attempts_LastAudioOffsetMs DEFAULT 0,
  CandidateNotesJson  NVARCHAR(MAX) NULL,
  PreflightVerifiedAt DATETIMEOFFSET NULL,
  Status              VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Attempts_Status CHECK (Status IN ('in_progress','submitted','graded','abandoned')),
  StartedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  ExpiresAt       DATETIMEOFFSET NOT NULL,          -- server-authoritative
  -- #216 / PLC-13: the first half of placement's inactivity expiry, `min(last_activity + 48 h,
  -- started_at + 72 h)`. #214 built the outer bound and deliberately did not invent this one.
  --
  -- It is stamped by a WRITE and never by a read: a reload that moved this column would move the
  -- expiry clock, which is exactly the "refresh is a free skip" the ticket forbids. NULL means
  -- "nothing written since the sitting opened", so the inactivity arm falls back to StartedAt.
  -- A mock ignores it - its own deadline is always shorter than 48 h - so the rule stays one rule.
  LastActivityAt  DATETIMEOFFSET NULL,
  IsAutoSubmitted BIT NOT NULL DEFAULT 0,
  -- UI-14: result "Completed 20 Sep 2026 Â· 11:42 Â· time taken 01:53:29"
  SubmittedAt     DATETIMEOFFSET NULL,
  -- UI-15: resume "You returned to question 8"
  -- #269: the paper position the learner is on, as a ROSTER id. This replaces LastQuestionId, which was a
  -- foreign key into Questions and therefore could not hold a pool position at all - a pool paper then
  -- resumed at its first unlocked position, which broke one-way AUDIO_DRIVEN navigation (the learner could
  -- step back over audio they had already heard). No FK: a position is addressed by its ExamItemId, and a
  -- binding retired mid-sitting must not delete the attempt that was sitting on it.
  CurrentExamItemId INT NULL,
  TotalScore      INT NULL,
  BandCode        VARCHAR(20) NULL,                 -- snapshot at grading
  -- #191 / D14: the writer identity and the ordering token. Both are required by the trigger below
  -- whenever TotalScore or BandCode is written; a sitting-state write declares neither.
  -- #246: which generation of the product this sitting was taken under. NULL means "written by the
  -- current code", which is every attempt created since the rebuild; a value means the row was
  -- PRESERVED across a cutover and is history rather than a sitting.
  --
  -- It is the tag D23 asks for ("completed mock attempts are preserved, tagged legacy_v1, read-only").
  -- The column is nullable and unset by default on purpose: a default would mean every new attempt
  -- claims an epoch, and the distinction between "taken before" and "taken after" is exactly what the
  -- tag exists to record.
  --
  -- Read-only is enforced by trg_Attempts_EpochIsReadOnly below, not by a convention: a preserved
  -- attempt may be read forever and written never, because its score was produced by a scoring model
  -- this build no longer has and no reader can honestly recompute it.
  EpochTag VARCHAR(16) NULL
    CONSTRAINT CK_Attempts_EpochTag CHECK (EpochTag IS NULL OR EpochTag IN ('legacy_v1')),
  -- #289: the skill scope of a placement sitting; NULL for mocks and legacy sittings.
  PlacementScope VARCHAR(24) NULL
    CONSTRAINT CK_Attempts_PlacementScope CHECK (PlacementScope IS NULL OR PlacementScope IN ('all_skills','listening_and_reading')),
  -- H-5: ExamId's CertId must be the attempt's own (pins the carried-down value)
  CONSTRAINT FK_Attempts_ExamCert FOREIGN KEY (ExamId, CertId) REFERENCES Exams(ExamId, CertId),
  CONSTRAINT FK_Attempts_ActiveSkill FOREIGN KEY (ActiveSkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- #269: the unit a locked-phase sitting is currently inside, as a BLOCK ADDRESS and not a reference:
  -- a stimulus id when the unit shares one, or the negated position id for a solo block (the same
  -- convention the Studio rail, the exporter and the roster use). No FK - a negated id names no Stimuli
  -- row, and a stimulus retired mid-sitting must not delete the attempt that was inside it.
  -- composite target so CreditTransactions can prove the charged user owns the attempt (H-1):
  CONSTRAINT UQ_Attempts_Id_User UNIQUE (AttemptId, UserId),
  -- H-5: composite targets so AttemptAnswers/FreeResponses/AttemptSkillScores can pin containment
  CONSTRAINT UQ_Attempts_Id_Exam UNIQUE (AttemptId, ExamId),
  CONSTRAINT UQ_Attempts_Id_Cert UNIQUE (AttemptId, CertId),
  -- UI-15: the resume position must be a question of this attempt's own exam
  -- #269: FK_Attempts_LastQuestionExam is gone with the column; a roster position is not a Questions row.
  CONSTRAINT CK_Attempts_SubmittedAt CHECK (SubmittedAt IS NULL OR Status IN ('submitted','graded'))
);
GO
CREATE TABLE AttemptSkillScores (
  AttemptId   INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  SkillId     INT NOT NULL REFERENCES Skills(SkillId),
  -- H-5: carried down from the attempt so the skill must belong to the attempt's own certificate
  CertId      INT NOT NULL,
  RawScore    INT NOT NULL,
  ScaledScore INT NOT NULL,
  CONSTRAINT PK_AttemptSkillScores PRIMARY KEY (AttemptId, SkillId),
  -- H-5: the attempt's own CertId must be used
  CONSTRAINT FK_ASS_AttemptCert FOREIGN KEY (AttemptId, CertId) REFERENCES ExamAttempts(AttemptId, CertId),
  -- H-5: the skill must belong to that same certificate (target exists: Skills(SkillId,CertId))
  CONSTRAINT FK_ASS_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId)
);
GO
CREATE TABLE AttemptAnswers (
  AnswerId         INT IDENTITY(1,1) PRIMARY KEY,
  AttemptId        INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  -- H-5: carried down from the attempt so the position must belong to the attempt's own exam
  ExamId           INT NOT NULL,
  -- #269: the legacy QuestionId is GONE with the Questions table. An answer is now exactly one thing -
  -- the roster position it was written against - and the version it pinned for copy-on-write reading.
  --
  -- #186: which version of which item the learner answered. Copy-on-write (CNT-43) means this pair,
  -- not the item row, is what the review renders: re-versioning or retiring the item afterwards
  -- cannot change what the attempt shows.
  ItemId           INT NOT NULL,
  ItemVersionId    INT NOT NULL,
  -- #250: the ROSTER POSITION this answer was recorded against.
  --
  -- Why a position column is needed on top of (ItemId, ItemVersionId): a paper may bind the same item
  -- version at two positions ("the same item on two papers, or twice on one paper, gets two positions"),
  -- so the version pair does not identify WHICH question of the paper was answered. Without this column
  -- two answers to one repeated item are indistinguishable, and the navigator cannot mark the right cell
  -- as answered. The position is the answer's identity.
  ExamItemId       INT NOT NULL REFERENCES ExamItems(ExamItemId),
  -- #186: which of the pinned version's four options was picked. An ItemVersion stores labels A-D,
  -- not option rows, so there is no OptionId to point at; SelectedOptionId is kept nullable for the
  -- text and multi-select shapes and stays NULL whenever a label is set. The pair is the answer's
  -- whole record of what was chosen, which is what makes a re-versioned item unable to change it.
  SelectedLabel    VARCHAR(1) NULL
    CONSTRAINT CK_AttemptAnswers_SelectedLabel CHECK (SelectedLabel IN ('A','B','C','D')),
  SelectedOptionId      INT NULL,
  ResponseText          NVARCHAR(MAX) NULL,
  SelectedOptionIdsJson NVARCHAR(MAX) NULL,
  IsCorrect             BIT NULL,
  -- UI-16: "Flag for review", "Flag 7", review filter "Only flagged questions"
  IsFlagged        BIT NOT NULL CONSTRAINT DF_AttemptAnswers_IsFlagged DEFAULT 0,
  -- #167: no AnsweredAt here. Sitting timing is not measured for regular attempts and nothing
  -- reads it; only PracticeAnswers.AnsweredAt feeds the calibration rushed-answer filter.
  -- #250: one answer per roster position is what the navigator counts, and it is the only statement
  -- that stays true when a paper binds one version twice.
  INDEX UX_AttemptAnswers_ExamItem UNIQUE (AttemptId, ExamItemId),
  -- H-5: ExamId must be the attempt's own
  CONSTRAINT FK_AttemptAnswers_AttemptExam FOREIGN KEY (AttemptId, ExamId) REFERENCES ExamAttempts(AttemptId, ExamId),
  -- #186: the answered version must be a version of the answered item - the same composite idiom
  -- ExamItems uses, so one row cannot name one item and another item's version.
  CONSTRAINT FK_AttemptAnswers_VersionItem FOREIGN KEY (ItemVersionId, ItemId) REFERENCES ItemVersions(ItemVersionId, ItemId),
  -- #269: the position must belong to the attempt's OWN paper. This replaced
  -- FK_AttemptAnswers_QuestionExam, which did the same job through Questions(QuestionId, ExamId).
  -- Measured without it: an attempt on a TOEIC paper accepted an IELTS position.
  CONSTRAINT FK_AttemptAnswers_ExamItemExam FOREIGN KEY (ExamItemId, ExamId) REFERENCES ExamItems(ExamItemId, ExamId)
);
GO
-- CNT-43: no hard delete. An item that has left draft, or that any learner has answered, is retired
-- instead; retirement removes it from future assembly and leaves every past attempt renderable.
--
-- It is created here rather than beside Items with its sibling above, because it names ItemVersions,
-- AttemptAnswers and PracticeAnswers by name. SQL Server defers name resolution inside a trigger body,
-- so it would create successfully wherever it was written and fail only when someone first deleted an
-- item; creating it where all three exist makes a missing table a load-time failure instead of a
-- discovery in production.
--
-- The boundary is the whole point, so both halves are named. A trigger that refused every DELETE
-- would be trivially simple and wrong: a draft item nothing points at is exactly what an author is
-- allowed to throw away.
--
-- INSTEAD OF, not AFTER, and that is not a style choice. Every answer pins an item version, and
-- FK_ItemVersions_Item refuses to orphan a version, so the foreign key raises its own error before
-- an AFTER trigger ever runs. An AFTER clause naming the learner reference would then be dead code:
-- the statement is already refused, by a message that says nothing about CNT-43. As INSTEAD OF this
-- trigger is the first thing to see the intent, so the caller is told which rule they hit.
--
-- Because it is INSTEAD OF it must perform the delete. That is also where "a draft item must be
-- deletable" becomes true: deleting a draft item means deleting its versions first, which the version
-- foreign key otherwise makes impossible - and every authored item has a version (#188 writes item
-- and version in one save). A draft item bound to a paper is still refused, by the binding's own
-- foreign keys; that is not a learner reference and CNT-43 does not name it.
--
-- The two clauses carry different messages on purpose. A caller told only "CNT-43 refused this" cannot
-- tell "it left draft" from "a learner touched it", and a test that matched only the code could not
-- tell a working first clause from one that fired on every draft.
CREATE TRIGGER trg_Items_NoHardDelete
ON Items INSTEAD OF DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted d WHERE d.LifecycleStatus <> 'draft')
    THROW 50061, 'CNT-43: an item that has left draft cannot be deleted; retire it instead.', 1;
  IF EXISTS (
    SELECT 1
    FROM deleted d
    WHERE EXISTS (SELECT 1 FROM ItemVersions v
                  JOIN AttemptAnswers aa ON aa.ItemVersionId = v.ItemVersionId
                  WHERE v.ItemId = d.ItemId)
       OR EXISTS (SELECT 1 FROM ItemVersions v
                  JOIN PracticeAnswers pa ON pa.ItemVersionId = v.ItemVersionId
                  WHERE v.ItemId = d.ItemId)
  )
    THROW 50062, 'CNT-43: an item a learner has answered cannot be deleted; retire it instead.', 1;

  -- Draft, and no learner has touched it. The versions go with it; anything else still pointing at
  -- either row - a paper binding, a saved question - is refused by that table's own foreign key, so
  -- this cascade cannot reach a reference this trigger does not name.
  DELETE v FROM ItemVersions v JOIN deleted d ON d.ItemId = v.ItemId;
  DELETE i FROM Items i JOIN deleted d ON d.ItemId = i.ItemId;
END;
GO
CREATE TABLE FreeResponses (
  FreeResponseId INT IDENTITY(1,1) PRIMARY KEY,
  AttemptId      INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  -- #269: the legacy QuestionId shape is GONE with the Questions table. A response names the roster
  -- position it was recorded against.
  ExamItemId     INT NOT NULL REFERENCES ExamItems(ExamItemId),
  -- H-5: carried down from the attempt so the position must belong to the attempt's own exam
  ExamId         INT NOT NULL,
  -- M-3: carried down from the position's section so the response's skill can be pinned to that
  -- section's own. This replaced the chain through Questions(QuestionId, SkillId).
  SectionId      INT NOT NULL,
  -- M-3: carried down from the position's skill so AiGradings must name that skill
  SkillId        INT NOT NULL,
  -- M-7: pins the shape (a CHECK cannot reach the item's render format, and a productive task is the
  -- only thing that produces a free response)
  QuestionType   VARCHAR(30) NOT NULL
    CONSTRAINT CK_FreeResp_QuestionType CHECK (QuestionType = 'free_response'),
  ResponseText   NVARCHAR(MAX) NULL,
  AudioPath      NVARCHAR(400) NULL,
  -- UI-17 (speaking gap 4): "You spoke for 27 of 30 seconds"
  DurationMs     INT NULL CONSTRAINT CK_FreeResp_Duration CHECK (DurationMs IS NULL OR DurationMs > 0),
  -- UI-40: exam-writing autosaves a 'draft' (restored after a reload); submit turns it into 'pending_ai'.
  -- A draft has no SubmittedAt; insert drafts with SubmittedAt = NULL explicitly (the default is for submits).
  Status         VARCHAR(12) NOT NULL DEFAULT 'pending_ai'
    CONSTRAINT CK_FreeResp_Status CHECK (Status IN ('draft','pending_ai','graded')),
  DraftSavedAt   DATETIMEOFFSET NULL,      -- UI-40: "Saved 11:42:05"
  SubmittedAt    DATETIMEOFFSET NULL DEFAULT SYSDATETIMEOFFSET(),
  -- #216 / PLC-15: the recording retry. `RecordingInterruptions` is the count of interrupted
  -- recordings for THIS task, and it is the reason a reload cannot grant a fresh retry: the count is
  -- durable server state, not a page-load counter. The cap is enforced here rather than trusted to
  -- the service, because a third interruption must never become a third retry.
  --
  -- `RecordingSessionToken` is the token of the last interruption already counted. The client mints
  -- one per recording attempt; a reload that re-sends the same token is the same interruption and is
  -- ignored. That is the whole difference between "counted per interruption" and "counted per page
  -- load", and no other column can express it.
  RecordingInterruptions INT NOT NULL CONSTRAINT DF_FreeResp_RecordingInterruptions DEFAULT 0,
  RecordingSessionToken  NVARCHAR(64) NULL,
  CONSTRAINT CK_FreeResp_InterruptionCap CHECK (RecordingInterruptions BETWEEN 0 AND 2),
  CONSTRAINT CK_FreeResp_DraftNotSubmitted CHECK (
       (Status = 'draft'  AND SubmittedAt IS NULL)
    OR (Status <> 'draft' AND SubmittedAt IS NOT NULL)
  ),
  -- #250: one response per roster position.
  INDEX UX_FreeResponses_ExamItem UNIQUE (AttemptId, ExamItemId),
  CONSTRAINT CK_FreeResp_HasContent CHECK (ResponseText IS NOT NULL OR AudioPath IS NOT NULL),
  -- H-5: ExamId must be the attempt's own
  CONSTRAINT FK_FreeResp_AttemptExam FOREIGN KEY (AttemptId, ExamId) REFERENCES ExamAttempts(AttemptId, ExamId),
  -- #269: the position must belong to the attempt's own paper, and the response's skill must be the
  -- position's own. These replaced FK_FreeResp_QuestionExam / _QuestionSkill, which did both through
  -- Questions. Measured without them: a reading position accepted a response claiming the speaking skill.
  CONSTRAINT FK_FreeResp_ExamItemExam FOREIGN KEY (ExamItemId, ExamId) REFERENCES ExamItems(ExamItemId, ExamId),
  CONSTRAINT FK_FreeResp_ExamItemSection FOREIGN KEY (ExamItemId, SectionId) REFERENCES ExamItems(ExamItemId, SectionId),
  CONSTRAINT FK_FreeResp_SectionSkill FOREIGN KEY (SectionId, SkillId) REFERENCES Sections(SectionId, SkillId),
  -- M-3: composite target so AiGradings can require its SkillId to be this response's own
  CONSTRAINT UQ_FreeResp_Id_Skill UNIQUE (FreeResponseId, SkillId)
);
GO
-- attempts may only target published exams (carried from old schema)
-- M-5: also cover UPDATE so an attempt can't be moved onto a draft exam after creation
CREATE TRIGGER trg_ExamAttempts_PublishedExamOnly ON ExamAttempts AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM inserted i JOIN Exams e ON e.ExamId=i.ExamId WHERE e.Status <> 'published')
  BEGIN
    THROW 50010, 'Attempt must target a published exam.', 1;
  END
END;
GO
-- #246: a PRESERVED attempt is history, and history is read-only.
--
-- D23 preserves completed mock attempts across a cutover and the ticket asks for two things of them:
-- that they carry an epoch tag, and that they are read-only thereafter. The tag alone would be a label;
-- this trigger is what makes "read-only" a fact. It refuses any write that touches a row whose EpochTag
-- is set, except the one write that sets it (an import must be able to TAG the row it is preserving).
--
-- Why refuse rather than trust the services: the row's score was produced by a scoring model this build
-- no longer has. A reader that recomputed it would produce a different number from the same answers, and
-- the two would disagree on the learner's record forever - the class of defect ADR-0012 exists to
-- prevent. The row is preserved exactly so it is never recalculated.
CREATE TRIGGER trg_Attempts_EpochIsReadOnly
ON ExamAttempts AFTER UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;

  -- A DELETE leaves `inserted` empty and an UPDATE does not. That is the only reliable discriminator:
  -- both fire with rows in `deleted`, so the first draft of this trigger reported every UPDATE as a
  -- DELETE and the message named the wrong rule.
  IF NOT EXISTS (SELECT 1 FROM inserted)
  BEGIN
    IF EXISTS (SELECT 1 FROM deleted WHERE EpochTag IS NOT NULL)
      THROW 50029, '#246: a preserved attempt is history and cannot be deleted. Its epoch tag means it was carried across a cutover precisely so the learner keeps that sitting.', 1;
    RETURN;
  END

  -- An UPDATE is refused when the row ALREADY carried a tag and anything at all moved. Setting the tag
  -- on an untagged row (the import's own act) is the one permitted write.
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN deleted d ON d.AttemptId = i.AttemptId
    WHERE d.EpochTag IS NOT NULL
  )
  BEGIN
    THROW 50029, '#246: a preserved attempt is read-only. Its score came from a scoring model this build no longer has, so recomputing it would silently disagree with the learner''s record.', 1;
  END
END;
GO
-- #227 criterion 5: an attempt cannot be edited into a different edition. `ExamAttempts.ExamId` IS
-- the edition reference, and both of its foreign keys admit a sibling edition of the same
-- certificate, so without this an UPDATE could move a finished attempt onto a paper it never sat.
CREATE TRIGGER trg_ExamAttempts_EditionIsFixed
ON ExamAttempts AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN deleted d ON d.AttemptId = i.AttemptId
    WHERE i.ExamId <> d.ExamId
  )
    THROW 50111, 'An attempt records the edition it sat. Moving an attempt to another exam - another edition - is refused: a review would then render a paper the learner never saw.', 1;
END;
GO
-- UI-45: a speaking- or writing-practice attempt scores one rubric against one skill, so the attempt
-- itself never claims a certificate total ("out of 990"). TotalScore stays NULL for those attempts.
-- E1.3 (#148): the band arm is gone. One skill's rubric, converted over the certificate's own bands,
-- is a legitimate estimate, and it is written to AiGradings.BandCode as a snapshot.
CREATE TRIGGER trg_Attempts_NoTotalScoreForSkillPractice
ON ExamAttempts AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i JOIN Exams e ON e.ExamId = i.ExamId
    WHERE e.Purpose IN ('speaking_practice','writing_practice') AND i.TotalScore IS NOT NULL  -- UI-45
  )
    THROW 50041, 'A speaking- or writing-practice attempt cannot carry a total score.', 1;
END;
GO
-- TotalScore uses the aggregation rule the ATTEMPT's edition froze when one exists, and the
-- certificate's column only as the fallback for an attempt with no edition row.
--
-- #257: this trigger used to read `Certificates.ScoreAggregationRule` alone, which made it the second
-- half of the defect #227 exists to close. Re-standardise a certificate from `sum` to `avg_half_step`
-- after a sitting, and every stored total becomes "wrong" to this trigger while still being exactly the
-- number that sitting produced - so a re-score could not be written at all, and a legitimate correction
-- was refused for agreeing with history. The edition is the record of what was in force; the absence of
-- an edition row is the record that nothing was frozen, and that attempt keeps reading today's column.
CREATE TRIGGER trg_Attempts_TotalMatchesSkills
ON AttemptSkillScores AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT a.AttemptId
    FROM ExamAttempts a
    JOIN Certificates c ON c.CertId = a.CertId
    CROSS APPLY (
      SELECT
        SUM(s.ScaledScore) AS SumScore,
        AVG(CONVERT(DECIMAL(10,4), s.ScaledScore)) AS AverageScore
      FROM AttemptSkillScores s
      WHERE s.AttemptId = a.AttemptId
    ) scores
    WHERE a.TotalScore IS NOT NULL
      AND a.AttemptId IN (SELECT AttemptId FROM inserted UNION SELECT AttemptId FROM deleted)
      AND a.TotalScore <> CASE c.ScoreAggregationRule
        WHEN 'avg_half_step' THEN CONVERT(INT, FLOOR((COALESCE(scores.AverageScore, 0) + 2.5) / 5.0) * 5)
        WHEN 'avg_whole' THEN CONVERT(INT, ROUND(COALESCE(scores.AverageScore, 0), 0))
        ELSE COALESCE(scores.SumScore, 0)
      END
  )
    THROW 50011, 'TotalScore does not match the aggregation rule in force for this attempt.', 1;
END;
GO
-- same check when TotalScore itself changes
CREATE TRIGGER trg_Attempts_TotalMatchesSkills_OnAttempt
ON ExamAttempts AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF UPDATE(TotalScore) AND EXISTS (
    SELECT i.AttemptId
    FROM inserted i
    JOIN Certificates c ON c.CertId = i.CertId
    CROSS APPLY (
      SELECT
        SUM(s.ScaledScore) AS SumScore,
        AVG(CONVERT(DECIMAL(10,4), s.ScaledScore)) AS AverageScore
      FROM AttemptSkillScores s
      WHERE s.AttemptId = i.AttemptId
    ) scores
    WHERE i.TotalScore IS NOT NULL
      AND EXISTS (SELECT 1 FROM AttemptSkillScores s WHERE s.AttemptId=i.AttemptId)
      AND i.TotalScore <> CASE c.ScoreAggregationRule
        WHEN 'avg_half_step' THEN CONVERT(INT, FLOOR((COALESCE(scores.AverageScore, 0) + 2.5) / 5.0) * 5)
        WHEN 'avg_whole' THEN CONVERT(INT, ROUND(COALESCE(scores.AverageScore, 0), 0))
        ELSE COALESCE(scores.SumScore, 0)
      END
  )
    THROW 50011, 'TotalScore does not match the aggregation rule in force for this attempt.', 1;
END;
GO
-- M-6: an attempt cannot reach 'graded' while any of its free responses is still 'pending_ai'
CREATE TRIGGER trg_Attempts_NoGradedWithPendingFreeResponses
ON ExamAttempts AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    WHERE i.Status = 'graded'
      AND EXISTS (SELECT 1 FROM FreeResponses fr WHERE fr.AttemptId = i.AttemptId AND fr.Status <> 'graded')
  )
    THROW 50025, 'Attempt cannot be graded while a free response is still pending_ai.', 1;
END;
GO
-- R4: the guard above only watches the ATTEMPT side. The same invariant breaks from the child
-- side: a pending_ai free response inserted into an already-'graded' attempt leaves the attempt
-- graded with ungraded work inside it. Close it on the FreeResponses side too.
CREATE TRIGGER trg_FreeResponses_NotIntoGradedAttempt
ON FreeResponses AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN ExamAttempts a ON a.AttemptId = i.AttemptId
    WHERE a.Status = 'graded' AND i.Status <> 'graded'
  )
    THROW 50030, 'A free response that is not graded cannot belong to a graded attempt.', 1;
END;
GO
-- UI-40: submit turns every draft into pending_ai first; an attempt past in_progress holds no draft
CREATE TRIGGER trg_Attempts_NoDraftsAfterSubmit
ON ExamAttempts AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    WHERE i.Status IN ('submitted','graded')
      AND EXISTS (SELECT 1 FROM FreeResponses fr WHERE fr.AttemptId = i.AttemptId AND fr.Status = 'draft')
  )
    THROW 50039, 'Submit every draft before the attempt is submitted.', 1;
END;
GO
CREATE TRIGGER trg_FreeResponses_DraftOnlyInProgress
ON FreeResponses AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN ExamAttempts a ON a.AttemptId = i.AttemptId
    WHERE i.Status = 'draft' AND a.Status <> 'in_progress'
  )
    THROW 50039, 'A draft can only belong to an attempt in progress.', 1;
END;
GO
-- #191 / D14: one writer per derived fact, enforced in the database rather than agreed by
-- convention. The derived fact on this table is TotalScore + BandCode; a sitting-state write
-- (Status, ExpiresAt, LastQuestionId, ...) derives nothing and stays legal with no declaration, so
-- this is not a blanket refusal on the busiest table in the schema.
--
-- PRN-01: the writer is a column on the write, not `sp_set_session_context`. Session context survives
-- commit and rollback, so a name left in it would be read by the next transaction on that pooled
-- connection and would vouch for code that never declared itself.
--
-- The predicate looks at what CHANGED, not at what the row now holds. An attempt that is already
-- graded has a non-null TotalScore; checking "is it non-null" would make every later status write
-- look like a scoring write and refuse it.
-- #309: trg_Attempts_SingleWriter removed (Expand phase).

-- ============ Domain 8: AI Grading ============
CREATE TABLE GradingCriteria (
  CriterionId  INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  SkillId      INT NOT NULL,
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  -- E1.2 (#147): the visible description of each score level. Without it the grader was told to
  -- "score according to the rubric" while no rubric text existed anywhere in the system.
  RubricDescription NVARCHAR(MAX) NULL,
  MaxScore     DECIMAL(5,2) NOT NULL CONSTRAINT CK_Criteria_MaxScore CHECK (MaxScore > 0),
  Weight       DECIMAL(5,2) NOT NULL DEFAULT 1.0,
  DisplayOrder INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Criteria_Skill_Code UNIQUE (SkillId, Code),
  -- M-3: the criterion's skill must belong to the criterion's own certificate
  CONSTRAINT FK_GradingCriteria_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- composite target so AiGradingScores can carry SkillId down from its criterion
  CONSTRAINT UQ_GradingCriteria_Id_Skill UNIQUE (CriterionId, SkillId)
);
GO
-- UI-20 (speaking gaps 2 + 3): a section declares which of its skill's criteria apply, and may
-- override the ceiling (TOEIC Speaking Q1-10 score 0-3, Q11 scores 0-5 on the same criteria).
-- A section with no rows here accepts every criterion of its skill at GradingCriteria.MaxScore.
CREATE TABLE SectionCriteria (
  SectionId   INT NOT NULL,
  CriterionId INT NOT NULL,
  SkillId     INT NOT NULL,
  MaxScore    DECIMAL(5,2) NULL CONSTRAINT CK_SectionCriteria_MaxScore CHECK (MaxScore IS NULL OR MaxScore > 0),
  CONSTRAINT PK_SectionCriteria PRIMARY KEY (SectionId, CriterionId),
  -- section and criterion must belong to the same skill
  CONSTRAINT FK_SectionCriteria_SectionSkill   FOREIGN KEY (SectionId, SkillId)   REFERENCES Sections(SectionId, SkillId),
  CONSTRAINT FK_SectionCriteria_CriterionSkill FOREIGN KEY (CriterionId, SkillId) REFERENCES GradingCriteria(CriterionId, SkillId)
);
GO
CREATE TABLE AiGradings (
  GradingId      INT IDENTITY(1,1) PRIMARY KEY,
  FreeResponseId INT NOT NULL REFERENCES FreeResponses(FreeResponseId),
  -- M-3: no longer a bare Skills FK â€” it must be the graded response's OWN skill (see FK below)
  SkillId        INT NOT NULL,
  Status         VARCHAR(8) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_AiGradings_Status CHECK (Status IN ('pending','done','failed')),
  OverallScaled  DECIMAL(6,2) NULL,
  BandCode       VARCHAR(20) NULL,             -- snapshot
  Model          NVARCHAR(80) NULL,
  -- UI-35: speaking-review "Grading did not finish Â· Retry grading" (retry is free: one ai_grading
  -- charge per attempt). A retry reuses this row: Status back to 'pending', AttemptCount + 1.
  FailureReason  NVARCHAR(400) NULL,
  -- E2.1 (#151): the feedback a learner can act on. An error span quotes the candidate's own text,
  -- so the page can show original -> corrected; all three stay NULL when the grader has nothing to
  -- fix, and a failed grading never carries any (Status = 'failed' means "not scored", not "bad").
  PriorityFix    NVARCHAR(1000) NULL,
  ModelRewrite   NVARCHAR(MAX) NULL,
  ErrorSpansJson NVARCHAR(MAX) NULL,          -- <=5 {originalSnippet, correctedSnippet, category, explanationVi}
  AttemptCount   INT NOT NULL CONSTRAINT DF_AiGradings_AttemptCount DEFAULT 1
    CONSTRAINT CK_AiGradings_AttemptCount CHECK (AttemptCount BETWEEN 1 AND 5),
  RequestedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CompletedAt    DATETIMEOFFSET NULL,
  CONSTRAINT UQ_AiGradings_FreeResponse UNIQUE (FreeResponseId),
  CONSTRAINT CK_AiGradings_FailureReason CHECK (FailureReason IS NULL OR Status = 'failed'),
  -- E2.1 (#151): feedback belongs to a finished grading only. A pending row has not been read, and a
  -- failed one was never scored - writing actionable notes onto it would claim work that did not happen.
  CONSTRAINT CK_AiGradings_FeedbackOnlyWhenDone
    CHECK (Status = 'done' OR (PriorityFix IS NULL AND ModelRewrite IS NULL AND ErrorSpansJson IS NULL)),
  CONSTRAINT CK_AiGradings_CompletedAt CHECK (CompletedAt IS NULL OR Status IN ('done','failed')),
  -- composite target so AiGradingScores can require the criterion's skill = the grading's own skill
  CONSTRAINT UQ_AiGradings_Id_Skill UNIQUE (GradingId, SkillId),
  -- M-3 clause 3: the graded skill must be the free response's own skill, which the chain
  -- FreeResponses â†’ Questions â†’ Sections pins to a single certificate
  CONSTRAINT FK_AiGradings_ResponseSkill FOREIGN KEY (FreeResponseId, SkillId) REFERENCES FreeResponses(FreeResponseId, SkillId)
);
GO
-- UI-40: a draft is not graded (nor charged for) until it is submitted
CREATE TRIGGER trg_AiGradings_NotOnDraft
ON AiGradings AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i JOIN FreeResponses fr ON fr.FreeResponseId = i.FreeResponseId
    WHERE fr.Status = 'draft'
  )
    THROW 50040, 'A draft response cannot be graded.', 1;
END;
GO
-- M-6: flip FreeResponses.Status to 'graded' once its AiGradings.Status reaches 'done'
CREATE TRIGGER trg_AiGradings_MarkFreeResponseGraded
ON AiGradings AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  UPDATE fr
    SET Status = 'graded'
  FROM FreeResponses fr
  JOIN inserted i ON i.FreeResponseId = fr.FreeResponseId
  WHERE i.Status = 'done' AND fr.Status <> 'graded';
END;
GO
CREATE TABLE AiGradingScores (
  GradingId   INT NOT NULL,
  CriterionId INT NOT NULL,
  -- M-3: carried down from the grading so the criterion's own skill must match
  SkillId     INT NOT NULL,
  Score       DECIMAL(5,2) NOT NULL CONSTRAINT CK_AGS_ScoreNonNegative CHECK (Score >= 0),
  Comment     NVARCHAR(MAX) NULL,
  CONSTRAINT PK_AiGradingScores PRIMARY KEY (GradingId, CriterionId),
  -- M-3: SkillId must be the grading's own
  CONSTRAINT FK_AGS_GradingSkill FOREIGN KEY (GradingId, SkillId) REFERENCES AiGradings(GradingId, SkillId),
  -- M-3: the criterion's own skill must be that same skill (pins skill AND cert transitively)
  CONSTRAINT FK_AGS_CriterionSkill FOREIGN KEY (CriterionId, SkillId) REFERENCES GradingCriteria(CriterionId, SkillId)
);
GO
-- M-9: a rubric score cannot exceed its criterion's MaxScore (a CHECK can't reach another table)
-- UI-20: the ceiling is the section override when one exists, else the criterion's own.
CREATE TRIGGER trg_AiGradingScores_BoundedByMaxScore
ON AiGradingScores AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN GradingCriteria c ON c.CriterionId = i.CriterionId
    JOIN AiGradings g      ON g.GradingId = i.GradingId
    JOIN FreeResponses fr  ON fr.FreeResponseId = g.FreeResponseId
    -- #269: a free response names the roster position it was recorded against, and the position names
    -- the section. The legacy Questions arm went with that table.
    LEFT JOIN ExamItems ei ON ei.ExamItemId = fr.ExamItemId
    LEFT JOIN SectionCriteria sc
           ON sc.CriterionId = i.CriterionId
          AND sc.SectionId = ei.SectionId
    WHERE i.Score > COALESCE(sc.MaxScore, c.MaxScore)
  )
    THROW 50026, 'AiGradingScores.Score cannot exceed the criterion MaxScore.', 1;
  -- UI-20: a section that lists its criteria accepts only those
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN AiGradings g     ON g.GradingId = i.GradingId
    JOIN FreeResponses fr ON fr.FreeResponseId = g.FreeResponseId
    JOIN ExamItems ei     ON ei.ExamItemId = fr.ExamItemId
    CROSS APPLY (SELECT ei.SectionId AS SectionId) sec
    WHERE sec.SectionId IS NOT NULL
      AND EXISTS (SELECT 1 FROM SectionCriteria x WHERE x.SectionId = sec.SectionId)
      AND NOT EXISTS (SELECT 1 FROM SectionCriteria x WHERE x.SectionId = sec.SectionId AND x.CriterionId = i.CriterionId)
  )
    THROW 50033, 'This criterion does not apply to the question''s section.', 1;
END;
GO
-- UI-20: same R7 guard for the section-level ceiling
CREATE TRIGGER trg_SectionCriteria_MaxScoreNotBelowStored
ON SectionCriteria AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted sc
    JOIN ExamItems ei ON ei.SectionId = sc.SectionId
    JOIN FreeResponses fr ON fr.ExamItemId = ei.ExamItemId
    JOIN AiGradings g     ON g.FreeResponseId = fr.FreeResponseId
    JOIN AiGradingScores s ON s.GradingId = g.GradingId AND s.CriterionId = sc.CriterionId
    JOIN GradingCriteria c ON c.CriterionId = sc.CriterionId
    WHERE s.Score > COALESCE(sc.MaxScore, c.MaxScore)
  )
    THROW 50034, 'A section ceiling cannot be set below a score already recorded against it.', 1;
END;
GO
-- R7: the bound is only checked when a SCORE is written. Lowering a criterion's MaxScore
-- afterwards left stored scores above their own ceiling, so OverallScaled and the band snapshot
-- derived from it become unbounded again by editing the rubric instead of the score.
CREATE TRIGGER trg_GradingCriteria_MaxScoreNotBelowStored
ON GradingCriteria AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN AiGradingScores s ON s.CriterionId = i.CriterionId
    WHERE s.Score > i.MaxScore
  )
    THROW 50031, 'MaxScore cannot be lowered below a score already recorded against this criterion.', 1;
END;
GO
-- ADR-0004: one AI narrative about a learner's progress, as shown on the dashboard, practice-summary,
-- exam-result, weakness-analysis and path-detail. Distinct from AiGradings, which scores ONE free
-- response against a rubric; an insight reads the learner's accuracy history and says what it means.
CREATE TABLE AiInsights (
  InsightId   INT IDENTITY(1,1) PRIMARY KEY,
  UserId      INT NOT NULL REFERENCES Users(UserId),
  CertId      INT NOT NULL REFERENCES Certificates(CertId),
  Scope       VARCHAR(16) NOT NULL
    CONSTRAINT CK_AiInsights_Scope CHECK (Scope IN ('practice_session','attempt','path','dashboard')),
  PracticeSessionId INT NULL,
  AttemptId         INT NULL,
  LearnerPathId     INT NULL,
  -- the prose the learner reads ("stronger in reading, Part 5 slipped")
  Narrative   NVARCHAR(MAX) NULL,
  Trend       VARCHAR(10) NULL
    CONSTRAINT CK_AiInsights_Trend CHECK (Trend IN ('improving','flat','declining')),
  Status      VARCHAR(8) NOT NULL CONSTRAINT DF_AiInsights_Status DEFAULT 'pending'
    CONSTRAINT CK_AiInsights_Status CHECK (Status IN ('pending','done','failed')),
  FailureReason NVARCHAR(400) NULL,
  Model       NVARCHAR(80) NULL,
  PromptTokens     INT NULL,
  CompletionTokens INT NULL,
  -- hash of the inputs this insight was generated from. Opening the dashboard ten times must not
  -- call the model ten times: unchanged inputs mean the stored insight is still current.
  SourceFingerprint CHAR(64) NOT NULL,
  CreatedAt   DATETIMEOFFSET NOT NULL CONSTRAINT DF_AiInsights_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
  CompletedAt DATETIMEOFFSET NULL,
  CONSTRAINT CK_AiInsights_FailureReason CHECK (FailureReason IS NULL OR Status = 'failed'),
  CONSTRAINT CK_AiInsights_CompletedAt CHECK (CompletedAt IS NULL OR Status IN ('done','failed')),
  CONSTRAINT CK_AiInsights_Narrative CHECK (Status <> 'done' OR Narrative IS NOT NULL),
  -- each scope names exactly the source it is about; a dashboard insight names none
  CONSTRAINT CK_AiInsights_Source CHECK (
       (Scope='practice_session' AND PracticeSessionId IS NOT NULL AND AttemptId IS NULL AND LearnerPathId IS NULL)
    OR (Scope='attempt'          AND AttemptId IS NOT NULL AND PracticeSessionId IS NULL AND LearnerPathId IS NULL)
    OR (Scope='path'             AND LearnerPathId IS NOT NULL AND PracticeSessionId IS NULL AND AttemptId IS NULL)
    OR (Scope='dashboard'        AND PracticeSessionId IS NULL AND AttemptId IS NULL AND LearnerPathId IS NULL)
  ),
  -- H-1 style composite FKs: the named source must be this same learner's own
  CONSTRAINT FK_AiInsights_SessionUser FOREIGN KEY (PracticeSessionId, UserId) REFERENCES PracticeSessions(SessionId, UserId),
  CONSTRAINT FK_AiInsights_AttemptUser FOREIGN KEY (AttemptId, UserId) REFERENCES ExamAttempts(AttemptId, UserId),
  CONSTRAINT UQ_AiInsights_Id_User UNIQUE (InsightId, UserId)
);
GO
-- one insight per set of inputs: the reuse check that keeps cost proportional to learner activity
-- rather than to page views
CREATE UNIQUE INDEX UX_AiInsights_Fingerprint ON AiInsights(UserId, Scope, SourceFingerprint);
GO
-- the dashboard reads the newest done insight for the active certificate
CREATE INDEX IX_AiInsights_Latest ON AiInsights(UserId, CertId, Scope, CreatedAt DESC) WHERE Status = 'done';
GO
-- the background generator's queue, mirroring IX_AiGradings_Queue
CREATE INDEX IX_AiInsights_Queue ON AiInsights(Status, CreatedAt) WHERE Status = 'pending';
GO
CREATE TABLE Recommendations (
  RecId          INT IDENTITY(1,1) PRIMARY KEY,
  UserId         INT NOT NULL REFERENCES Users(UserId),
  CertId         INT NOT NULL REFERENCES Certificates(CertId),
  GradingId      INT NULL REFERENCES AiGradings(GradingId),   -- NULL = not from rubric grading
  -- ADR-0004: the aggregate insight that produced this recommendation, when one did
  InsightId      INT NULL,
  -- UI-21: exam-result "To reach B2 (your target): â€¦" lists recommendations for THIS attempt
  AttemptId      INT NULL,
  -- UI-43: practice-summary "Next: review present perfect" lists recommendations for THIS session
  PracticeSessionId INT NULL,
  SkillId        INT NULL,
  Text           NVARCHAR(MAX) NOT NULL,
  Severity       VARCHAR(8) NOT NULL DEFAULT 'info'
    CONSTRAINT CK_Reco_Severity CHECK (Severity IN ('info','minor','major')),
  TargetBandCode VARCHAR(20) NULL,
  IsResolved     BIT NOT NULL DEFAULT 0,
  CreatedAt      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- M-12: the skill must belong to this same certificate (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_Reco_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- M-12: the target band must be a real band of this same certificate (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_Reco_TargetBand FOREIGN KEY (CertId, TargetBandCode) REFERENCES ScoreBands(CertId, Code),
  -- UI-21: the attempt must be this user's own (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_Reco_AttemptUser FOREIGN KEY (AttemptId, UserId) REFERENCES ExamAttempts(AttemptId, UserId),
  -- UI-43: the session must be this user's own; a recommendation names at most one source
  CONSTRAINT FK_Reco_SessionUser FOREIGN KEY (PracticeSessionId, UserId) REFERENCES PracticeSessions(SessionId, UserId),
  CONSTRAINT CK_Reco_OneSource CHECK (AttemptId IS NULL OR PracticeSessionId IS NULL),
  -- ADR-0004: the insight must be this same learner's own, and a recommendation comes from rubric
  -- grading or from an aggregate insight, never from both
  CONSTRAINT FK_Reco_InsightUser FOREIGN KEY (InsightId, UserId) REFERENCES AiInsights(InsightId, UserId),
  CONSTRAINT CK_Reco_OneProducer CHECK (GradingId IS NULL OR InsightId IS NULL)
);
GO
-- ============ Domain 9: LearningPath ============
CREATE TABLE PlacementTests (
  PlacementId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId         INT NOT NULL REFERENCES Users(UserId),
  -- #309: NULL allows universal placement before certificate selection in OOBE Step 3.
  CertId         INT NULL REFERENCES Certificates(CertId),
  ExamId         INT NULL,
  Status         VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Placement_Status CHECK (Status IN ('in_progress','done','abandoned')),
  ResultBandCode VARCHAR(20) NULL,        -- snapshot
  -- UI-22: placement-take saves/resumes answers and placement-result shows L/R scores, time and
  -- question count. All of that is an ExamAttempt (free: no mock_exam_start charge) â€” linked here.
  AttemptId      INT NULL,
  ExamPurpose    VARCHAR(20) NOT NULL CONSTRAINT DF_Placement_ExamPurpose DEFAULT 'placement'
    CONSTRAINT CK_Placement_ExamPurpose CHECK (ExamPurpose = 'placement'),
  TotalScore     INT NULL,                -- snapshot of the attempt total ("385 out of 990")
  TakenAt        DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- #191 / D14: required by trg_PlacementTests_SingleWriter on any write of the snapshot. An
  -- in-progress placement derives nothing yet and declares neither.
  -- UI-46, replaced in #212: a finished placement names its result band and its total **where it has
  -- one**. `done â‡’ band AND total` was the constraint that turned the confirmed defect
  -- (docs/research/placement-free-response-defect.md) into a live crash: a placement carrying a free
  -- response had no band yet, so the row could not be written at all. The rule that replaces it is
  -- about measured SKILLS, not about a certificate band, and it lives in
  -- trg_PlacementTests_CompletionIsEarned below because it reads another table.
  --
  -- CompletionFailureReason is the honest second way to finish: PLC-36's "done â‡’ overall present, or a
  -- completion failure recorded". It is deliberately NOT a fourth PlacementStatus - the sitting is
  -- still `in_progress` while a productive task is in the AI queue, and that state is carried by the
  -- per-skill rows' `grading` status, which is the grain the screen needs.
  CompletionFailureReason VARCHAR(24) NULL
    CONSTRAINT CK_Placement_CompletionFailure
    CHECK (CompletionFailureReason IS NULL
           OR CompletionFailureReason IN ('candidate_skipped','inactivity_timeout','ai_grading_failed','no_cut_score_row')),
  -- #213 / story 52: the learner chose, at the preflight, to carry over the skills whose stored result
  -- is still inside D18's window rather than re-measure everything. The choice belongs to the sitting -
  -- the result writer reads it to decide which skills are carried - and it is NOT a stored result: the
  -- per-skill rows still say, per skill, whether this sitting measured it or reused it.
  ReusesPriorResults BIT NOT NULL CONSTRAINT DF_Placement_ReusesPriorResults DEFAULT 0,
  -- #289: learner's chosen scope ('all_skills' or 'listening_and_reading') and optional retake charge/idempotence links.
  Scope VARCHAR(24) NOT NULL CONSTRAINT DF_Placement_Scope DEFAULT 'all_skills'
    CONSTRAINT CK_Placement_Scope CHECK (Scope IN ('all_skills','listening_and_reading')),
  -- #309: normalized per-skill and overall diagnostic percentages for universal placement band mapping.
  ListeningPct     DECIMAL(5,2) NULL,
  ReadingPct       DECIMAL(5,2) NULL,
  SpeakingPct      DECIMAL(5,2) NULL,
  WritingPct       DECIMAL(5,2) NULL,
  OverallPct       DECIMAL(5,2) NULL,
  CreditTxnId INT NULL,
  IdempotencyKey VARCHAR(64) NULL,
  -- M-2: the placement exam must belong to the same certificate (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_Placement_ExamCert FOREIGN KEY (ExamId, CertId) REFERENCES Exams(ExamId, CertId),
  -- UI-3: and must be a placement exam, not a mock
  CONSTRAINT FK_Placement_ExamPurpose FOREIGN KEY (ExamId, CertId, ExamPurpose) REFERENCES Exams(ExamId, CertId, Purpose),
  -- UI-22: the attempt must be this user's own
  CONSTRAINT FK_Placement_AttemptUser FOREIGN KEY (AttemptId, UserId) REFERENCES ExamAttempts(AttemptId, UserId),
  -- UI-46 kept, narrowed: a finished placement still traces back to the attempt it was derived from.
  -- The band and total arms moved to trg_PlacementTests_CompletionIsEarned, because a partial result
  -- with a recorded failure legitimately has no band.
  CONSTRAINT CK_Placement_DoneHasAttempt CHECK (Status <> 'done' OR AttemptId IS NOT NULL),
  CONSTRAINT UQ_PlacementTests_Id_User UNIQUE (PlacementId, UserId),
  -- #212: the pair the per-skill rows hang off, so a row's CertId is pinned to its placement's and one
  -- certificate's skill cannot be recorded under another's (the isolation rule carried down every table).
  CONSTRAINT UQ_PlacementTests_Id_Cert UNIQUE (PlacementId, CertId)
);
GO
-- UI-46: the snapshot is a copy of the attempt's own result, not a free-standing number â€”
-- a placement-result page ("385 out of 990 Â· L 215 Â· R 170") must trace back to real scores.
CREATE TRIGGER trg_PlacementTests_SnapshotMatchesAttempt
ON PlacementTests AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN ExamAttempts a ON a.AttemptId = i.AttemptId
    WHERE i.TotalScore IS NOT NULL AND i.TotalScore <> a.TotalScore
  )
    THROW 50042, 'PlacementTests.TotalScore must be a snapshot of the attempt''s own TotalScore.', 1;
END;
GO
CREATE UNIQUE INDEX UX_PlacementTests_Attempt ON PlacementTests(AttemptId) WHERE AttemptId IS NOT NULL;
GO
CREATE UNIQUE INDEX UX_PlacementTests_CreditTxn ON PlacementTests(CreditTxnId) WHERE CreditTxnId IS NOT NULL;
GO
CREATE UNIQUE INDEX UX_PlacementTests_Idempotency ON PlacementTests(UserId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
GO
CREATE TABLE TargetBands (
  UserId         INT NOT NULL REFERENCES Users(UserId),
  CertId         INT NOT NULL REFERENCES Certificates(CertId),
  TargetBandCode VARCHAR(20) NOT NULL,
  SetAt          DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- #153: a goal with a deadline. Every column below is NULLable: the placement flow creates a row
  -- with only a band code, and a goal with no deadline legitimately has no feasibility answer.
  CurrentBandCode   VARCHAR(20) NULL,     -- snapshot of the band the learner starts from
  TargetDate        DATE NULL,            -- the exam date the learner is working towards
  WeeklyStudyHours  DECIMAL(4,1) NULL,    -- hours per week the learner can actually spend
  RequiredTotalHours INT NULL,            -- Î”H, computed by code; NULL when the bands carry no hours
  FeasibilityStatus VARCHAR(12) NULL,     -- computed by code, never by the LLM
  CONSTRAINT PK_TargetBands PRIMARY KEY (UserId, CertId),
  CONSTRAINT FK_TargetBands_Band FOREIGN KEY (CertId, TargetBandCode) REFERENCES ScoreBands(CertId, Code),
  -- #153: the starting band must be a real band of this same certificate (NULL stays legal)
  CONSTRAINT FK_TargetBands_CurrentBand FOREIGN KEY (CertId, CurrentBandCode) REFERENCES ScoreBands(CertId, Code),
  -- 25 h/week is the ceiling the spec puts on any study plan, declared or required
  CONSTRAINT CK_TargetBands_WeeklyHours CHECK (WeeklyStudyHours IS NULL OR (WeeklyStudyHours > 0 AND WeeklyStudyHours <= 25)),
  CONSTRAINT CK_TargetBands_RequiredHours CHECK (RequiredTotalHours IS NULL OR RequiredTotalHours > 0),
  CONSTRAINT CK_TargetBands_Feasibility CHECK (FeasibilityStatus IS NULL OR FeasibilityStatus IN ('feasible','stretch','unrealistic')),
  -- a deadline is a date plus a pace: half a goal cannot be judged
  CONSTRAINT CK_TargetBands_DeadlinePair CHECK ((TargetDate IS NULL AND WeeklyStudyHours IS NULL)
                                             OR (TargetDate IS NOT NULL AND WeeklyStudyHours IS NOT NULL)),
  -- no deadline means no answer, and an answer without a deadline would be a lie
  CONSTRAINT CK_TargetBands_AnswerNeedsDeadline CHECK (FeasibilityStatus IS NULL OR TargetDate IS NOT NULL)
);
GO
-- #232 / D10: PathTemplates, PathModules, PathModuleItems and PathModuleTopics are dropped, and with
-- them LearnerPaths.TemplateId. A step is a coordinate - (skill, topic, target level, step type, sizing
-- tier) - synthesised by LearningPathPlanner from current capability. A step that needs a template row
-- to say what it is cannot be re-planned, and re-planning is the whole point of the path.
--
-- Dropping the four tables removes the triple of composite keys that existed only to pin a step to a
-- template (UQ_PathTemplates_Id_Cert, UQ_PathModules_Id_Template, UQ_PMI_Id_Template, and
-- UQ_LearnerPaths_Id_Template below). #231 measured the wall this was blocked on: LearnerPathSteps
-- carried PathModuleItemId NOT NULL with a composite FK to PathModuleItems, so the step had to be
-- repointed at its own coordinate before the scaffolding could go. That repoint is this table.
CREATE TABLE LearnerPaths (
  LearnerPathId   INT IDENTITY(1,1) PRIMARY KEY,
  UserId          INT NOT NULL REFERENCES Users(UserId),
  CertId          INT NOT NULL REFERENCES Certificates(CertId),
  CurrentBandCode VARCHAR(20) NULL,
  -- UI-24: path-generated "Built from your placement result on 15 August 2026"
  PlacementId     INT NULL,
  Status          VARCHAR(10) NOT NULL DEFAULT 'active'
    CONSTRAINT CK_LearnerPaths_Status CHECK (Status IN ('active','completed','reset')),
  StartedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- #233 / D10: THE FOUR-WEEK WINDOW LIVES HERE.
  --
  -- WindowStartDate is the calendar day week `ActiveWeek` began. `WindowNumber` alone is not enough:
  -- week 1 next month is a different week, so every archived week is keyed by its own number (see
  -- LearnerPathWeeks below), and the live window is the span [ActiveWeek, ActiveWeek + 3].
  --
  -- #232's closing comment left these to #233: a path with no window has no close rule, so its weeks
  -- can neither slide nor archive.
  --
  -- The default keeps the demo seed and the diagnostic benchmark loadable: a row written by a seed or
  -- by hand starts its window on the day it was created, at week 1, and slides from there.
  WindowStartDate DATE NOT NULL
    CONSTRAINT DF_Paths_WindowStartDate DEFAULT CAST(SYSDATETIMEOFFSET() AS DATE),
  -- The first live week's number. It only ever moves forward, and only when a week closes.
  ActiveWeek      INT NOT NULL CONSTRAINT DF_Paths_ActiveWeek DEFAULT 1
    CONSTRAINT CK_Paths_ActiveWeek CHECK (ActiveWeek >= 1),
  -- #233: how many times the window has slid. The slide declares the synthesis writer; this column is
  -- what makes two slides at the same instant collide instead of synthesising two weeks.
  WindowSlides    INT NOT NULL CONSTRAINT DF_Paths_WindowSlides DEFAULT 0
    CONSTRAINT CK_Paths_WindowSlides CHECK (WindowSlides >= 0),
  -- composite target so CreditTransactions can prove the charged user owns the path (H-1):
  CONSTRAINT UQ_LearnerPaths_Id_User UNIQUE (LearnerPathId, UserId),
  -- #232: composite target so every step's carried-down CertId is this path's own, and the step's
  -- skill, topic and resource can be pinned to that same certificate.
  CONSTRAINT UQ_LearnerPaths_Id_Cert UNIQUE (LearnerPathId, CertId),
  -- UI-24: the placement must be this user's own
  CONSTRAINT FK_LearnerPaths_PlacementUser FOREIGN KEY (PlacementId, UserId) REFERENCES PlacementTests(PlacementId, UserId)
);
GO
-- ADR-0004: declared here because AiInsights (Domain 8) is created before LearnerPaths (Domain 9).
-- Composite so a path insight must be about this same learner's own path.
ALTER TABLE AiInsights ADD CONSTRAINT FK_AiInsights_PathUser
  FOREIGN KEY (LearnerPathId, UserId) REFERENCES LearnerPaths(LearnerPathId, UserId);
GO
-- Review Focus #5: at most one active path per (UserId,CertId)
CREATE UNIQUE INDEX UX_LearnerPaths_OneActive
  ON LearnerPaths(UserId, CertId) WHERE Status = 'active';
GO
-- #232 / D10: THE STEP IS A COORDINATE.
--
-- StepCoordinate = (SkillId, TopicId, TargetCefr, StepType, SizingTier). Those five columns say what a
-- step is; nothing outside them does. PathModuleItemId is gone, and with it the requirement that a
-- step belong to a module of a template - which is what makes a step re-plannable at all.
--
-- TargetCefr is the level of THIS skill, never the certificate's target band. Those are different
-- quantities (D1/D6): a band is a scored interval the certificate defines, a level is a position on
-- the CEFR ladder. Reading a band code as a level is the defect class this column exists to prevent.
--
-- The (SkillId, Modality) and (TopicId, Modality) composite keys are the idiom Sections and Items
-- already use: a listening step on a reading topic is a defect, and a comment cannot enforce it.
CREATE TABLE LearnerPathSteps (
  StepId          INT IDENTITY(1,1) PRIMARY KEY,
  LearnerPathId   INT NOT NULL,
  -- carried down from the owning path so every coordinate below is pinned to the path's certificate
  CertId          INT NOT NULL,
  -- the skill axis. CONTEXT.md: a Skill is exactly one modality, and Skills is certificate-scoped.
  SkillId         INT NOT NULL,
  Modality        VARCHAR(10) NOT NULL
    CONSTRAINT CK_Steps_Modality CHECK (Modality IN ('listening','reading','speaking','writing')),
  -- #183: the canonical twenty. No twenty-first topic exists, so a step cannot invent one.
  TopicId         INT NOT NULL,
  -- the level this skill is practised at, A1-C2. Never 'pre_a1': the floor is reportable, not
  -- authored, and BusinessObjects.CefrLevel.TryParseAuthored refuses it for the same reason.
  TargetCefr      VARCHAR(10) NOT NULL
    CONSTRAINT CK_Steps_TargetCefr CHECK (TargetCefr IN ('A1','A2','B1','B2','C1','C2')),
  -- D10's three execution paths, not a label:
  --   diagnostic_drill / focus_practice / review_spaced -> the unified item selector (#235)
  --   concept_lesson / expanded_remedial                -> the lesson reader
  --   mock_section                                      -> IAttemptEngine
  StepType        VARCHAR(20) NOT NULL
    CONSTRAINT CK_Steps_StepType CHECK (StepType IN
      ('diagnostic_drill','focus_practice','review_spaced',
       'concept_lesson','expanded_remedial','mock_section')),
  -- D10's sizing table holds exactly these three values and no others. A fourth tier would be a size
  -- nobody measured the minutes for.
  SizingTier      VARCHAR(12) NOT NULL
    CONSTRAINT CK_Steps_SizingTier CHECK (SizingTier IN ('mastered','developing','low')),
  -- What the step actually opens. A drill names no resource: the selector decides which items at run
  -- time, which is what lets a drill substitute a sibling topic when its own cell is cooling down.
  RefLessonId     INT NULL,
  RefExamId       INT NULL,
  Status          VARCHAR(12) NOT NULL DEFAULT 'locked'
    -- UI-25: learning-path shows Not started / In progress / Done / Locked
    CONSTRAINT CK_Steps_Status CHECK (Status IN ('locked','available','in_progress','done')),
  Score           DECIMAL(6,2) NULL,
  StartedAt       DATETIMEOFFSET NULL,
  CompletedAt     DATETIMEOFFSET NULL,
  -- #154: the planning week this step belongs to. Nullable because rows written by hand or by seed have
  -- no plan behind them; a generated step always carries both numbers.
  --
  -- #233 / D10: this was `BETWEEN 1 AND 4`, which is the fixed-block model the four-week window
  -- replaces. The window is *live weeks 1-4*, not *week numbers 1-4*: after the first slide the far
  -- end is week 5, then 6, and a CHECK capping it at 4 would refuse to synthesise any week after the
  -- first. The live span is enforced where it belongs - in PathWindow.IsLive, over the path's own
  -- ActiveWeek - because a week number is history and a live window is a position.
  WeekNumber       INT NULL CONSTRAINT CK_Steps_Week CHECK (WeekNumber IS NULL OR WeekNumber >= 1),
  -- #232 / D10: absolute ordering inside the week. Ordering is pedagogical (lesson -> blocked
  -- practice -> interleaved review, alternating skills across the timeline), so it is a field the
  -- planner sets, never the row's insertion order. #233 packs weeks to a budget; it orders by this.
  WeekSequence     INT NOT NULL CONSTRAINT DF_Steps_WeekSequence DEFAULT 0,
  -- #232: the size the learner is shown before starting ("8 items, ~12 min"). SizingTier is the band
  -- the planner chose; this is the count it actually resolved, so a short cell says so honestly
  -- instead of claiming a count it cannot fill. NULL for a lesson, which is sized in minutes alone.
  PlannedItemCount INT NULL CONSTRAINT CK_Steps_PlannedItemCount CHECK (PlannedItemCount IS NULL OR PlannedItemCount > 0),
  EstimatedMinutes INT NULL CONSTRAINT CK_Steps_EstMinutes CHECK (EstimatedMinutes IS NULL OR EstimatedMinutes > 0),
  -- #154: what "this week is on track" is measured against, per step
  TargetAccuracyPct INT NOT NULL CONSTRAINT DF_Steps_TargetAccuracy DEFAULT 70
    CONSTRAINT CK_Steps_TargetAccuracy CHECK (TargetAccuracyPct BETWEEN 0 AND 100),
  -- #154: measured, never guessed: correct/answered for a drill or a mock, NULL for a lesson
  ActualAccuracyPct DECIMAL(5,2) NULL
    CONSTRAINT CK_Steps_ActualAccuracy CHECK (ActualAccuracyPct IS NULL OR ActualAccuracyPct BETWEEN 0 AND 100),
  -- #191 / D14: required by trg_LearnerPathSteps_SingleWriter when the step is CLOSED. A step moving
  -- between open states (locked -> available -> in_progress) derives nothing and declares nothing.
  -- M-1: CertId must be the learner path's own
  CONSTRAINT FK_Steps_PathCert FOREIGN KEY (LearnerPathId, CertId) REFERENCES LearnerPaths(LearnerPathId, CertId),
  -- the skill must belong to that same certificate
  CONSTRAINT FK_Steps_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- and the modality must be the skill's own (a listening step cannot name a reading skill)
  CONSTRAINT FK_Steps_SkillModality FOREIGN KEY (SkillId, Modality) REFERENCES Skills(SkillId, Modality),
  -- a lesson must be a lesson of the same certificate (NULL stays legal, MATCH SIMPLE).
  -- Declared as an ALTER after Domain 9.5 creates Lessons, because LearnerPathSteps is created first.
  -- the topic must exist and carry that same modality
  CONSTRAINT FK_Steps_TopicModality FOREIGN KEY (TopicId, Modality) REFERENCES Topics(TopicId, Modality),
  -- a mock section must be an exam of the same certificate (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_Steps_ExamCert FOREIGN KEY (RefExamId, CertId) REFERENCES Exams(ExamId, CertId),
  -- Review Focus #3, restated for the coordinate: the step type and the resource must agree. A drill
  -- names neither, because the selector picks its items; a lesson names only its lesson; a mock
  -- section names only its exam. Without this, a concept_lesson with no lesson would open an empty
  -- reader and a mock_section with no exam would open nothing at all.
  CONSTRAINT CK_Steps_ResourceMatchesType CHECK (
       (StepType IN ('diagnostic_drill','focus_practice','review_spaced')
          AND RefLessonId IS NULL AND RefExamId IS NULL)
    OR (StepType IN ('concept_lesson','expanded_remedial')
          AND RefLessonId IS NOT NULL AND RefExamId IS NULL)
    OR (StepType = 'mock_section'
          AND RefExamId IS NOT NULL AND RefLessonId IS NULL)
  )
);
GO
-- UI-12: now that LearnerPathSteps exists, link a practice session to the module step it serves
ALTER TABLE PracticeSessions ADD CONSTRAINT FK_Practice_PathStep FOREIGN KEY (LearnerPathStepId) REFERENCES LearnerPathSteps(StepId);
GO
-- #191 / D14: step closure has exactly one writer, PathStepCompletion. Before #154 three callers
-- closed a step inline and the unlock rule drifted; #154 put them behind one class and this makes
-- the arrangement structural. `LearningPathService` also writes this row - it moves a step to
-- in_progress - which is why the derived fact is the CLOSURE (Status moving to done, plus the score
-- and the completion time) and not the row.
--
-- A closed step is also terminal: reopening it would let a second writer reach the same row through
-- the open-state door. PathStepCompletion is idempotent on an already-done step and the reset-path
-- case is handled by LearnerPaths.Status, not by reopening the step.
-- #309: trg_LearnerPathSteps_SingleWriter removed (Expand phase).
-- ============ Domain 9.5: Guided Learning (Lessons) ============
-- Issue #45: Interactive lesson walkthroughs for remediation and guided paths
CREATE TABLE Lessons (
  LessonId         INT IDENTITY(1,1) PRIMARY KEY,
  CertId           INT NOT NULL REFERENCES Certificates(CertId),
  SkillId          INT NOT NULL,
  CategoryId       INT NULL REFERENCES QuestionCategories(CategoryId),
  Title            NVARCHAR(160) NOT NULL,
  Summary          NVARCHAR(500) NULL,
  EstimatedMinutes INT NOT NULL DEFAULT 5 CONSTRAINT CK_Lessons_Est CHECK (EstimatedMinutes > 0),
  DisplayOrder     INT NOT NULL DEFAULT 0,
  -- #309: comma-separated target band codes (e.g. '5.5,6.0,6.5') or 'ALL' for general knowledge.
  TargetBands      VARCHAR(100) NOT NULL CONSTRAINT DF_Lessons_TargetBands DEFAULT 'ALL',
  CreatedAt        DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT FK_Lessons_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  CONSTRAINT UQ_Lessons_Id_Cert UNIQUE (LessonId, CertId)
);
GO
CREATE TABLE LessonCards (
  CardId          INT IDENTITY(1,1) PRIMARY KEY,
  LessonId        INT NOT NULL,
  CertId          INT NOT NULL,
  CardType        VARCHAR(20) NOT NULL
    CONSTRAINT CK_LessonCards_CardType CHECK (CardType IN ('rule', 'pitfall', 'example', 'micro_check')),
  Title           NVARCHAR(160) NOT NULL,
  ContentMarkdown NVARCHAR(MAX) NOT NULL,
  QuestionPrompt  NVARCHAR(MAX) NULL,
  OptionA         NVARCHAR(250) NULL,
  OptionB         NVARCHAR(250) NULL,
  OptionC         NVARCHAR(250) NULL,
  OptionD         NVARCHAR(250) NULL,
  CorrectOption   CHAR(1) NULL CONSTRAINT CK_LessonCards_Correct CHECK (CorrectOption IS NULL OR CorrectOption IN ('A','B','C','D')),
  Explanation     NVARCHAR(MAX) NULL,
  Rationale       NVARCHAR(MAX) NULL,
  DisplayOrder    INT NOT NULL DEFAULT 0,
  CONSTRAINT FK_LessonCards_LessonCert FOREIGN KEY (LessonId, CertId) REFERENCES Lessons(LessonId, CertId),
  CONSTRAINT UQ_LessonCards_Id_Cert UNIQUE (CardId, CertId)
);
GO
CREATE TABLE LearnerLessonProgress (
  ProgressId        INT IDENTITY(1,1) PRIMARY KEY,
  UserId            INT NOT NULL REFERENCES Users(UserId),
  LessonId          INT NOT NULL,
  CertId            INT NOT NULL,
  LastViewedCardId  INT NULL REFERENCES LessonCards(CardId),
  CardsCompleted    INT NOT NULL DEFAULT 0,
  TotalCards        INT NOT NULL DEFAULT 0,
  IsCompleted       BIT NOT NULL DEFAULT 0,
  FirstOpenedAt     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CompletedAt       DATETIMEOFFSET NULL,
  UpdatedAt         DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT FK_LLP_LessonCert FOREIGN KEY (LessonId, CertId) REFERENCES Lessons(LessonId, CertId),
  CONSTRAINT UQ_LLP_UserLesson UNIQUE (UserId, LessonId)
);
GO
-- #159 review: a micro-check is graded once per learner. Without this row every POST of the same card
-- moved mastery again, so one known answer posted repeatedly farmed a category to Mastered - the exact
-- "click until green" the drill half closed in #155.
CREATE TABLE LearnerLessonMicroChecks (
  UserId          INT NOT NULL REFERENCES Users(UserId),
  CardId          INT NOT NULL REFERENCES LessonCards(CardId),
  IsCorrect       BIT NOT NULL,
  AnsweredAt      DATETIMEOFFSET NULL,
  CONSTRAINT PK_LearnerLessonMicroChecks PRIMARY KEY (UserId, CardId)
);
GO
-- #232: Domain 9.5 creates Lessons after LearnerPathSteps, so the step's lesson reference is pinned
-- here instead. Certificate isolation uses the same (Id, CertId) composite Lessons already publishes
-- for LessonCards and LearnerLessonProgress.
ALTER TABLE LearnerPathSteps ADD CONSTRAINT FK_Steps_LessonCert
  FOREIGN KEY (RefLessonId, CertId) REFERENCES Lessons(LessonId, CertId);
GO
-- UI-44, moved onto the step by #232. The rule was trg_PMI_AiTaskRefsSkillPracticeExam on
-- PathModuleItems: a path activity may open a sittable paper, never a mock-that-is-really-a-placement
-- or a placement sitting, which must stay free. PathModuleItems is dropped, so the rule lands on the
-- one column that can still open a paper - a mock_section step's RefExamId.
--
-- A CHECK cannot read Exams.Purpose, so this stays a trigger. It refuses a mock_section pointing at
-- any purpose other than 'mock': a path step that opened a placement would make a free diagnostic
-- chargeable, and one that opened a skill-practice paper would bill a sitting the learner did not ask
-- for. Drills and lessons name no exam, which CK_Steps_ResourceMatchesType already guarantees.
CREATE TRIGGER trg_LearnerPathSteps_MockSectionIsSittableExam
ON LearnerPathSteps AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN Exams e ON e.ExamId = i.RefExamId
    WHERE i.StepType = 'mock_section'
      AND e.Purpose <> 'mock'
  )
    THROW 50044, 'A mock_section step may only open a mock exam. A placement or skill-practice paper is not a path mock section.', 1;
END;
GO
-- ============ Domain 10: Mentor ============
CREATE TABLE MentorProfiles (
  MentorId       INT IDENTITY(1,1) PRIMARY KEY,
  DisplayName    NVARCHAR(120) NOT NULL,
  Bio            NVARCHAR(MAX) NULL,
  CertId         INT NOT NULL REFERENCES Certificates(CertId),
  SkillId        INT NULL REFERENCES Skills(SkillId),
  MeetLink       NVARCHAR(400) NOT NULL CONSTRAINT DF_Mentor_MeetLink DEFAULT N'https://meet.google.com/new',
  -- UI-26 (gap 7): what the learner is charged; the mentor_booking ledger Delta must equal -Bookings.PriceCredits
  PriceCredits   INT NOT NULL CONSTRAINT DF_Mentor_PriceCredits DEFAULT 1
    CONSTRAINT CK_Mentor_PriceCredits CHECK (PriceCredits > 0),
  Headline       NVARCHAR(200) NULL,
  Expertise      NVARCHAR(400) NULL,
  SessionMinutes INT NOT NULL CONSTRAINT DF_Mentor_SessionMinutes DEFAULT 60
    CONSTRAINT CK_Mentor_SessionMinutes CHECK (SessionMinutes > 0),
  IsActive       BIT NOT NULL DEFAULT 1,
  -- M-14: a mentor's SkillId (if any) must belong to the mentor's own CertId
  CONSTRAINT FK_Mentor_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId)
);
GO
CREATE TABLE MentorWeeklySlots (
  WeeklySlotId INT IDENTITY(1,1) PRIMARY KEY,
  MentorId     INT NOT NULL REFERENCES MentorProfiles(MentorId),
  DayOfWeek    TINYINT NOT NULL CONSTRAINT CK_MentorWeeklySlots_DayOfWeek CHECK (DayOfWeek BETWEEN 1 AND 7),
  StartTime    TIME(0) NOT NULL,
  EndTime      TIME(0) NOT NULL,
  CONSTRAINT CK_MentorWeeklySlots_Range CHECK (EndTime > StartTime)
);
GO
CREATE TABLE MentorSlots (
  SlotId   INT IDENTITY(1,1) PRIMARY KEY,
  MentorId INT NOT NULL REFERENCES MentorProfiles(MentorId),
  StartAt  DATETIMEOFFSET NOT NULL,
  EndAt    DATETIMEOFFSET NOT NULL,
  Status   VARCHAR(6) NOT NULL DEFAULT 'open'
    CONSTRAINT CK_Slots_Status CHECK (Status IN ('open','booked','closed')),
  CONSTRAINT CK_Slots_Range CHECK (EndAt > StartAt),
  -- PartC-1: composite target so Bookings.SlotId must agree with the slot's owning mentor
  CONSTRAINT UQ_MentorSlots_Id_Mentor UNIQUE (SlotId, MentorId)
);
GO
CREATE TABLE Bookings (
  BookingId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId       INT NOT NULL REFERENCES Users(UserId),
  MentorId     INT NOT NULL REFERENCES MentorProfiles(MentorId),
  SlotId       INT NOT NULL REFERENCES MentorSlots(SlotId),
  PriceCredits INT NOT NULL CONSTRAINT DF_Bookings_PriceCredits DEFAULT 1
    CONSTRAINT CK_Bookings_PriceCredits CHECK (PriceCredits > 0),
  Status       VARCHAR(10) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_Bookings_Status CHECK (Status IN ('pending','confirmed','done','cancelled')),
  MeetLink     NVARCHAR(400) NULL,
  FocusArea    NVARCHAR(120) NULL,          -- UI-27: mentor-confirm "Focus area: Listening strategies"
  CancelledAt  DATETIMEOFFSET NULL,       -- UI-27: refund only if cancelled 24h+ before the slot
  CreatedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- composite target so CreditTransactions can prove the charged user owns the booking (H-1).
  -- Batch 2 (PartC-3) also needs this constraint; reused there, not recreated.
  CONSTRAINT UQ_Bookings_Id_User UNIQUE (BookingId, UserId),
  -- PartC-1: SlotId must belong to the same mentor as MentorId
  CONSTRAINT FK_Bookings_SlotMentor FOREIGN KEY (SlotId, MentorId) REFERENCES MentorSlots(SlotId, MentorId),
  CONSTRAINT CK_Bookings_CancelledAt CHECK (CancelledAt IS NULL OR Status = 'cancelled')
);
GO
-- Review Focus #5: one live booking per slot (cancelled frees it)
CREATE UNIQUE INDEX UX_Bookings_OnePerSlot
  ON Bookings(SlotId) WHERE Status <> 'cancelled';
GO
CREATE TABLE Reviews (
  ReviewId  INT IDENTITY(1,1) PRIMARY KEY,
  BookingId INT NOT NULL,
  UserId    INT NOT NULL REFERENCES Users(UserId),
  Rating    INT NOT NULL CONSTRAINT CK_Reviews_Rating CHECK (Rating BETWEEN 1 AND 5),
  Text      NVARCHAR(MAX) NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT UQ_Reviews_OnePerBooking UNIQUE (BookingId),
  -- PartC-3: a review must be written by the user who made the booking
  CONSTRAINT FK_Reviews_BookingUser FOREIGN KEY (BookingId, UserId) REFERENCES Bookings(BookingId, UserId)
);
GO
CREATE TABLE Complaints (
  ComplaintId          INT IDENTITY(1,1) PRIMARY KEY,
  -- UI-28: admin complaints list relates complaints to a booking, an exam attempt or an order.
  -- Exactly one subject is set; each composite FK proves the complainant owns it.
  BookingId            INT NULL,
  AttemptId            INT NULL,
  OrderId              INT NULL,
  UserId               INT NOT NULL REFERENCES Users(UserId),
  Type                 VARCHAR(16) NOT NULL
    CONSTRAINT CK_Complaints_Type CHECK (Type IN
      ('mentor_no_show','mentor_quality','grading_issue','technical_error','payment_issue')),
  Reason               NVARCHAR(MAX) NOT NULL,
  Status               VARCHAR(10) NOT NULL DEFAULT 'open'
    CONSTRAINT CK_Complaints_Status CHECK (Status IN ('open','reviewing','resolved')),
  Resolution           NVARCHAR(MAX) NULL,
  InternalNotes        NVARCHAR(MAX) NULL,   -- UI-28: "Not shown to the learner"
  ResolvedByUserId     INT NULL CONSTRAINT FK_Complaints_ResolvedBy REFERENCES Users(UserId),
  ResolvedAt           DATETIMEOFFSET NULL,
  ResolvedByGrantTxnId INT NULL,      -- FK added in Task 10; composite FK to CreditTransactions(TxnId,UserId) added in Batch 1
  CreatedAt            DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- M-16: a make-good grant can only be recorded once the complaint is resolved
  CONSTRAINT CK_Complaints_GrantRequiresResolved CHECK (ResolvedByGrantTxnId IS NULL OR Status='resolved'),
  -- PartC-4: a complaint must be filed by the user who made the booking
  CONSTRAINT FK_Complaints_BookingUser FOREIGN KEY (BookingId, UserId) REFERENCES Bookings(BookingId, UserId),
  CONSTRAINT FK_Complaints_AttemptUser FOREIGN KEY (AttemptId, UserId) REFERENCES ExamAttempts(AttemptId, UserId),
  -- FK_Complaints_OrderUser added once Orders exists
  CONSTRAINT CK_Complaints_OneSubject CHECK (
    (CASE WHEN BookingId IS NULL THEN 0 ELSE 1 END
   + CASE WHEN AttemptId IS NULL THEN 0 ELSE 1 END
   + CASE WHEN OrderId   IS NULL THEN 0 ELSE 1 END) = 1),
  CONSTRAINT CK_Complaints_TypeMatchesSubject CHECK (
       (Type IN ('mentor_no_show','mentor_quality') AND BookingId IS NOT NULL)
    OR (Type IN ('grading_issue','technical_error') AND AttemptId IS NOT NULL)
    OR (Type = 'payment_issue' AND OrderId IS NOT NULL)),
  CONSTRAINT CK_Complaints_ResolvedAt CHECK (ResolvedAt IS NULL OR Status = 'resolved')
);
GO
-- M-15: reject bookings placed on a closed slot, and keep MentorSlots.Status in sync
-- with live (non-cancelled) bookings. Booking-side filtered unique index is the double-book
-- guard (kept as the accepted deviation from the spec's slot-side wording); this trigger only
-- closes the remaining `closed`-slot hole and makes Status non-decorative.
-- R6: DELETE is in the event list because a deleted booking must free its slot; without it the
-- slot stays 'booked' forever and no one can ever book that time again.
CREATE TRIGGER trg_Bookings_SlotStatusGuardAndSync
ON Bookings AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN MentorSlots ms ON ms.SlotId = i.SlotId
    WHERE i.Status <> 'cancelled' AND ms.Status = 'closed'
  )
    THROW 50024, 'Booking cannot be placed on a closed slot.', 1;

  ;WITH affected AS (
    SELECT SlotId FROM inserted
    UNION SELECT SlotId FROM deleted
  )
  UPDATE ms
    SET Status = CASE WHEN EXISTS (
        SELECT 1 FROM Bookings b WHERE b.SlotId = ms.SlotId AND b.Status <> 'cancelled'
      ) THEN 'booked' ELSE 'open' END
  FROM MentorSlots ms
  JOIN affected a ON a.SlotId = ms.SlotId
  WHERE ms.Status <> 'closed';
END;
GO
-- M-13: spec-mandated mentor rating aggregate (never stored, always derived)
CREATE VIEW vw_MentorAvgRating AS
  SELECT b.MentorId, AVG(CAST(r.Rating AS DECIMAL(3,2))) AS AvgRating, COUNT(*) AS ReviewCount
  FROM Reviews r JOIN Bookings b ON b.BookingId = r.BookingId
  GROUP BY b.MentorId;
GO
-- ============ Domain 5: Payment ============
CREATE TABLE Packages (
  PackageId INT IDENTITY(1,1) PRIMARY KEY,
  Name      NVARCHAR(120) NOT NULL,
  Credits   INT NOT NULL CONSTRAINT CK_Packages_Credits CHECK (Credits > 0),
  Price     DECIMAL(12,2) NOT NULL CONSTRAINT CK_Packages_Price CHECK (Price >= 0),
  IsActive  BIT NOT NULL DEFAULT 1,
  IsFeatured   BIT NOT NULL CONSTRAINT DF_Packages_IsFeatured DEFAULT 0,  -- UI-29: "Featured" / "Popular"
  Features     NVARCHAR(1000) NULL,  -- UI-29: pricing card bullet list, one per line
  DisplayOrder INT NOT NULL CONSTRAINT DF_Packages_DisplayOrder DEFAULT 0
);
GO
-- UI-29: "1 featured" on the packages page; pricing highlights a single card
CREATE UNIQUE INDEX UX_Packages_OneFeatured ON Packages(IsFeatured) WHERE IsFeatured = 1;
GO
-- ADR-0003: numeric merchant order number sent to the gateway. A SEQUENCE (not IDENTITY) because the
-- value is needed to build the checkout request before the Orders row is written, and because every
-- gateway must see a number it has never seen before â€” including across retries of a failed insert.
CREATE SEQUENCE SEQ_PaymentNo AS BIGINT START WITH 1 INCREMENT BY 1 NO CYCLE;
GO
CREATE TABLE Orders (
  OrderId   INT IDENTITY(1,1) PRIMARY KEY,
  UserId    INT NOT NULL REFERENCES Users(UserId),
  PackageId INT NOT NULL REFERENCES Packages(PackageId),
  Status    VARCHAR(12) NOT NULL DEFAULT 'pending'
    -- UI-30: 'reconciling' = gateway has not returned a final result ("Reconciling transaction")
    CONSTRAINT CK_Orders_Status CHECK (Status IN ('pending','reconciling','paid','failed','expired')),
  Amount    DECIMAL(12,2) NOT NULL CONSTRAINT CK_Orders_Amount CHECK (Amount >= 0),
  -- UI-30: order code "DH-20260920-0148" is what the learner sees and quotes in a complaint
  OrderCode    VARCHAR(20) NOT NULL,
  -- ADR-0003: gateway-agnostic numeric merchant order number (payOS requires a number, and so do
  -- most gateways). Drawn from SEQ_PaymentNo, never reused, unique across every gateway.
  PaymentNo    BIGINT NOT NULL,
  -- ADR-0003: 'payos' is the real gateway; 'mock' moves no money (demos, tests, offline grading).
  -- Adding vnpay/momo later means a new value here plus a new IPaymentGateway implementation.
  Gateway      VARCHAR(8) NOT NULL CONSTRAINT CK_Orders_Gateway CHECK (Gateway IN ('payos','mock')),
  -- payOS paymentLinkId (its own id for the checkout link)
  GatewayTxnRef  VARCHAR(64) NULL,
  GatewayErrorCode VARCHAR(16) NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  PaidAt    DATETIMEOFFSET NULL,
  CONSTRAINT CK_Orders_PaidAtRequiresPaid CHECK (PaidAt IS NULL OR Status='paid'),
  -- composite target so CreditTransactions can prove the charged user owns the order (H-1):
  CONSTRAINT UQ_Orders_Id_User UNIQUE (OrderId, UserId),
  CONSTRAINT UQ_Orders_OrderCode UNIQUE (OrderCode),
  CONSTRAINT UQ_Orders_PaymentNo UNIQUE (PaymentNo)
);
GO
-- Review Focus #5: one pending order per user
-- UI-30: a reconciling order also blocks a new one ("do not pay again")
CREATE UNIQUE INDEX UX_Orders_OnePendingPerUser
  ON Orders(UserId) WHERE Status IN ('pending','reconciling');
GO
CREATE UNIQUE INDEX UX_Orders_GatewayTxnRef ON Orders(Gateway, GatewayTxnRef) WHERE GatewayTxnRef IS NOT NULL;
GO
-- ADR-0003: every message about an order from or to its gateway, kept as evidence. Credit is granted
-- only on a signature-verified webhook or a server-side status query â€” never on the return redirect,
-- which anyone can type â€” so which source said "paid" has to be auditable after the fact.
CREATE TABLE PaymentEvents (
  EventId    INT IDENTITY(1,1) PRIMARY KEY,
  OrderId    INT NOT NULL REFERENCES Orders(OrderId),
  Source     VARCHAR(16) NOT NULL
    -- webhook: gateway called us. status_query: we asked the gateway. return_redirect: the browser
    -- came back (recorded, never trusted). cancel: we told the gateway to void the link.
    CONSTRAINT CK_PaymentEvents_Source CHECK (Source IN ('webhook','status_query','return_redirect','cancel')),
  -- the order status this event asserted, as the gateway reported it; NULL when the call itself failed
  ReportedStatus VARCHAR(12) NULL
    CONSTRAINT CK_PaymentEvents_ReportedStatus CHECK (ReportedStatus IN ('pending','paid','failed','expired','cancelled')),
  -- true only for a webhook whose signature verified; always 0 for other sources
  SignatureValid BIT NOT NULL CONSTRAINT DF_PaymentEvents_SignatureValid DEFAULT 0,
  -- whether this event is the one that moved the order (a repeat webhook is logged but changes nothing)
  WasApplied BIT NOT NULL CONSTRAINT DF_PaymentEvents_WasApplied DEFAULT 0,
  Payload    NVARCHAR(MAX) NULL,   -- raw body or response, for reconciliation
  Note       NVARCHAR(400) NULL,
  CreatedAt  DATETIMEOFFSET NOT NULL CONSTRAINT DF_PaymentEvents_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT CK_PaymentEvents_SigOnlyWebhook CHECK (SignatureValid = 0 OR Source = 'webhook'),
  -- only a trusted source may be the event that granted credit
  CONSTRAINT CK_PaymentEvents_AppliedTrusted CHECK (
    WasApplied = 0
    OR (Source = 'status_query')
    OR (Source = 'webhook' AND SignatureValid = 1)
    OR (Source = 'cancel')
  )
);
GO
CREATE INDEX IX_PaymentEvents_Order ON PaymentEvents(OrderId, CreatedAt DESC);
GO
-- UI-28: complaint about an order must be the complainant's own order
ALTER TABLE Complaints ADD CONSTRAINT FK_Complaints_OrderUser FOREIGN KEY (OrderId, UserId) REFERENCES Orders(OrderId, UserId);
GO
CREATE TABLE CreditTransactions (
  TxnId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId   INT NOT NULL REFERENCES Users(UserId),
  Reason   VARCHAR(16) NOT NULL
    CONSTRAINT CK_CreditTxn_Reason CHECK (Reason IN
      ('purchase','admin_grant','mock_exam_start','ai_grading','mentor_booking','admin_revoke',
       'booking_cancel',   -- UI-31 (gap 8): refund of a cancelled booking
       'refund_credit',    -- #280: the credits a credit refund granted
       'placement_retake', -- #288: paid placement retake charge (16 chars, fits VARCHAR(16))
       'learning_path')),  -- #309: 5-credit 4-week learning path generation charge
  Delta    INT NOT NULL CONSTRAINT CK_CreditTxn_NonZero CHECK (Delta <> 0),
  OrderId       INT NULL,
  AttemptId     INT NULL,
  BookingId     INT NULL,
  -- UI-32: admin users "Adjust credits": reason is required and logged with the admin's name
  AdminUserId   INT NULL CONSTRAINT FK_CreditTxn_Admin REFERENCES Users(UserId),
  Note          NVARCHAR(400) NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT CK_CreditTxn_AdminActor CHECK (
       (Reason IN ('admin_grant','admin_revoke','refund_credit') AND AdminUserId IS NOT NULL AND Note IS NOT NULL)
    OR (Reason = 'placement_retake' AND AdminUserId IS NULL AND Note IS NOT NULL)
    OR (Reason NOT IN ('admin_grant','admin_revoke','refund_credit','placement_retake') AND AdminUserId IS NULL)),
  -- M-16: lets Complaints prove the linked grant belongs to the complainant
  CONSTRAINT UQ_CreditTxn_Id_User UNIQUE (TxnId, UserId),
  -- H-1: composite FKs carry UserId so the source row's owner must match the charged wallet.
  -- MATCH SIMPLE (default) skips the FK check when any column is NULL, so admin_grant/admin_revoke
  -- rows (all three source columns NULL) remain legal with no extra guards.
  --
  -- #246: FK_CreditTxn_Path is gone with the premium-path feature. The learner path it pointed at is
  -- dropped in the rebuild, and the user's decision was to wipe rather than carry a tombstone id:
  -- the project has no production data to preserve. A ledger row that charged for a feature that no
  -- longer exists has nothing left to point at, and inventing a reference would be the mapping D23
  -- forbids.
  CONSTRAINT FK_CreditTxn_Order   FOREIGN KEY (OrderId, UserId)       REFERENCES Orders(OrderId, UserId),
  CONSTRAINT FK_CreditTxn_Attempt FOREIGN KEY (AttemptId, UserId)     REFERENCES ExamAttempts(AttemptId, UserId),
  CONSTRAINT FK_CreditTxn_Booking FOREIGN KEY (BookingId, UserId)     REFERENCES Bookings(BookingId, UserId),
  -- sign per reason
  -- #280: refund_credit is positive - a refund gives value back, it never takes it. The negative
  -- variant (clawing back a refund) would be a separate reason with its own evidence, not this one
  -- with a flipped sign.
  CONSTRAINT CK_CreditTxn_DeltaSign CHECK (
       (Reason IN ('purchase','admin_grant','booking_cancel','refund_credit') AND Delta > 0)
    OR (Reason IN ('mock_exam_start','ai_grading','mentor_booking','admin_revoke','placement_retake','learning_path') AND Delta < 0)
  ),
  -- exactly the source column matching the reason is set (admin_* carry none)
  -- #280: refund_credit carries NO source column either, for a reason the ledger cannot express: the
  -- order that was refunded is already claimed by the 'purchase' row under UX_CreditTxn_Order (unique
  -- on OrderId), so a refund credit pointing at the order would be refused as a second row for one
  -- order. The order link therefore lives on Refunds.OrderId, one table over.
  CONSTRAINT CK_CreditTxn_Source CHECK (
       (Reason='purchase'        AND OrderId IS NOT NULL AND AttemptId IS NULL AND BookingId IS NULL)
    OR (Reason='mock_exam_start' AND AttemptId IS NOT NULL AND OrderId IS NULL AND BookingId IS NULL)
    OR (Reason='ai_grading'      AND AttemptId IS NOT NULL AND OrderId IS NULL AND BookingId IS NULL)
    OR (Reason IN ('mentor_booking','booking_cancel')
                                 AND BookingId IS NOT NULL AND OrderId IS NULL AND AttemptId IS NULL)
    OR (Reason IN ('admin_grant','admin_revoke','refund_credit','placement_retake','learning_path') AND OrderId IS NULL AND AttemptId IS NULL AND BookingId IS NULL)
  )
);
GO
ALTER TABLE PlacementTests ADD CONSTRAINT FK_Placement_CreditTxn
  FOREIGN KEY (CreditTxnId, UserId) REFERENCES CreditTransactions(TxnId, UserId);
GO
CREATE VIEW vw_UserCreditBalance AS
  SELECT UserId, SUM(Delta) AS Balance
  FROM CreditTransactions GROUP BY UserId;
GO
-- #162: the vw_LearnerPathIsPremium view is gone with the premium-path feature. #246 removed the
-- 'premium_path' reason code and the LearnerPathId column as well: the learner path is dropped in the
-- rebuild, so a ledger row pointing at it has nothing to point at. The user's decision was to wipe
-- rather than carry a tombstone id, because there is no production ledger to preserve.
-- Review Focus #4 / C-1: no debit may drive the running balance below zero.
-- Must also cover DELETE so a removed credit row can't leave a wallet negative unnoticed.
CREATE TRIGGER trg_CreditTransactions_NoNegativeBalance
ON CreditTransactions AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM (
      SELECT DISTINCT UserId FROM inserted
      UNION SELECT DISTINCT UserId FROM deleted
    ) u
    WHERE (SELECT SUM(Delta) FROM CreditTransactions t WHERE t.UserId=u.UserId) < 0
  )
    THROW 50020, 'Transaction would drive wallet balance negative.', 1;
END;
GO
-- C-1: the ledger is append-only. No row may ever be deleted.
CREATE TRIGGER trg_CreditTransactions_AppendOnly
ON CreditTransactions AFTER DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted)
    THROW 50021, 'CreditTransactions is append-only; rows cannot be deleted.', 1;
END;
GO
-- R2b: append-only means IMMUTABLE, not merely undeletable. Without this, a settled
-- purchase could be rewritten into an admin_grant with OrderId=NULL â€” severing it from its
-- order, which frees UX_CreditTxn_Order to credit that same order a second time.
CREATE TRIGGER trg_CreditTransactions_NoRewrite
ON CreditTransactions AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN deleted d ON d.TxnId = i.TxnId
    WHERE i.UserId <> d.UserId
       OR i.Reason <> d.Reason
       OR i.Delta  <> d.Delta
       -- source columns are nullable: compare with a sentinel so NULL->value is caught too
       OR ISNULL(i.OrderId,-1)       <> ISNULL(d.OrderId,-1)
       OR ISNULL(i.AttemptId,-1)     <> ISNULL(d.AttemptId,-1)
       OR ISNULL(i.BookingId,-1)     <> ISNULL(d.BookingId,-1)
  )
    THROW 50028, 'CreditTransactions is append-only; a posted transaction cannot be rewritten.', 1;
END;
GO
-- H-2: OrderId funds at most one txn; AttemptId allows one
-- mock_exam_start AND one ai_grading charge, but only one of each (spec: one charge covers all answers).
-- UI-31: a booking likewise carries one mentor_booking charge and at most one booking_cancel refund.
-- #246: UX_CreditTxn_Path went with the LearnerPathId column.
CREATE UNIQUE INDEX UX_CreditTxn_Order ON CreditTransactions(OrderId) WHERE OrderId IS NOT NULL;
GO
CREATE UNIQUE INDEX UX_CreditTxn_Booking ON CreditTransactions(BookingId, Reason) WHERE BookingId IS NOT NULL;
GO
CREATE UNIQUE INDEX UX_CreditTxn_Attempt_Reason ON CreditTransactions(AttemptId, Reason) WHERE AttemptId IS NOT NULL;
GO
-- H-2: a 'purchase' txn must reference a paid order and its Delta must equal that order's package credits.
CREATE TRIGGER trg_CreditTransactions_PurchaseMatchesOrder
ON CreditTransactions AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN Orders o ON o.OrderId = i.OrderId
    JOIN Packages pk ON pk.PackageId = o.PackageId
    WHERE i.Reason = 'purchase'
      AND (o.Status <> 'paid' OR i.Delta <> pk.Credits)
  )
    THROW 50022, 'purchase txn must reference a paid order with Delta equal to the package credits.', 1;
END;
GO
-- UI-37: what an attempt may be charged depends on its exam's purpose:
-- mock -> mock_exam_start and ai_grading; speaking/writing practice -> ai_grading only (UI-38);
-- placement -> nothing (free).
CREATE TRIGGER trg_CreditTransactions_ChargeMatchesExamPurpose
ON CreditTransactions AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN ExamAttempts a ON a.AttemptId = i.AttemptId
    JOIN Exams e        ON e.ExamId = a.ExamId
    WHERE (i.Reason = 'mock_exam_start' AND e.Purpose <> 'mock')
       OR (i.Reason = 'ai_grading'      AND e.Purpose = 'placement')
  )
    THROW 50038, 'This charge does not apply to the attempt''s exam purpose.', 1;
END;
GO
-- UI-26/UI-31: a mentor_booking charge must equal the booking's PriceCredits; a booking_cancel refund
-- must exactly reverse a prior charge on a booking that is now cancelled.
CREATE TRIGGER trg_CreditTransactions_BookingAmounts
ON CreditTransactions AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN Bookings b ON b.BookingId = i.BookingId
    WHERE i.Reason = 'mentor_booking' AND i.Delta <> -b.PriceCredits
  )
    THROW 50035, 'mentor_booking Delta must equal minus the booking''s PriceCredits.', 1;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN Bookings b ON b.BookingId = i.BookingId
    WHERE i.Reason = 'booking_cancel'
      AND (b.Status <> 'cancelled'
           OR NOT EXISTS (SELECT 1 FROM CreditTransactions c
                          WHERE c.BookingId = i.BookingId AND c.Reason = 'mentor_booking' AND c.Delta = -i.Delta))
  )
    THROW 50036, 'booking_cancel must refund exactly the charge of a cancelled booking.', 1;
END;
GO
-- R3: the above proves the order was 'paid' AT THE MOMENT OF CREDITING, but nothing stopped the
-- order being reverted to 'pending'/'expired' afterwards, leaving a credit with no paid order
-- behind it. A credited order is settled and may no longer leave 'paid'.
CREATE TRIGGER trg_Orders_CreditedStaysPaid
ON Orders AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN deleted d ON d.OrderId = i.OrderId
    WHERE d.Status = 'paid' AND i.Status <> 'paid'
      AND EXISTS (SELECT 1 FROM CreditTransactions ct
                  WHERE ct.OrderId = i.OrderId AND ct.Reason = 'purchase')
  )
    THROW 50029, 'An order that has already been credited cannot leave the paid status.', 1;
END;
GO
-- backfill the complaint make-good FK now that CreditTransactions exists.
-- M-16: composite FK carries UserId so the grant must belong to the complainant.
ALTER TABLE Complaints
  ADD CONSTRAINT FK_Complaints_GrantTxn FOREIGN KEY (ResolvedByGrantTxnId, UserId) REFERENCES CreditTransactions(TxnId, UserId);
GO
-- M-16: the linked grant must actually be an admin_grant txn (a CHECK cannot read another table).
CREATE TRIGGER trg_Complaints_GrantMustBeAdminGrant
ON Complaints AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN CreditTransactions t ON t.TxnId = i.ResolvedByGrantTxnId
    WHERE i.ResolvedByGrantTxnId IS NOT NULL
      AND t.Reason <> 'admin_grant'
  )
    THROW 50023, 'ResolvedByGrantTxnId must reference an admin_grant transaction.', 1;
END;
GO
-- #280: a refund is a first-class record with two methods. It exists here, after CreditTransactions,
-- because a credit refund names the ledger row it posted (FK_Refunds_CreditTxn) and only that table
-- carries the (TxnId, UserId) target.
--
-- The complaint half needs a composite target of its own first: Complaints had no (ComplaintId, UserId)
-- unique key because nothing until now pointed at a complaint and proved its owner in one FK.
ALTER TABLE Complaints ADD CONSTRAINT UQ_Complaints_Id_User UNIQUE (ComplaintId, UserId);
GO
--
-- Method and amount are ONE decision enforced by the schema, not two the service keeps in step: the
-- method names which column the value lives in, and CK_Refunds_MethodOwnsAmount refuses both a money
-- refund with no amount and a credit refund that also carries one. A service that forgot the second
-- half would leave a row saying "credit" next to a cash figure, which is the shape accounting cannot
-- read and nobody can repair later.
CREATE TABLE Refunds (
  RefundId   INT IDENTITY(1,1) PRIMARY KEY,
  UserId     INT NOT NULL REFERENCES Users(UserId),
  -- optional: a refund can compensate a failure that produced no complaint (spec AC 4)
  ComplaintId INT NULL,
  -- optional: a refund with no order behind it is capped by a configured ceiling at the service
  -- boundary, so NULL here is legal and not an omission.
  OrderId    INT NULL,
  Method     VARCHAR(6) NOT NULL
    CONSTRAINT CK_Refunds_Method CHECK (Method IN ('money','credit')),
  Reason     NVARCHAR(400) NOT NULL
    CONSTRAINT CK_Refunds_ReasonNotEmpty CHECK (LEN(LTRIM(RTRIM(Reason))) > 0),
  Status     VARCHAR(10) NOT NULL DEFAULT 'requested'
    CONSTRAINT CK_Refunds_Status CHECK (Status IN ('requested','approved','settled','rejected')),
  -- DECIMAL(12,2) is Orders.Amount's exact type on purpose: net revenue subtracts one from the other,
  -- and a narrower or differently-scaled column would round in the middle of a money report.
  Amount     DECIMAL(12,2) NULL,
  CreditCount INT NULL,
  RequestedByUserId INT NOT NULL CONSTRAINT FK_Refunds_RequestedBy REFERENCES Users(UserId),
  DecidedByUserId   INT NULL CONSTRAINT FK_Refunds_DecidedBy   REFERENCES Users(UserId),
  DecidedAt  DATETIMEOFFSET NULL,
  SettledByUserId   INT NULL CONSTRAINT FK_Refunds_SettledBy   REFERENCES Users(UserId),
  SettledAt  DATETIMEOFFSET NULL,
  -- the ledger row a credit refund posted (spec: also the evidence the credits were really granted)
  CreditTxnId INT NULL,
  CreatedAt  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- the method OWNS the amount: exactly one of the two value columns is set, and it is the one the
  -- method names. Written as two arms rather than an XOR so a credit refund carrying money is refused
  -- by its own reason instead of looking like a mistyped money refund.
  CONSTRAINT CK_Refunds_MethodOwnsAmount CHECK (
       (Method='money'  AND Amount IS NOT NULL AND Amount > 0 AND CreditCount IS NULL)
    OR (Method='credit' AND CreditCount IS NOT NULL AND CreditCount > 0 AND Amount IS NULL)),
  -- a credit refund ends at 'approved': the ledger, not a bank, is where the value lands, so there is
  -- nothing left for an accountant to settle.
  CONSTRAINT CK_Refunds_SettledIsMoney CHECK (Status <> 'settled' OR Method='money'),
  -- a status that RECORDS A DECISION cannot be reached without the decision: both the actor and the
  -- timestamp, because "who" without "when" cannot be reported on and "when" without "who" is not an
  -- audit trail. 'requested' is the only status with no decision behind it.
  CONSTRAINT CK_Refunds_DecisionStamped CHECK (
       Status='requested'
    OR (DecidedByUserId IS NOT NULL AND DecidedAt IS NOT NULL)),
  -- the settle stamps belong to 'settled' and to nothing else, and a money refund marked settled must
  -- carry them. Same reason as the decision arm, on the second transition.
  CONSTRAINT CK_Refunds_SettleStamped CHECK (
       (Status='settled' AND SettledByUserId IS NOT NULL AND SettledAt IS NOT NULL)
    OR (Status<>'settled' AND SettledByUserId IS NULL AND SettledAt IS NULL)),
  -- the ledger row a credit refund posted is a fact about a CREDIT refund only
  CONSTRAINT CK_Refunds_CreditTxnIsCredit CHECK (CreditTxnId IS NULL OR Method='credit'),
  -- composite targets: each FK proves the referenced row belongs to the refunded learner, the same
  -- H-1 pattern CreditTransactions uses for its three source columns.
  CONSTRAINT UQ_Refunds_Id_User UNIQUE (RefundId, UserId),
  CONSTRAINT FK_Refunds_OrderUser FOREIGN KEY (OrderId, UserId) REFERENCES Orders(OrderId, UserId),
  CONSTRAINT FK_Refunds_ComplaintUser FOREIGN KEY (ComplaintId, UserId) REFERENCES Complaints(ComplaintId, UserId),
  CONSTRAINT FK_Refunds_CreditTxn FOREIGN KEY (CreditTxnId, UserId) REFERENCES CreditTransactions(TxnId, UserId)
);
GO
-- the queue an accountant works from, and the range its CSV is exported over
CREATE INDEX IX_Refunds_Status ON Refunds(Status, CreatedAt);
CREATE INDEX IX_Refunds_User   ON Refunds(UserId);
CREATE INDEX IX_Refunds_Order  ON Refunds(OrderId) WHERE OrderId IS NOT NULL;
GO
-- #280: no delete path. A rejected refund stays a rejected refund (spec: "append-and-transition only"),
-- and a deleted refund would silently move the month's net revenue after accounting had exported it.
CREATE TRIGGER trg_Refunds_AppendOnly
ON Refunds AFTER DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted)
    THROW 50041, 'Refunds is append-and-transition only; a refund cannot be deleted.', 1;
END;
GO
-- #280: the row a credit refund posts is a 'refund_credit' grant, not an arbitrary ledger row. A CHECK
-- cannot read another table, so it is a trigger - the same reason trg_Complaints_GrantMustBeAdminGrant
-- exists beside FK_Complaints_GrantTxn.
CREATE TRIGGER trg_Refunds_CreditTxnIsRefundCredit
ON Refunds AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN CreditTransactions t ON t.TxnId = i.CreditTxnId
    WHERE i.CreditTxnId IS NOT NULL
      AND (t.Reason <> 'refund_credit' OR t.Delta <> i.CreditCount)
  )
    THROW 50042, 'A credit refund must point at the refund_credit ledger row that granted exactly its CreditCount.', 1;
END;
GO
-- expire stale pending orders (carried from old schema; frees UX_Orders_OnePendingPerUser)
CREATE PROCEDURE usp_ExpireStalePendingOrders @OlderThanMinutes INT = 15 AS
BEGIN
  SET NOCOUNT ON;
  UPDATE Orders SET Status='expired'
  WHERE Status='pending'
    AND CreatedAt < DATEADD(minute, -@OlderThanMinutes, SYSDATETIMEOFFSET());
END;
GO
-- ============ Views ============
-- UI-33: wallet "15 purchased âˆ’ 7 used", pricing "7 of 15 used", "Total purchased 548,000 â‚«"
CREATE VIEW vw_UserCreditSummary AS
  SELECT UserId,
         SUM(Delta) AS Balance,
         SUM(CASE WHEN Delta > 0 THEN Delta ELSE 0 END)  AS CreditsIn,
         SUM(CASE WHEN Reason = 'purchase' THEN Delta ELSE 0 END) AS CreditsPurchased,
         -SUM(CASE WHEN Delta < 0 AND Reason <> 'admin_revoke' THEN Delta ELSE 0 END)
           - SUM(CASE WHEN Reason = 'booking_cancel' THEN Delta ELSE 0 END) AS CreditsUsed
  FROM CreditTransactions GROUP BY UserId;
GO
-- UI-33: wallet stat "Total purchased 548,000 â‚« Â· 2 paid orders" â€” money, not credits
CREATE VIEW vw_UserPurchaseTotals AS
  SELECT o.UserId,
         SUM(o.Amount)    AS AmountPurchased,
         COUNT(*)         AS PaidOrderCount
  FROM Orders o
  WHERE o.Status = 'paid'
  GROUP BY o.UserId;
GO
-- UI-34: weakness analysis combines exams AND practice ("45/78 questions" across both)
--
-- #269: only ATTEMPT answers contribute now. A pool PRACTICE answer has neither a section nor a paper to
-- read one from - a free drill is scoped by its cell (TopicId + CefrLevel), which is where
-- vw_UserTopicAccuracy reports it. That was already #187's stated boundary; the legacy practice-answer
-- arm that read PracticeAnswers.SectionId went with that column, and the attempt arm is the sole branch.
--
-- Column names are unchanged on purpose: WeaknessAnalysisService, AiInsightService.Dashboard (L75,
-- L198) and AiInsightService.Paths/Exams read UserId, SectionId, Answered and Correct by name.
CREATE VIEW vw_UserSectionAccuracyAll AS
  SELECT a.UserId, ei.SectionId, COUNT(aa.IsCorrect) AS Answered,
         SUM(CASE WHEN aa.IsCorrect = 1 THEN 1 ELSE 0 END) AS Correct
  FROM AttemptAnswers aa
  JOIN ExamAttempts a ON a.AttemptId = aa.AttemptId
  JOIN ExamItems   ei ON ei.ExamItemId = aa.ExamItemId
  WHERE a.Status = 'graded'
  GROUP BY a.UserId, ei.SectionId;
GO
-- #243 / D3: accuracy on the two axes the rest of the product uses, (TopicId, CefrLevel).
--
-- D3: "Every reader of CategoryId moves to TopicId", and "vw_UserCategoryAccuracy is replaced by
-- vw_UserTopicAccuracy, grouping by (TopicId, CefrLevel)". This is that view.
--
-- WHERE A CELL COMES FROM. Only the pool carries a topic: Items.TopicId and Items.CefrLevel. The
-- legacy Questions row has neither (it has CategoryId, which is the bucket D3 retires), so a legacy
-- answer has no cell and contributes no row here. That is not an omission to be papered over with a
-- category-to-topic guess - the vocabulary is a closed set of twenty and no mapping exists.
--
-- WHY THE CELL IS THE ITEM'S, NOT THE SESSION'S. #237 settled this for mastery: a drill may serve a
-- substituted sibling-topic item, and crediting the session's cell would report accuracy for a topic
-- the learner never practised. The joining column is therefore Items.TopicId/Items.CefrLevel, which
-- is also what makes the two answer shapes share one branch each.
--
-- THE DOUBLE COUNT #250 FOUND CANNOT ARISE HERE, and that is worth stating because the shapes DO
-- overlap: AttemptAnswers keeps a non-null QuestionId on a pool answer so the legacy reader can still
-- resolve it. This view has exactly ONE branch per attempt answer - the pinned-version join to Items -
-- and no branch that reaches a cell through Questions, because Questions has no cell to reach. A row
-- is counted once or not at all. What the join must not do is fall back to the session's cell when the
-- item is missing, and it does not: an answer with no ItemId yields no row.
--
-- AN UNANSWERED ROW IS NOT AN OBSERVATION. PracticeAnswers rows are written when a drill starts, with
-- every answer column null; counting them would inflate every denominator by the size of the drill.
-- IsCorrect IS NULL is the filter, and it is the same one vw_DailyPracticeVolume uses.
--
-- A TOPIC WITH NO ITEMS PRODUCES NO ROW, rather than a zero row: there is nothing to average, and a
-- 0% row is a claim about the learner that no answer supports.
CREATE VIEW vw_UserTopicAccuracy AS
  WITH cells AS (
    SELECT x.UserId, x.TopicId, x.CefrLevel, COUNT(x.IsCorrect) AS Answered,
           SUM(CASE WHEN x.IsCorrect = 1 THEN 1 ELSE 0 END) AS Correct
    FROM (
      -- pool practice answer: the item names the cell
      SELECT ps.UserId, i.TopicId, i.CefrLevel, pa.IsCorrect
      FROM PracticeAnswers pa
      JOIN PracticeSessions ps ON ps.SessionId = pa.SessionId
      JOIN Items i            ON i.ItemId = pa.ItemId
      WHERE pa.IsCorrect IS NOT NULL
      UNION ALL
      -- pool attempt answer: the same item join, on a graded attempt only
      SELECT a.UserId, i.TopicId, i.CefrLevel, aa.IsCorrect
      FROM AttemptAnswers aa
      JOIN ExamAttempts a ON a.AttemptId = aa.AttemptId
      JOIN Items i        ON i.ItemId = aa.ItemId
      WHERE a.Status = 'graded' AND aa.IsCorrect IS NOT NULL
    ) x
    GROUP BY x.UserId, x.TopicId, x.CefrLevel
  )
  -- The percentage is HERE, not in C#. D3 makes this view the deterministic accuracy SQL that the
  -- weakness page and the AI insight both read; a reader that divided Correct by Answered would be a
  -- second definition of the same figure.
  SELECT UserId, TopicId, CefrLevel, Answered, Correct,
         CAST(ROUND(100.0 * Correct / Answered, 0) AS INT) AS AccuracyPercent
  FROM cells;
GO
-- UI-34's "frequently missed question types" and the two readers that used to sit on a per-CATEGORY
-- aggregation - the dashboard's per-skill cards (LearnerService.Skills) and the AI insight payload
-- (AiInsightService.Dashboard / Paths / Exams / Practice) - read vw_UserTopicAccuracy above as of
-- #271. vw_UserCategoryAccuracy is GONE rather than retained beside it: a second view computing the
-- same answers on the retired axis is a second definition of one figure, which is the defect D3's
-- "every reader of CategoryId moves to TopicId" exists to remove. It also could not be rewritten onto
-- the pool - Questions carries no TopicId, so a category view can only ever see legacy answers.
--
-- A LEGACY answer therefore has no accuracy cell at all, on any screen. That is correct, not a gap to
-- paper over: the topic vocabulary is a closed set of twenty and no mapping from a retired category
-- to a topic exists. A screen must render "no cell yet" for such a learner, never a 0%.
CREATE VIEW vw_UserSectionAccuracy AS
  SELECT ps.UserId, s.SectionId, s.Name AS SectionName,
         COUNT(pa.IsCorrect) AS Answered,
         SUM(CASE WHEN pa.IsCorrect=1 THEN 1 ELSE 0 END) AS Correct
  FROM PracticeAnswers pa
  JOIN PracticeSessions ps ON ps.SessionId = pa.SessionId
  JOIN Sections s ON s.SectionId = ps.SectionId
  GROUP BY ps.UserId, s.SectionId, s.Name;
GO
-- ============ #235: daily practice volume, derived and never counted ============
-- D12 / PLC-64 / NFI-05: the daily ceiling is one session per (topic, level) cell per day and 50-60
-- objective items or 4 productive tasks overall. Both halves are DERIVED here from the answer rows, so
-- there is no counter column to drift and no service that can disagree with the database about how much
-- a learner has done today.
--
-- What counts as "done today" is an ANSWER, not a session row: a session is written with all of its
-- answers blank at the moment it starts, so counting rows would charge a learner for a drill they never
-- opened. The four columns of `AnswerContent.IsAnswered` are the SPOT: the same four the practice screen
-- asks, so "answered" cannot mean one thing here and another there.
--
-- The day is the UTC day of `PracticeAnswers.AnsweredAt`. That column is a legacy DATETIME2 written with
-- DateTime.UtcNow, so it holds UTC; a row with no AnsweredAt (written before #156) cannot be dated and
-- contributes to no day rather than to an invented one.
--
-- `DistinctCells` counts the pool cells actually answered in, which is what makes "one session per cell
-- per day" checkable from the same read. A section drill names no cell, so it contributes 0 to that
-- count and still contributes to the volume. The CASE returns NULL for a section drill on purpose:
-- COUNT(DISTINCT ...) skips NULL, whereas CONCAT over two NULLs would yield ':' and count as one cell.
CREATE VIEW vw_DailyPracticeVolume AS
  SELECT ps.UserId,
         CAST(pa.AnsweredAt AS DATE) AS PractisedOn,
         COUNT(*) AS AnsweredCount,
         SUM(CASE WHEN i.Modality IN ('speaking','writing')
                    OR s.Modality IN ('speaking','writing') THEN 1 ELSE 0 END) AS ProductiveTaskCount,
         COUNT(DISTINCT CASE WHEN ps.TopicId IS NULL OR ps.CefrLevel IS NULL THEN NULL
                             ELSE CONCAT(CONVERT(VARCHAR(11), ps.TopicId), ':', ps.CefrLevel)
                        END) AS DistinctCells
  FROM PracticeAnswers pa
  JOIN PracticeSessions ps ON ps.SessionId = pa.SessionId
  -- #269: a practice answer has no SectionId of its own any more; the session names the section it
  -- drills (NULL for a pool drill, which is the LEFT JOIN's job).
  LEFT JOIN Sections s     ON s.SectionId = ps.SectionId
  LEFT JOIN Items i        ON i.ItemId = pa.ItemId
  WHERE pa.AnsweredAt IS NOT NULL
    AND (pa.SelectedOptionId IS NOT NULL
      OR pa.SelectedLabel IS NOT NULL
      OR pa.ResponseText IS NOT NULL
      OR pa.SelectedOptionIdsJson IS NOT NULL)
  GROUP BY ps.UserId, CAST(pa.AnsweredAt AS DATE);
GO
-- ============ Query indexes ============
-- SQL Server does not index foreign keys. These cover the joins the triggers run on every write
-- and the screens' hot reads. Uniqueness lives above; nothing here enforces a rule.
-- Identity
CREATE INDEX IX_EmailVerificationTokens_User ON EmailVerificationTokens(UserId);
CREATE INDEX IX_PasswordResetTokens_User     ON PasswordResetTokens(UserId);
-- Content (exam list, exam editor, question bank). #269: the three indexes into the legacy
-- QuestionGroups/Questions tables went with them; a paper's positions are indexed by their own keys.
CREATE INDEX IX_Exams_Cert_Status        ON Exams(CertId, Purpose, Status) WHERE IsDeleted = 0;
-- #184: the pool. Cell lookup is (topic, level) - the coordinate a practice drill and a path step
-- both ask for; the selection index is the two columns every item-selection query filters on.
CREATE INDEX IX_Items_Cell               ON Items(TopicId, CefrLevel) INCLUDE (Modality, ItemType);
CREATE INDEX IX_Items_Selection          ON Items(LifecycleStatus, ContentTier) INCLUDE (CefrLevel, PlacementEligible);
CREATE INDEX IX_Items_Stimulus           ON Items(StimulusId) WHERE StimulusId IS NOT NULL;
CREATE INDEX IX_ItemVersions_Item        ON ItemVersions(ItemId, VersionNumber);
CREATE INDEX IX_ImportLog_Batch          ON ImportLog(BatchId);
-- Practice (history, weakness views, path module progress)
CREATE INDEX IX_Practice_User            ON PracticeSessions(UserId, StartedAt);
CREATE INDEX IX_Practice_PathStep        ON PracticeSessions(LearnerPathStepId) WHERE LearnerPathStepId IS NOT NULL;
CREATE INDEX IX_PracticeAnswers_Version  ON PracticeAnswers(ItemVersionId);
-- #187: a pool session is resumed and ceiling-checked by its cell, not by a section.
CREATE INDEX IX_Practice_Cell           ON PracticeSessions(UserId, TopicId, CefrLevel, StartedAt) WHERE TopicId IS NOT NULL;
-- Mock exam (exam history, review, grading triggers)
CREATE INDEX IX_Attempts_User            ON ExamAttempts(UserId, StartedAt) INCLUDE (ExamId, Status, TotalScore);
CREATE INDEX IX_Attempts_Exam            ON ExamAttempts(ExamId);
-- #269: answers and responses are found by their roster position and pinned version.
CREATE INDEX IX_AttemptAnswers_ExamItem  ON AttemptAnswers(ExamItemId);
CREATE INDEX IX_FreeResponses_ExamItem   ON FreeResponses(ExamItemId);
-- AI grading worker queue: pending first, failed for retry
CREATE INDEX IX_FreeResponses_Pending    ON FreeResponses(SubmittedAt) WHERE Status = 'pending_ai';
CREATE INDEX IX_AiGradings_Queue         ON AiGradings(Status, RequestedAt) WHERE Status IN ('pending','failed');
CREATE INDEX IX_Recommendations_User     ON Recommendations(UserId, CertId);
CREATE INDEX IX_Recommendations_Attempt  ON Recommendations(AttemptId) WHERE AttemptId IS NOT NULL;
CREATE INDEX IX_Recommendations_Session  ON Recommendations(PracticeSessionId) WHERE PracticeSessionId IS NOT NULL;
-- Learning path
CREATE INDEX IX_Placement_User           ON PlacementTests(UserId, CertId);
CREATE INDEX IX_LearnerPaths_User        ON LearnerPaths(UserId, CertId);
CREATE INDEX IX_Steps_LearnerPath        ON LearnerPathSteps(LearnerPathId);
-- #154: the path screen reads its steps week by week, so the plan order is the index order
CREATE INDEX IX_Steps_LearnerPath_Week   ON LearnerPathSteps(LearnerPathId, WeekNumber, StepId);
-- #232: #233 packs a week to a minute budget and orders it by WeekSequence, so the plan's own
-- ordering is the index rather than the identity column that happens to match it today.
CREATE INDEX IX_Steps_LearnerPath_Sequence ON LearnerPathSteps(LearnerPathId, WeekNumber, WeekSequence);
-- #232: "which steps are at this coordinate" is what a re-plan asks, and what a mastery change
-- invalidates. Keyed without CertId because the coordinate is certificate-agnostic (D4).
CREATE INDEX IX_Steps_Coordinate         ON LearnerPathSteps(TopicId, TargetCefr, StepType);
-- Mentor
CREATE INDEX IX_Mentors_Cert             ON MentorProfiles(CertId) WHERE IsActive = 1;
CREATE INDEX IX_MentorWeeklySlots_Mentor ON MentorWeeklySlots(MentorId, DayOfWeek);
CREATE INDEX IX_MentorSlots_Mentor       ON MentorSlots(MentorId, StartAt);
CREATE INDEX IX_Bookings_User            ON Bookings(UserId, CreatedAt);
CREATE INDEX IX_Bookings_Mentor          ON Bookings(MentorId);
CREATE INDEX IX_Complaints_Status        ON Complaints(Status, CreatedAt);
CREATE INDEX IX_Complaints_User          ON Complaints(UserId);
-- Payment (wallet history, balance view, admin orders)
CREATE INDEX IX_Orders_User              ON Orders(UserId, CreatedAt);
CREATE INDEX IX_Orders_Open              ON Orders(CreatedAt) WHERE Status IN ('pending','reconciling');
CREATE INDEX IX_CreditTxn_User           ON CreditTransactions(UserId, CreatedAt) INCLUDE (Reason, Delta);
GO
-- ============ #201 / D16: no self-approval, and triage that cannot write ============
-- Two promises that must survive every combination of the five namespaces (ADM-02, ADM-03, ADM-04).
-- Both are refused HERE, in the transaction, because a check that exists only in a controller is
-- bypassed by the next endpoint someone writes.
--
-- The actor is a COLUMN ON THE WRITE, never `sp_set_session_context` (PRN-01): this product reaches
-- one database principal, so the database cannot otherwise tell one staff account from another, and
-- session context survives commit and rollback and would vouch for whatever ran next.
--
-- `LastWrittenBy` is NULL for a seed, a fixture or a migration. That is not a hole to be closed by
-- refusing NULL: the guarded tables are written by ~60 C# sites and ~300 fixture statements that
-- predate this rule, and a NOT NULL declaration would refuse the demo seed. The refusal below is a
-- property of the DECLARED account, which is what makes it "a property of the data, not of the role".

-- ---- the authorship the schema did not carry ----
--
-- Until #201 nothing recorded who wrote an item version: `ItemVersions` had `CreatedAt` and no
-- author, and `Items` had neither. So "a batch it uploaded OR LAST MODIFIED" was inexpressible for
-- the item half - an uploader whose colleague revised the items could still clear the review.
--
-- One column serves both readings of "last modified": on INSERT it is the author of that version; on
-- the one in-place edit (a cosmetic `Explanation` fix, CNT-43) it is the account that made the edit.
-- The approver is barred from the newest version's value, so a typo fix moves authorship exactly as
-- ADM-02's "or last modified" clause says it should.
ALTER TABLE ItemVersions ADD LastWrittenBy INT NULL
  CONSTRAINT FK_ItemVersions_LastWrittenBy REFERENCES Users(UserId);
GO
-- The batch half of the same clause. #205 is the staging workbench that will edit a batch; no path
-- writes this column today, so it is covered by a direct-write test rather than by a service. A batch
-- row is NOT the item (revising an item does not touch its batch), which is why ItemVersions carries
-- its own column above instead of borrowing this one.
ALTER TABLE ImportBatches ADD LastModifiedBy INT NULL
  CONSTRAINT FK_ImportBatches_LastModifiedBy REFERENCES Users(UserId);
GO
-- The triage guard reads the acting account on each table a prohibited write can reach. ADM-03 names
-- item text and keys, rubrics, AND exam structure - a check on item edits alone leaves exam structure
-- editable by triage, which D16 forbids in the same sentence. Sections and ExamItems are the assembly;
-- GradingCriteria and its descriptors are the rubric. ItemVersions already carries the column above.
ALTER TABLE ExamItems                 ADD LastWrittenBy INT NULL CONSTRAINT FK_ExamItems_LastWrittenBy REFERENCES Users(UserId);
GO
ALTER TABLE Sections                  ADD LastWrittenBy INT NULL CONSTRAINT FK_Sections_LastWrittenBy REFERENCES Users(UserId);
GO
ALTER TABLE GradingCriteria           ADD LastWrittenBy INT NULL CONSTRAINT FK_GradingCriteria_LastWrittenBy REFERENCES Users(UserId);
GO
-- ADM-03 names "a write to item text, a key, a rubric or exam structure". The last two guards were
-- missing: a triage-only account could publish an exam (`Exams.Status`) or flip an item to
-- `active` (`Items.LifecycleStatus`), both of which are exactly the authority triage may not have.
-- Proved by direct write on a strict catalog before these two were added.
ALTER TABLE Exams                     ADD LastWrittenBy INT NULL CONSTRAINT FK_Exams_LastWrittenBy REFERENCES Users(UserId);
GO
ALTER TABLE Items                     ADD LastWrittenBy INT NULL CONSTRAINT FK_Items_LastWrittenBy REFERENCES Users(UserId);
GO
-- ---- is this account triage, and nothing that may author? ----
--
-- `can_override` is empty: holding more namespaces does not WAIVE either invariant, and this function
-- is where that is stated for triage. An account is triage-only when it holds
-- `operations.support.triage` and holds neither namespace that authorizes the write it is attempting.
-- An account that also holds `pool.content.author` or `assessment.blueprint.design` is an author, and
-- its writes are reviewed by the no-self-approval rule below rather than refused here. Reading
-- "triage may not write" as "any account holding triage may never author" would make content
-- unauthorable by the only account that holds all five, which is not what ADM-03 is for.
--
-- One function rather than five copies of the join: five copies is how one of them ends up subtly
-- different, and the difference would be a silently unguarded table.
CREATE FUNCTION dbo.fn_IsTriageOnly (@UserId INT) RETURNS BIT AS
BEGIN
  IF @UserId IS NULL RETURN 0;
  RETURN CASE WHEN EXISTS (
    SELECT 1 FROM Users u
    JOIN RolePermissions rp ON rp.RoleId = u.RoleId
    WHERE u.UserId = @UserId AND rp.PermissionCode = 'operations.support.triage'
  ) AND NOT EXISTS (
    SELECT 1 FROM Users u
    JOIN RolePermissions rp ON rp.RoleId = u.RoleId
    WHERE u.UserId = @UserId
      AND rp.PermissionCode IN ('pool.content.author','assessment.blueprint.design')
  ) THEN 1 ELSE 0 END;
END;
GO
-- ---- the fixture-catalog flag, and why it exists ----
--
-- A raw fixture INSERT and a raw triage INSERT are INDISTINGUISHABLE: both are SQL that declares no
-- account. No trigger can separate them without an identity channel, and SQL Server has none that
-- survives connection pooling (see PRN-01 below). So "fixtures need not declare" and "triage cannot
-- write undeclared" cannot both be true in the same catalog.
--
-- The honest split is by CATALOG, not by statement: a catalog that holds only disposable test data
-- may relax the declaration requirement, and a catalog that holds anything real may not. This table
-- is that switch. It carries no identity - it cannot name an account - and it does not relax the
-- triage rule itself, which applies to every declared id in every catalog.
--
-- Set by `docs/schema-tests/reset-and-load.sh` and `tests/Services.Tests/DatabaseFixture.cs`, which
-- create throwaway catalogs. `docs/seed-demo.sql` saves the prior value, turns it on for its own
-- writes, and restores it, so a catalog that loads the demo seed ends exactly as strict as it began.
-- Production never sets it, so an undeclared write there is refused.
CREATE TABLE CatalogMode (
  -- One row, pinned by the CHECK. A second row would be a second answer to one question.
  ModeId           TINYINT NOT NULL PRIMARY KEY CONSTRAINT CK_CatalogMode_SingleRow CHECK (ModeId = 1),
  IsFixtureCatalog BIT NOT NULL CONSTRAINT DF_CatalogMode_Fixture DEFAULT 0
);
GO
CREATE FUNCTION dbo.fn_IsFixtureCatalog() RETURNS BIT AS
BEGIN
  -- A missing row is "not a fixture catalog": strict is the default, so forgetting to provision the
  -- row can never silently relax a guard.
  RETURN ISNULL((SELECT IsFixtureCatalog FROM CatalogMode WHERE ModeId = 1), 0);
END;
GO
-- ---- invariant 2: triage cannot write ----
--
-- THE GUARD'S QUESTION IS "WHO IS WRITING". The first draft got this wrong in a way the review
-- reproduced on a real database: it asked `fn_IsTriageOnly(i.LastWrittenBy)` and required
-- re-declaration only when the STORED row already named an author. So an undeclared row was a free
-- pass - a triage UPDATE of `Explanation` on any seeded row was ACCEPTED, and a raw INSERT of a whole
-- new version with no declaration was ACCEPTED. Both are the criterion's own words ("a write to item
-- text"), and both came from confusing the DECLARED AUTHOR with the WRITER.
--
-- Three rules, on INSERT and on UPDATE, on all five tables:
--   1. a write must DECLARE its actor - `LastWrittenBy IS NULL` is refused, so no row is a free pass;
--   2. the DECLARED actor must not be triage-only - this holds in EVERY catalog, fixture or not, so
--      the exemption below never lets a triage account through;
--   3. an UPDATE must RE-DECLARE the column. Without this, a write that sets only `Explanation`
--      inherits the previous author's name and the check would be answering about nobody.
--
-- WHY A COLUMN AND NOT SESSION CONTEXT (PRN-01). PRN-01 forbids `sp_set_session_context` because it
-- survives commit and rollback on a pooled connection and would vouch for code that never declared
-- itself. That reasoning stands here, and this guard identifies NOBODY from session state: rules 1-3
-- read only the column the write assigns. SQL Server has no transaction-local identity channel at all
-- - session context and `CONTEXT_INFO` are both per-session and survive pooling, and a temp table is
-- pooled the same way - so the column is the only honest channel. ADR-0012 reached the same
-- conclusion for the writer column.
--
-- THE EXEMPTION, AND ITS EXACT SHAPE. Rules 1 and 3 are relaxed by exactly one boolean,
-- `dbo.fn_IsFixtureCatalog()`, which is true only in a catalog that holds nothing but disposable test
-- data (see CatalogMode). Rule 2 is never relaxed. The shape of the exemption is therefore:
--
--   fixture catalog : an UNDECLARED write is allowed; a write declaring a triage account is REFUSED.
--   any other       : an undeclared write is refused; a write declaring triage is refused.
--
-- It exists because a raw fixture INSERT and a raw triage INSERT are indistinguishable - both are SQL
-- that declares no account - and SQL Server has no channel that would separate them. A catalog-scoped
-- flag is the only split that is honest about that. It carries no identity: it cannot name an account,
-- so it cannot be used to impersonate one, and rule 2 still refuses every triage id inside a fixture
-- catalog (domain35 proves both halves). Production never sets it, so an undeclared write there is
-- refused. A caller who can write the flag can equally run `ALTER TABLE ... DISABLE TRIGGER`; that is
-- the raw-database trust boundary, stated rather than hidden.
CREATE TRIGGER trg_ItemVersions_TriageCannotWrite
ON ItemVersions AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  -- A DELETE carries no `inserted` row, so the account to judge is the one recorded on the
  -- row being removed. The invariant is about triage, not about denying deletion to an
  -- author: a triage-only account may retire nothing, because retiring is a write.
  IF EXISTS (SELECT 1 FROM deleted dd WHERE dbo.fn_IsTriageOnly(dd.LastWrittenBy) = 1)
    THROW 50082, 'ADM-03: support triage cannot delete from ItemVersions; the account that wrote it cannot be a triage-only account.', 1;
  IF EXISTS (
    SELECT 1 FROM inserted i
    LEFT JOIN deleted d ON d.ItemVersionId = i.ItemVersionId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.ItemVersionId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)
    THROW 50081, 'ADM-03: support triage may inspect item text and keys but cannot write them. A write to ItemVersions must declare who made it (LastWrittenBy), and an update must re-declare it.', 1;
END;
GO
CREATE TRIGGER trg_ExamItems_TriageCannotWrite
ON ExamItems AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  -- A DELETE carries no `inserted` row, so the account to judge is the one recorded on the
  -- row being removed. The invariant is about triage, not about denying deletion to an
  -- author: a triage-only account may retire nothing, because retiring is a write.
  IF EXISTS (SELECT 1 FROM deleted dd WHERE dbo.fn_IsTriageOnly(dd.LastWrittenBy) = 1)
    THROW 50082, 'ADM-03: support triage cannot delete from ExamItems; the account that wrote it cannot be a triage-only account.', 1;
  IF EXISTS (
    SELECT 1 FROM inserted i
    LEFT JOIN deleted d ON d.ExamItemId = i.ExamItemId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.ExamItemId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)
    THROW 50081, 'ADM-03: support triage cannot change exam assembly. A write to ExamItems must declare who made it (LastWrittenBy), and an update must re-declare it.', 1;
END;
GO
CREATE TRIGGER trg_Sections_TriageCannotWrite
ON Sections AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  -- A DELETE carries no `inserted` row, so the account to judge is the one recorded on the
  -- row being removed. The invariant is about triage, not about denying deletion to an
  -- author: a triage-only account may retire nothing, because retiring is a write.
  IF EXISTS (SELECT 1 FROM deleted dd WHERE dbo.fn_IsTriageOnly(dd.LastWrittenBy) = 1)
    THROW 50082, 'ADM-03: support triage cannot delete from Sections; the account that wrote it cannot be a triage-only account.', 1;
  IF EXISTS (
    SELECT 1 FROM inserted i
    LEFT JOIN deleted d ON d.SectionId = i.SectionId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.SectionId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)
    THROW 50081, 'ADM-03: support triage cannot change exam structure. A write to Sections must declare who made it (LastWrittenBy), and an update must re-declare it.', 1;
END;
GO
CREATE TRIGGER trg_GradingCriteria_TriageCannotWrite
ON GradingCriteria AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  -- A DELETE carries no `inserted` row, so the account to judge is the one recorded on the
  -- row being removed. The invariant is about triage, not about denying deletion to an
  -- author: a triage-only account may retire nothing, because retiring is a write.
  IF EXISTS (SELECT 1 FROM deleted dd WHERE dbo.fn_IsTriageOnly(dd.LastWrittenBy) = 1)
    THROW 50082, 'ADM-03: support triage cannot delete from GradingCriteria; the account that wrote it cannot be a triage-only account.', 1;
  IF EXISTS (
    SELECT 1 FROM inserted i
    LEFT JOIN deleted d ON d.CriterionId = i.CriterionId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.CriterionId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)
    THROW 50081, 'ADM-03: support triage cannot write a rubric. A write to GradingCriteria must declare who made it (LastWrittenBy), and an update must re-declare it.', 1;
END;
GO
CREATE TRIGGER trg_Exams_TriageCannotWrite
ON Exams AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  -- A DELETE carries no `inserted` row, so the account to judge is the one recorded on the
  -- row being removed. The invariant is about triage, not about denying deletion to an
  -- author: a triage-only account may retire nothing, because retiring is a write.
  IF EXISTS (SELECT 1 FROM deleted dd WHERE dbo.fn_IsTriageOnly(dd.LastWrittenBy) = 1)
    THROW 50082, 'ADM-03: support triage cannot delete from Exams; the account that wrote it cannot be a triage-only account.', 1;
  IF EXISTS (
    SELECT 1 FROM inserted i
    LEFT JOIN deleted d ON d.ExamId = i.ExamId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.ExamId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)
    THROW 50081, 'ADM-03: support triage cannot publish or restructure an exam. A write to Exams must declare who made it (LastWrittenBy), and an update must re-declare it.', 1;
END;
GO
CREATE TRIGGER trg_Items_TriageCannotWrite
ON Items AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  -- A DELETE carries no `inserted` row, so the account to judge is the one recorded on the
  -- row being removed. The invariant is about triage, not about denying deletion to an
  -- author: a triage-only account may retire nothing, because retiring is a write.
  IF EXISTS (SELECT 1 FROM deleted dd WHERE dbo.fn_IsTriageOnly(dd.LastWrittenBy) = 1)
    THROW 50082, 'ADM-03: support triage cannot delete from Items; the account that wrote it cannot be a triage-only account.', 1;
  IF EXISTS (
    SELECT 1 FROM inserted i
    LEFT JOIN deleted d ON d.ItemId = i.ItemId
    WHERE (dbo.fn_IsFixtureCatalog() = 0
             AND (i.LastWrittenBy IS NULL
                  OR (d.ItemId IS NOT NULL AND NOT UPDATE(LastWrittenBy))))
       OR dbo.fn_IsTriageOnly(i.LastWrittenBy) = 1)
    THROW 50081, 'ADM-03: support triage cannot set an item''s lifecycle or topic. A write to Items must declare who made it (LastWrittenBy), and an update must re-declare it.', 1;
END;
GO
-- ============ #205: the ingestion and staging workbench ============
--
-- Two changes. Neither adds a table: ImportBatches already carries the batch, the uploader and the
-- timestamp, and ImportLog already carries the line and the message. What is missing is the three
-- things this page cannot say without.
--
-- 1. A POOL UPLOAD HAS NO CERTIFICATE. ImportBatches.CertId was NOT NULL because the only importer
--    was the exam one, which always names a paper's certificate - and that importer still always
--    writes one. Pool stock does not name one: CNT-01 makes it certificate-agnostic, and domain22
--    inserts Items, ItemVersions and Stimuli with zero certificates in the database. #198's seam
--    therefore could not record its own batch at all, which its own doc comment says, so "who
--    submitted what, and when" had nowhere to land. Nullable is the honest shape: the column means
--    "the certificate this upload was for", and a pool upload is for none. The batch projection
--    left-joins Certificates, so a NULL renders as an empty code rather than dropping the row.
--
-- 2. ImportLog gains the verdict's own three facts, and ImportBatches the document they came from:
--
--      * RuleCode - the gate's machine code (criterion 1, "the failing rule"). Message is prose; a
--        page that recovered the rule by parsing a sentence would break the first time one was
--        reworded, and the codes are already a stable vocabulary (GateOneRules, GateTwoRules,
--        IngestionRules). Named RuleCode rather than Rule because RULE is a reserved word in T-SQL
--        and the unquoted form refuses to parse.
--      * ItemId - the item an accepted record became (criterion 2). Without it, "staged" could only
--        be joined on Items.BatchTag, which is the FILE's own tag (CNT-03) rather than the batch:
--        two uploads of one file collide on it, and a file declaring none matches nothing.
--      * SourceDocument - the upload verbatim, once per batch (criterion 3). Re-judging a corrected
--        record needs its GROUP, not only the record: gate two profiles the shared passage and gate
--        four's span rule is about siblings. A corrected record judged without them would be judged
--        by a weaker check and reported as the same one. The same document also supplies the record
--        text back to the correction form, so no second copy per row is needed - and a copy per row
--        would store the shared passage two to four times.
--
--    RowNo already separates the two kinds of row this table will now hold: a verdict names the line
--    it judged, a batch-level note names none. That is what makes "one row per record" a query
--    rather than a convention, and the read path below depends on it.
--
--    Attempt is why a re-submitted record keeps its history without the list growing: a re-judge
--    writes new verdicts at Attempt + 1 for the lines it judged, and the read path takes the highest
--    attempt per line. Deleting the old rows instead would have been one line of code and would have
--    erased the record of the rejection that was corrected - the very thing a workbench exists to
--    keep.
ALTER TABLE ImportBatches ALTER COLUMN CertId INT NULL;
GO
ALTER TABLE ImportBatches ADD SourceDocument NVARCHAR(MAX) NULL;
GO
ALTER TABLE ImportLog ADD RuleCode VARCHAR(40) NULL;
GO
ALTER TABLE ImportLog ADD Attempt INT NOT NULL CONSTRAINT DF_ImportLog_Attempt DEFAULT 1;
GO
ALTER TABLE ImportLog ADD ItemId INT NULL
  CONSTRAINT FK_ImportLog_Item REFERENCES Items(ItemId) ON DELETE SET NULL;
GO
-- ============ Domain 40: #240 The leveled lesson ============
-- A lesson is addressed by (skill, topic, level), and it carries the level itself. Before this block a
-- lesson had a skill and a legacy question-category and no level at all, so "which lesson do I read at
-- R-DET B1" had no answer: the path picked whichever lesson of the skill sorted first and pitched it
-- at a level the row did not hold. D0.5's 120 lessons are one per cell, indexed by exactly these three.
--
-- TopicId and Modality arrive together, the same idiom Items already uses: Modality is carried down so
-- a reading lesson cannot sit on a listening topic, and so a lesson sitting (PracticeSessions, below)
-- can name its cell without re-reading the skill.
ALTER TABLE Lessons ADD
  Modality  VARCHAR(10) NOT NULL
    CONSTRAINT CK_Lessons_Modality CHECK (Modality IN ('listening','reading','speaking','writing')),
  TopicId   INT NOT NULL,
  CefrLevel VARCHAR(10) NOT NULL
    CONSTRAINT CK_Lessons_Level CHECK (CefrLevel IN ('A1','A2','B1','B2','C1','C2'));
GO
ALTER TABLE Lessons ADD CONSTRAINT FK_Lessons_SkillModality
  FOREIGN KEY (SkillId, Modality) REFERENCES Skills(SkillId, Modality);
GO
ALTER TABLE Lessons ADD CONSTRAINT FK_Lessons_TopicModality
  FOREIGN KEY (TopicId, Modality) REFERENCES Topics(TopicId, Modality);
GO
-- The cell lookup the reader and the path both use. Not UNIQUE: the demo seed legitimately holds more
-- than one lesson for one cell today, and the lookup's tie-break (DisplayOrder, then LessonId) is what
-- makes "which of them" a reproducible answer rather than the storage engine's.
CREATE INDEX IX_Lessons_Cell ON Lessons(CertId, SkillId, TopicId, CefrLevel);
GO
-- D0.5: every lesson carries two sub-modules - module_primary (the rule and worked examples) and
-- module_remedial (distractor anatomy, or the band k-1 -> k comparison). They are separate because a
-- second attempt at the same cell must not re-read identical instruction, so a module is a set of card
-- ROWS with its own content, never a flag on one set of cards.
--
-- The default is 'primary', and it is not a lie: every card authored before this block IS primary
-- content. A remedial card opts in.
ALTER TABLE LessonCards ADD Module VARCHAR(10) NOT NULL
  CONSTRAINT DF_LessonCards_Module DEFAULT 'primary'
  CONSTRAINT CK_LessonCards_Module CHECK (Module IN ('primary','remedial'));
GO
-- Progress is per learner per lesson PER MODULE: a remedial visit has its own place to resume and its
-- own "complete", otherwise a learner who finished the primary module would find the remedial one
-- already ticked off without reading it - which is criterion 5's trap.
ALTER TABLE LearnerLessonProgress ADD Module VARCHAR(10) NOT NULL
  CONSTRAINT DF_LLP_Module DEFAULT 'primary'
  CONSTRAINT CK_LLP_Module CHECK (Module IN ('primary','remedial'));
GO
ALTER TABLE LearnerLessonProgress DROP CONSTRAINT UQ_LLP_UserLesson;
GO
ALTER TABLE LearnerLessonProgress ADD CONSTRAINT UQ_LLP_UserLessonModule
  UNIQUE (UserId, LessonId, Module);
GO
-- #240 criterion 3: a micro-check answer must leave the same evidence a drill answer leaves, which is
-- a PracticeAnswers row - a private counter is invisible to vw_UserTopicAccuracy and to the cell
-- mastery the path sizes its steps from. A lesson sitting is therefore an ordinary pool-shaped
-- PracticeSessions row (SectionId NULL, TopicId + CefrLevel from the lesson) that names its lesson,
-- and one answer names its card.
--
-- LessonId is deliberately NOT mapped as an EF navigation: it composes a nullable column with the
-- non-nullable CertId, and EF reads a partly-nullable composite key as required, which would make
-- every ordinary drill session fail to save. The constraint still holds server-side (MATCH SIMPLE).
ALTER TABLE PracticeSessions ADD LessonId INT NULL;
GO
ALTER TABLE PracticeSessions ADD CONSTRAINT FK_Practice_LessonCert
  FOREIGN KEY (LessonId, CertId) REFERENCES Lessons(LessonId, CertId);
GO
-- One OPEN sitting per learner per lesson: two would leave "where did I stop" with two answers. The
-- filter is on the status, not on LessonId alone, so a finished sitting is history and a later visit
-- starts a new one.
CREATE UNIQUE INDEX UX_Practice_OneLessonSitting ON PracticeSessions(UserId, LessonId)
  WHERE LessonId IS NOT NULL AND Status = 'in_progress';
GO
ALTER TABLE PracticeAnswers ADD CardId INT NULL REFERENCES LessonCards(CardId);
GO
-- A second shape beside the pool item shape: a lesson micro-check names its card and nothing else.
-- #269: the legacy question arm is gone with the Questions table, so the shape is now two arms, and it
-- is declared here (after CardId exists) rather than re-declaring a constraint that no longer has three.
ALTER TABLE PracticeAnswers ADD CONSTRAINT CK_PracticeAnswers_Shape CHECK (
     (ItemId IS NOT NULL AND ItemVersionId IS NOT NULL AND CardId IS NULL)
  OR (ItemId IS NULL AND ItemVersionId IS NULL AND CardId IS NOT NULL)
);
GO
CREATE UNIQUE INDEX UQ_PracticeAnswers_Card ON PracticeAnswers(SessionId, CardId)
  WHERE CardId IS NOT NULL;
GO
