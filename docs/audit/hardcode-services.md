# Hardcode audit — services, API, contracts, data layer

Generated: 2026-10-31
Total rows: 82 · BLOCKER 11 · WRONG_DISPLAY 52 · COSMETIC 19 · CLOSED_ENUM 14

| path:line | CATEGORY | what it assumes | breaks when | severity |
|---|---|---|---|---|
| src/Services/Admin/Exams/AdminExamService.Audio.cs:35 | SKILL_CODE | section audio may only be uploaded when `Modality == "listening"` | a cert that attaches audio to a reading/dictation section is rejected outright | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Audit.cs:72 | SKILL_CODE | `speaking_practice` purpose ⇒ the exam's sections are exactly those with `Modality == "speaking"` | a new single-skill purpose has no branch; audit falls through to the `_ =>` arm | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Audit.cs:73 | SKILL_CODE | `writing_practice` purpose ⇒ sections with `Modality == "writing"` | same as above for any added purpose | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Audit.cs:187 | SKILL_CODE | audio-readiness is only audited when the section's modality is `"listening"` | a reading section carrying audio is never checked for a master track | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Audit.cs:245 | SKILL_CODE | a master audio track is only required/expected on `"listening"` sections | non-listening audio-driven sections bypass the audit | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Audit.cs:455 | SKILL_CODE | word-bound validation (`MinWords`/`MaxWords` > 0) is enforced only when modality is `"writing"` | a cert whose writing tasks carry no word cap, or whose reading tasks need one, is mis-audited | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Audit.cs:469 | SKILL_CODE | preparation seconds are only required on `"speaking"` | a cert with prep time on another modality is not validated | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Catalog.cs:97 | SKILL_CODE | LINQ filter `.Where(s => ... && s.Modality == "listening")` — literal skill modality in a query | any vocabulary other than the four CK values silently yields an empty list | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Catalog.cs:194 | SKILL_CODE | `hasAudio` defaults to `true` for everything except `"listening"` | a new audio-bearing modality shows as audio-ready without a file | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Groups.cs:204 | SKILL_CODE | a section is free-response iff modality is `"speaking"` or `"writing"` | a cert using free_response inside reading (short answer) gets MCQ group defaults | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Groups.cs:260 | OTHER | speaking prep = 15 s, everything else = 60 s | cert with its own prep policy gets a wrong default seeded | COSMETIC |
| src/Services/Admin/Exams/AdminExamService.Groups.cs:261 | OTHER | response window = 45 s speaking / 1800 s writing | cert-specific timing ignored | COSMETIC |
| src/Services/Admin/Exams/AdminExamService.Groups.cs:262 | OTHER | writing `MinWords = 150` | cert with a different task length displays the wrong target | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Groups.cs:263 | OTHER | writing `MaxWords = 300` | same | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Groups.cs:279 | OTHER | option labels are either `A,B,C` or `A,B,C,D` — only counts 3 and 4 exist | `Sections.OptionCount` is CHECK-constrained 2..6, so a 5- or 6-option section is mislabelled | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Lifecycle.cs:143 | SKILL_CODE | publish guard tests `Section.Modality != "speaking"` | any renamed speaking modality passes the guard silently | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Lifecycle.cs:151 | SKILL_CODE | publish guard tests `Section.Modality != "writing"` | same | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Studio.cs:106 | SKILL_CODE | `SkillCode: sec.Skill?.Code ?? sec.Code` — falls back to the *section* code when the skill nav is null | a section code (e.g. `P5`) is emitted as a skill code and groups the studio tree wrongly | WRONG_DISPLAY |
| src/Services/Admin/Exams/AdminExamService.Validation.cs:23 | SKILL_CODE | `SpeakingPractice` purpose ⇒ `sectionModality != "speaking"` is an error | redundant with the DB trigger; drifts if the trigger changes | COSMETIC |
| src/Services/Admin/Exams/AdminExamService.Validation.cs:28 | SKILL_CODE | `WritingPractice` purpose ⇒ modality must be `"writing"` | same | COSMETIC |
| src/Services/Admin/Generation/ExamGeneratorService.Ingest.cs:189 | OTHER | `section.OptionCount == 3` is the only signal that a question is `mcq_3` | a 2-option section ingests as `mcq` | WRONG_DISPLAY |
| src/Services/Admin/Generation/ExamGeneratorService.Prompts.cs:25 | CERT_NAME | system prompt enumerates "(TOEIC, IELTS, VSTEP)" as the universe of exams | a fourth certificate is authored by a model primed to imitate those three | WRONG_DISPLAY |
| src/Services/Admin/Generation/ExamGeneratorService.Prompts.cs:43 | SECTION_CODE | manifest JSON schema example hardcodes `"sectionCode": "P5"` (TOEIC Part 5) | the model copies `P5`-shaped section codes into manifests for non-TOEIC certs | WRONG_DISPLAY |
| src/Services/Admin/Generation/ExamGeneratorService.Prompts.cs:101 | BAND_CODE | default target band is `"B2"` (a CEFR code) | a non-CEFR cert is told to target `B2`, which its ScoreBands may not contain | WRONG_DISPLAY |
| src/Services/Admin/Generation/ExamGeneratorService.Prompts.cs:164 | OTHER | option labels derived as `OptionCount == 3 ? "A, B, C" : "A, B, C, D"` | OptionCount 2, 5 or 6 produces the wrong label set in the prompt | WRONG_DISPLAY |
| src/Services/Admin/Generation/ExamGeneratorService.Prompts.cs:181 | OTHER | question type derived as `OptionCount == 3 ? "mcq_3" : "mcq"` | 2-option sections are told to author `mcq` | WRONG_DISPLAY |
| src/Services/Admin/Imports/ExamQuickIngestService.BlueprintValidation.cs:55 | OTHER | `OptionCount == 3` ⇒ `mcq_3` is the expected type | 2-option sections mis-validate | WRONG_DISPLAY |
| src/Services/Admin/Imports/ExamQuickIngestService.Questions.cs:369 | OTHER | `if (section.OptionCount == 3) return "mcq_3";` | 2-option sections classify as `mcq` | WRONG_DISPLAY |
| src/Services/Admin/Imports/ExamQuickIngestService.Templates.cs:55 | CERT_NAME | comment anchors the free-response branch to "IELTS Writing Task 2, Speaking Part 2" | documentation only; drifts as certs are added | COSMETIC |
| src/Services/Admin/Imports/ExamQuickIngestService.Templates.cs:56 | SKILL_CODE | sample template chosen by `Modality == "writing"` / `"speaking"` | a free-response section on another modality falls through to the MCQ template | WRONG_DISPLAY |
| src/Services/Admin/Imports/ExamQuickIngestService.Templates.cs:99 | CERT_NAME | comment anchors the 3-option branch to "TOEIC Part 2 Q-and-R" | documentation only | COSMETIC |
| src/Services/Admin/Imports/ExamQuickIngestService.Templates.cs:100 | OTHER | `OptionCount == 3 \|\| HideOptionText` ⇒ the 3-option audio template | a 4-option section with hidden text gets a 3-option sample | WRONG_DISPLAY |
| src/Services/Admin/Imports/ExamQuickIngestService.Templates.cs:125 | OTHER | sample response window 45 s (speaking) / 1200 s (writing) | sample does not match cert policy | COSMETIC |
| src/Services/Admin/Imports/QuestionImportService.QuestionValidation.cs:85 | SKILL_CODE | free-response answers are rejected unless modality is `"speaking"` or `"writing"` | a cert that wants free_response in reading is blocked at import | BLOCKER |
| src/Services/Admin/Imports/QuestionImportService.QuestionValidation.cs:92 | SKILL_CODE | keyword/accepted-answer questions are rejected unless modality is `"listening"` or `"reading"` | free-response-style certs cannot import short-answer items | BLOCKER |
| src/Services/Admin/Imports/QuestionImportService.Templates.cs:42 | SKILL_CODE | default question type = `free_response` iff modality is speaking/writing, else `mcq` | cert using free_response elsewhere gets MCQ defaults | WRONG_DISPLAY |
| src/Services/Admin/Imports/QuestionImportService.Templates.cs:107 | SKILL_CODE | the free-response template is gated on speaking/writing | same | WRONG_DISPLAY |
| src/Services/Admin/Imports/QuestionImportService.Templates.cs:125 | OTHER | `ResponseSeconds = speaking ? 45 : 1200` | wrong default timing | COSMETIC |
| src/Services/Admin/Imports/QuestionImportService.Templates.cs:126 | OTHER | `MinWords = writing ? 150 : null` | COSMETIC |
| src/Services/Admin/Imports/QuestionImportService.Templates.cs:127 | OTHER | `MaxWords = writing ? 250 : null` (differs from AdminExamService.Groups.cs:263's 300) | two code paths seed different caps for the same concept | COSMETIC |
| src/Services/Admin/Lessons/AdminLessonService.Export.cs:63 | SKILL_CODE | the lesson import template hardcodes skill `"reading"` | the template references a skill the target cert may not define | COSMETIC |
| src/Services/AiGrader.cs:50 | OTHER | rubric criteria identified by substring matches on codes `PRON`/`INT`/`GRAM`/`LR`/`REL` and on English criterion names | a cert whose GradingCriterion codes/names don't contain those tokens gets every criterion scored by the `else` factor | WRONG_DISPLAY |
| src/Services/AiGrader.cs:55 | OTHER | `code.Contains("INT")` — substring, not equality | codes such as `PRINT`/`POINT`/`MAINT` match the intonation branch | WRONG_DISPLAY |
| src/Services/Attempts/AttemptEngine.Preflight.cs:159 | SKILL_CODE | microphone required when section modality is `"speaking"` | a cert recording speaking under another modality never prompts for mic | WRONG_DISPLAY |
| src/Services/Attempts/AttemptEngine.Preflight.cs:161 | SKILL_CODE | `s.Skill?.Code == "speaking"` — compares a skill *code* to a modality *name* | dead check: seed codes are `SPEA`, so this never fires | COSMETIC |
| src/Services/Attempts/AttemptEngine.Submission.cs:442 | OTHER | free-response skill aggregation has exactly two branches: `avg_half_step`, everything else | `independent` and `avg_whole` both collapse to integer rounding away from zero | WRONG_DISPLAY |
| src/Services/Catalog/CertificateService.Blueprint.cs:71 | SKILL_CODE | `EnumCodes.TryParse<Modality>(...)` failure silently yields `ModalityDto.Listening` | any unrecognised modality is displayed and edited as Listening | WRONG_DISPLAY |
| src/Services/Catalog/CertificateService.Wizard.cs:29 | CLOSED_ENUM | write path re-lists the four aggregation rules as a hardcoded allowlist in C# | adding a rule to the schema/enum still rejects it here | BLOCKER |
| src/Services/Claude/ClaudeAiGrader.cs:68 | CERT_NAME | system prompt: "certified official English exam evaluator (IELTS, TOEIC, VSTEP)" | grading a new cert's rubric is framed around three named exams | WRONG_DISPLAY |
| src/Services/Claude/ClaudeAiGrader.cs:102 | OTHER | a criterion the model omitted is filled with `MaxScore * 0.7m` "Standard performance." | missing rubric scores are invented at a fixed 70% | WRONG_DISPLAY |
| src/Services/GuidedLearning/LearningPathService.Generation.cs:63 | BAND_CODE | starting band falls back to the literal `"A1"` | a cert without CEFR-named bands generates a path whose `FromBandCode = "A1"` matches no PathTemplate | BLOCKER |
| src/Services/GuidedLearning/LearningPathService.Generation.cs:77 | BAND_CODE | target band falls back to the literal `"B2"` | same, for `ToBandCode` | BLOCKER |
| src/Services/LearnerService.Mission.cs:48 | SKILL_CODE | "receptive" sections are exactly `Modality is "listening" or "reading"` | a cert with only productive skills silently falls back to *all* sections | WRONG_DISPLAY |
| src/Services/LearnerService.Simulator.cs:62 | SKILL_CODE | participating skills forced in when `Modality == Listening` or `Reading` | the simulated total for a speaking-only cert includes skills it never scores | WRONG_DISPLAY |
| src/Services/LearnerService.Simulator.cs:97 | OTHER | `step = 5` when every scaled value is a multiple of 5 — a TOEIC-shaped heuristic | certs scored on a 0–9 or 0–10 scale get a step inferred from data shape | COSMETIC |
| src/Services/Mentors/MentorService.Catalog.cs:28 | OTHER | sentinel skill filter value `"all"` | a cert legitimately having a skill whose modality is "all" can never be filtered | COSMETIC |
| src/Services/Practice/PracticeService.Catalog.cs:32 | SKILL_CODE | LINQ predicate `q.QuestionType != "writing" && q.QuestionType != "speaking"` compares a *question type* to *skill* names | dead predicate — never excludes anything; signals vocabulary confusion | WRONG_DISPLAY |
| src/Services/Practice/PracticeService.Catalog.cs:69 | SKILL_CODE | practice sections bucketed into a `Listening` group by modality | new modality lands in "other" | WRONG_DISPLAY |
| src/Services/Practice/PracticeService.Catalog.cs:70 | SKILL_CODE | `other = items.Where(i => i.Modality is not (Listening or Reading))` | same | WRONG_DISPLAY |
| src/Services/Practice/PracticeService.Catalog.cs:74 | SKILL_CODE | "weakest part" only considers listening/reading sections | a cert practising only writing never gets a weakest part | WRONG_DISPLAY |
| src/Services/Practice/PracticeService.Lifecycle.cs:31 | SKILL_CODE | same `QuestionType != "writing" && != "speaking"` predicate on session start | dead predicate; if it ever became live it would drop valid questions | WRONG_DISPLAY |
| src/Services/SpeakingPracticeService.cs:48 | SKILL_CODE | the speaking instructions screen lists only sections with `Modality == Speaking` | a cert without a speaking skill shows an empty instructions page rather than "not offered" | WRONG_DISPLAY |
| src/Services/SpeakingPracticeService.cs:642 | SCORE_SCALE | `scaledScore = ratio × 200` — hardcoded TOEIC Speaking 0–200 scale | IELTS speaking (0–9) and VSTEP (0–10) report a 0–200 number | BLOCKER |
| src/Services/SpeechSuper/SpeechSuperAiGrader.cs:76 | SCORE_SCALE | missing pronunciation score defaults to `6.0m` — an IELTS band midpoint | a silent provider gap becomes 67% of max score on every cert | WRONG_DISPLAY |
| src/Services/SpeechSuper/SpeechSuperAiGrader.cs:77 | SCORE_SCALE | every criterion ratio is `score / 9.0m` (IELTS band denominator; repeated at 83, 89, 95, 107) | any cert not scored on a 0–9 band is graded with a wrong ratio | BLOCKER |
| src/Services/SpeechSuper/SpeechSuperAiGrader.cs:100 | SCORE_SCALE | relevance defaults to `80m` and divides by `100m`, mixing a 0–100 scale into otherwise 0–9 criteria | the relevance criterion is weighted on a different scale than its siblings | WRONG_DISPLAY |
| src/Services/SpeechSuper/SpeechSuperClient.cs:108 | CERT_NAME | provider request pins `test_type = "ielts"` for every speaking assessment | TOEIC Speaking and VSTEP answers are scored against IELTS norms | BLOCKER |
| src/Services/SpeechSuper/SpeechSuperClient.cs:109 | CERT_NAME | `task_type = "ielts_part1"` for every task | cue-card and opinion tasks are scored as IELTS Part 1 | WRONG_DISPLAY |
| src/Services/WritingPracticeService.cs:51 | SKILL_CODE | the writing instructions screen lists only sections with `Modality == Writing` | a cert without a writing skill shows an empty page | WRONG_DISPLAY |
| src/BusinessObjects/Catalog/Modality.cs:10 | CLOSED_ENUM | skill/section modality is exactly `listening, reading, speaking, writing` | a new cert cannot introduce a modality (e.g. "grammar", "integrated") without a migration | BLOCKER |
| src/Contracts/Catalog/ModalityDto.cs:11 | CLOSED_ENUM | the same four values mirrored on the wire | API surface cannot express a new modality | BLOCKER |
| src/BusinessObjects/Catalog/QuestionType.cs:5 | CLOSED_ENUM | question types are exactly `mcq, mcq_3, multi_select, inline_completion, dropdown, drag_drop, free_response` | a cert needing a new interaction (matching, ordering) is unsupported end to end | WRONG_DISPLAY |
| src/BusinessObjects/Catalog/ExamPurpose.cs:5 | CLOSED_ENUM | exam purposes are exactly `mock, placement, speaking_practice, writing_practice` | a new purpose (diagnostic, homework) requires enum + CHECK + trigger edits | WRONG_DISPLAY |
| src/BusinessObjects/Catalog/ScoreAggregationRule.cs:5 | CLOSED_ENUM | aggregation rules are exactly `sum, avg_half_step, avg_whole, independent` | a cert needing weighted-per-section aggregation cannot be configured | WRONG_DISPLAY |
| src/BusinessObjects/Catalog/NavigationMode.cs:5 | CLOSED_ENUM | five navigation modes only | new pacing strategy needs code + schema change | COSMETIC |
| src/BusinessObjects/Catalog/LayoutType.cs:5 | CLOSED_ENUM | four layout types (`StandardVertical, SplitHorizontal, MediaCard, Cluster3`) | new layout needs code + DTO change | COSMETIC |
| src/BusinessObjects/Catalog/DeliveryMode.cs:5 | CLOSED_ENUM | `strict_exam, practice` only | COSMETIC |
| src/BusinessObjects/Catalog/ExamStatus.cs:5 | CLOSED_ENUM | `draft, published, hidden` only | COSMETIC |
| docs/database-schema.sql:404 | CLOSED_ENUM | `PracticeSessions.Modality` permits only `listening, reading` (CK_Practice_Modality) plus a composite FK pinning it to the section's modality | free practice is impossible for speaking- or writing-only certificates | BLOCKER |
| docs/database-schema.sql:369 | CLOSED_ENUM | trigger `trg_QGroups_SkillPracticeOneModality`: `speaking_practice` exams may hold only speaking sections, `writing_practice` only writing | any new single-skill purpose has no trigger arm and is unconstrained | WRONG_DISPLAY |
| docs/database-schema.sql:1651 | CLOSED_ENUM | trigger `trg_CreditTransactions_ChargeMatchesExamPurpose` hardcodes `mock` / `placement` purpose names in the billing rule | a new purpose gets no defined charge behaviour | COSMETIC |
| docs/database-schema.sql:587 | CLOSED_ENUM | trigger `trg_Attempts_NoBandForSpeakingPractice` forbids BandCode/TotalScore for purposes `speaking_practice, writing_practice` | a new free-response purpose would be allowed to carry a certificate band | WRONG_DISPLAY |

## Closed vocabularies (cannot be changed by data)

| concept | permitted values | enforced where |
|---|---|---|
| Certificates.ScoreAggregationRule | `sum`, `avg_half_step`, `avg_whole`, `independent` | DB CHECK `CK_Certificates_ScoreAggregationRule` (schema:15) + EF default `"sum"` (CompanionDbContext.Authoring.cs:18) + entity default (Certificate.cs:28) + C# enum + duplicate C# allowlist (CertificateService.Wizard.cs:29) + SQL trigger CASE (schema:614-660) |
| Skills.Modality, Sections.Modality | `listening`, `reading`, `speaking`, `writing` | DB CHECK `CK_Skills_Modality` (schema:26) and `CK_Sections_Modality` (schema:43) + C# enum Modality (Modality.cs:10) + wire mirror ModalityDto (ModalityDto.cs:13-16) + composite FKs `FK_Sections_SkillModality` |
| PracticeSessions.Modality | `listening`, `reading` only | DB CHECK `CK_Practice_Modality` (schema:404) + FK `FK_Practice_SectionModality` (schema:420). No C# enum — carried as string. |
| Exams.Purpose | `mock`, `placement`, `speaking_practice`, `writing_practice` | DB CHECK `CK_Exams_Purpose` (schema:187) + EF default `"mock"` (Authoring.cs:137) + C# enum + trigger `trg_CreditTransactions_ChargeMatchesExamPurpose` (schema:1651) |
| Exams.Status | `draft`, `published`, `hidden` | DB CHECK `CK_Exams_Status` (schema:182) + EF default `"draft"` (Authoring.cs:140) + C# enum |
| Exams.DeliveryMode | `strict_exam`, `practice` | DB CHECK `CK_Exams_DeliveryMode` (schema:189) + C# enum |
| Questions.QuestionType | `mcq`, `mcq_3`, `multi_select`, `inline_completion`, `dropdown`, `drag_drop`, `free_response` | DB CHECK `CK_Questions_Type` (schema:281) + C# enum + per-section `AllowedComponentTypesJson` filter |
| Sections.NavigationMode | `FULL_SANDBOX`, `UNIT_LOCKED_SANDBOX`, `AUDIO_DRIVEN`, `SYSTEM_PACED_AUTO`, `HYBRID_GROUP_LOCKED` | DB CHECK `CK_Sections_NavigationMode` (schema:48) + default + C# enum + wire mirror |
| Sections.LayoutType | `STANDARD_VERTICAL`, `SPLIT_HORIZONTAL`, `MEDIA_CARD`, `CLUSTER3` | **C# enum only** — schema:50 declares a default but **no CHECK constraint**, so the DB accepts any string |
| Sections.OptionCount | 2–6 | DB CHECK `CK_Sections_OptionCount` (schema:52); every code path that reads it (Groups.cs:279, Prompts.cs:164, Ingest.cs:189, Questions.cs:369, Templates.cs:100/112) assumes only 3 or 4 |
| Single-skill exam binding | `speaking_practice` ⇒ speaking sections only; `writing_practice` ⇒ writing only | trigger `trg_QGroups_SkillPracticeOneModality` (schema:369-394) **and** C# re-implementations at AdminExamService.Validation.cs:23/28 and QuestionImportService.BlueprintValidation.cs:60-68 |
| Free-response attempt shape | attempts with purpose `speaking_practice`/`writing_practice` may carry neither `BandCode` nor `TotalScore` | trigger `trg_Attempts_NoBandForSpeakingPractice` (schema:587-601) |
| TotalScore derivation | `sum` / `avg_half_step` / `avg_whole` / `independent` | trigger `trg_Attempts_TotalMatchesSkills` (schema:604-659) + re-derived in C# at AttemptEngine.Submission.cs:442 |
| PathModuleItems.ItemType | `practice_section`, `mock_exam`, `ai_task`, `mentor`, `lesson` | DB CHECK `CK_PMI_ItemType` (schema:1217) + ref-matches-type CHECK (schema:1222) + trigger `trg_PMI_AiTaskRefsSkillPracticeExam` (schema:1143) |
| PracticeSource / FeedbackMode | `all`, `wrong_before`, `unseen` / `immediate`, `end` | DB CHECK (schema:411, 413) + C# enums |

## Ghi chú ngoài lề

`PracticeService.Catalog.cs:32` và `PracticeService.Lifecycle.cs:31` so sánh `QuestionType` với các literal `"writing"` / `"speaking"` — hai giá trị này **không bao giờ** khớp một question type nào, nên bộ lọc **không làm gì cả**. Đây là bug tiềm ẩn, không chỉ là vấn đề portability.
