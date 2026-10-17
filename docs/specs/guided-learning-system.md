# Guided Learning System: Interactive Lesson Walkthroughs

## Problem Statement

Learners with low proficiency (A1-A2) who take the placement test and enter a learning path encounter a pedagogical gap: the platform offers extensive diagnostic testing (Practice by Section, Mock Exams, Weakness Analysis) but provides no actual instructional content — no grammar rules, no test strategies, no guided explanations. A beginner placed into an open-choice question bank experiences cognitive overload: they drill questions without understanding the underlying concepts, making the same errors repeatedly with no framework to correct them. The product diagnoses weakness but does not teach how to address it.

## Solution

Introduce **Lessons** as a first-class learning activity within PathModules, accessible both through linear guided learning paths and as contextual remediation from Weakness Analysis. Each Lesson is an interactive walkthrough deck of 4 card types:
- **rule**: Concise grammar formula or concept placard
- **pitfall**: Common exam traps and distraction patterns (warning visual treatment)
- **example**: Real exam specimen with keyword highlights
- **micro_check**: Single formative checkpoint question with immediate pedagogical explanation (not scored for exam results)

Lessons are tagged with a CategoryId (QuestionCategories), enabling direct remediation links from weakness analysis and practice errors. The diagnostic loop (placement, practice, weakness analysis) remains strictly free; guided lessons are unlockable as premium_path for 5 credits via the existing CreditTransactions ledger.

## User Stories

1. As a beginner learner placed at A1, I want to learn grammar rules before drilling questions, so that I understand why an answer is correct instead of guessing.
2. As a learner viewing weakness analysis, I want to click directly to the lesson explaining a concept I'm weak in, so that I can remediate immediately without searching.
3. As a learner on a guided learning path, I want to progress linearly through Lesson → Practice → AI Task, so that I build understanding step-by-step.
4. As a learner viewing a lesson, I want to see a concise rule card first, so that I grasp the core concept before details.
5. As a learner, I want to see common pitfalls highlighted in warning colors, so that I recognize exam traps before encountering them in real questions.
6. As a learner, I want to see a worked example with keyword highlights, so that I can apply the rule to real question patterns.
7. As a learner, I want a micro-check question with immediate explanation, so that I can self-assess understanding before moving to timed practice.
8. As a learner, I want to see my progress through a lesson (3 of 4 cards viewed), so that I know how much content remains.
9. As a learner, I want to resume a lesson from the last card I viewed, so that I don't lose my place across sessions.
10. As a learner, I want to complete a lesson without it affecting my mock exam score or band, so that formative learning stays separate from summative assessment.
11. As a learner, I want to see which lessons are free and which are premium, so that I can decide whether to unlock a learning path.
12. As a learner, I want to spend 5 credits once to unlock all lessons in a learning path, so that I pay per path, not per lesson.
13. As a learner, I want to see estimated completion time for each lesson (5-10 minutes), so that I can fit learning into my schedule.
14. As a learner who just failed a practice question in Part 5 grammar, I want a direct link to the lesson on that specific CategoryId, so that I remediate the exact weakness.
15. As a learner, I want the lesson interface to follow the same exam-hall signage design system as the rest of the app, so that the experience is consistent.
16. As a learner, I want lesson cards to appear one at a time with clear progression controls, so that I focus on one concept without distraction.
17. As a learner, I want my answer to a micro-check question to be validated immediately with explanation, so that I get instant feedback on whether I understood.
18. As a learner, I want to mark a lesson as complete only after viewing all cards, so that progress tracking is accurate.
19. As a learner, I want to see "Done" status on a completed lesson in my path, so that I know which modules I've already covered.
20. As a learner on a premium path, I want to access all lessons in the path without further credit charges, so that one unlock gives me full access.
21. As an admin, I want to author lessons with Markdown content for rule/pitfall/example cards, so that formatting is flexible without HTML knowledge.
22. As an admin, I want to tag each lesson with a SkillId and CategoryId, so that learners drilling a specific section weakness get the right remediation link.
23. As an admin, I want to specify DisplayOrder for lessons and cards, so that content appears in pedagogical sequence.
24. As an admin, I want to create path module items of type 'lesson', so that lessons fit into the same linear progression as practice and mock exams.
25. As an admin, I want to set a lesson's EstimatedMinutes, so that learners see realistic time commitments.
26. As an admin, I want to write a Summary for each lesson, so that learners preview what the lesson covers before opening it.
27. As an admin, I want to create multiple micro-check questions per lesson, so that assessment covers different facets of the concept.
28. As an admin, I want to provide four options (A/B/C/D) for each micro-check, so that the format mirrors real exam MCQs.
29. As an admin, I want to write Explanation for each micro-check answer, so that learners understand why the correct option is correct.
30. As an admin, I want to write Rationale for wrong options in micro-checks, so that learners see why distractors are traps.
31. As the system, I want to enforce CertId isolation on Lessons via composite foreign keys, so that a lesson never references questions or skills from another certificate.
32. As the system, I want to store LearnerLessonProgress independently per (UserId, LessonId), so that progress persists across sessions.
33. As the system, I want to track LastViewedCardId in progress, so that resume lands on the next unseen card.
34. As the system, I want to count CardsCompleted / TotalCards in progress, so that the UI shows accurate "3 of 4" stats.
35. As the system, I want to mark IsCompleted only when CardsCompleted equals TotalCards, so that partial views don't count as done.
36. As the system, I want to write CreditTransactions with Reason='premium_path' when a learner unlocks a path, so that ledger audit shows what the charge was for.
37. As the system, I want to deduct exactly 5 credits for premium_path unlock, so that pricing is fixed per the monetization model.
38. As the system, I want to prevent charging premium_path twice for the same LearnerPathId, so that a learner who already unlocked never pays again.
39. As the system, I want to allow free access to lessons on free learning paths, so that not all lessons require payment.
40. As the system, I want PathModuleItems.ItemType to include 'lesson' alongside 'practice_section', 'mock_exam', 'ai_task', 'mentor', so that lessons are first-class module steps.
41. As the system, I want PathModuleItems with ItemType='lesson' to reference Lessons via RefLessonId, so that each path step points to exactly one lesson.
42. As the system, I want to enforce that RefLessonId is non-NULL when ItemType='lesson' and NULL otherwise, so that the ref column matches the type.
43. As the system, I want to enforce that a lesson's SkillId matches its CertId via composite FK, so that cross-certificate references are impossible.
44. As the system, I want to enforce that a lesson card's (LessonId, CertId) matches its lesson's own, so that cards never belong to another certificate's lesson.
45. As the system, I want to timestamp FirstOpenedAt when a learner first views a lesson, so that engagement analytics are accurate.
46. As the system, I want to timestamp CompletedAt when IsCompleted becomes true, so that completion duration is measurable.
47. As the system, I want to update UpdatedAt on every progress change, so that stale sessions are detectable.
48. As the system, I want to allow a learner to re-view a completed lesson without resetting progress, so that review is always available.
49. As the system, I want lesson content (ContentMarkdown) to support basic Markdown (bold, italic, lists, code), so that pedagogical formatting is rich.
50. As the system, I want lesson cards to be served in DisplayOrder ascending, so that the pedagogical sequence is preserved.

## Implementation Decisions

### Schema Design

**Four new tables** to be added to `docs/database-schema.sql`:

1. **Lessons**: One row per lesson concept, scoped to (CertId, SkillId, CategoryId). Carries Title, Summary, EstimatedMinutes, DisplayOrder. Composite unique (LessonId, CertId) for M-1 containment. Foreign key to (SkillId, CertId) ensures lesson's skill belongs to its certificate. Optional FK to CategoryId (QuestionCategories) for weakness-remediation linking.

2. **LessonCards**: One row per card in a lesson's walkthrough deck. CardType enum ('rule', 'pitfall', 'example', 'micro_check'). Title and ContentMarkdown for display. QuestionPrompt, OptionA/B/C/D, CorrectOption, Explanation for micro_check cards only (all NULL for other card types). Rationale per option for wrong-answer feedback. DisplayOrder for sequencing. Composite FK (LessonId, CertId) to Lessons ensures card's certificate matches lesson's. Composite unique (CardId, CertId) for potential future containment.

3. **LearnerLessonProgress**: One row per (UserId, LessonId) tracking viewing state. LastViewedCardId references LessonCards(CardId). CardsCompleted / TotalCards for "3 of 4" display. IsCompleted bit derived from CardsCompleted = TotalCards. FirstOpenedAt, CompletedAt, UpdatedAt timestamps. Composite FK (LessonId, CertId) to Lessons. Unique (UserId, LessonId) so progress is singleton per learner-lesson pair.

4. **PathModuleItems extension**: Add RefLessonId INT NULL column. Extend CK_PMI_Type check to include 'lesson'. Extend CK_PMI_RefMatchesType to enforce: `(ItemType='lesson' AND RefLessonId IS NOT NULL AND RefSectionId IS NULL AND RefExamId IS NULL)`. Add composite FK (RefLessonId, CertId) to Lessons(LessonId, CertId) so lesson refs are certificate-isolated (MATCH SIMPLE, NULL stays legal). Maintain existing IsPremiumOnly bit for premium lessons.

**Composite key discipline**: Every table carrying CertId uses composite foreign keys to pin references to the same certificate. LessonCards cannot reference a Lesson from another certificate; PathModuleItems cannot reference a Lesson from another certificate. This mirrors the existing Skills/Sections/Questions pattern and satisfies the multi-certificate isolation invariant.

### Interactive Walkthrough Format

**Card progression**: Lessons render as a single-card-at-a-time interface. State machine: load lesson → fetch all cards in DisplayOrder → render card 1 → on "Next", increment CardsCompleted, save progress, render card 2 → repeat until final card → on "Complete", set IsCompleted=1, CompletedAt=NOW(). Navigation: "Next" button (enabled only after viewing current card content; for micro_check, enabled only after answering and viewing explanation). Optional "Previous" to review earlier cards without decrementing progress.

**Card type rendering**:
- `rule`: Title + ContentMarkdown in a primary-colored placard (sign-blue background, painted foreground). Concise 2-4 sentence formula.
- `pitfall`: Title + ContentMarkdown in a warning-colored placard (sign-gold background, warning-ink foreground). Lists common traps, bullets with ⚠️ visual prefix. Design system has no icon set, so use text labels ("TRAP:", "WRONG:") instead.
- `example`: Title + ContentMarkdown showing a real exam question with `<mark>` highlights for keywords. Inline explanation of why the correct answer applies the rule.
- `micro_check`: QuestionPrompt + 4 radio options (A/B/C/D) + "Check answer" button → on submit, show CorrectOption highlighted green, wrong options grey, Explanation for correct + Rationale for each wrong option inline. Not scored in ExamAttempts; purely formative.

**Design system adherence**: Follow DESIGN.md exam-hall signage system: `border-radius: 0`, `box-shadow: none`, 2px ink lines for borders, IBM Plex Sans Condensed for headings, no icons (text labels only). Pitfall cards use `sign-gold` background with `warning-ink` text. Rule cards use `sign-blue` background with `painted` text. Example and micro_check cards use default `painted` background with `ink` text.

### Monetization Integration

**Premium path unlock**: When a learner clicks "Unlock this path" on a learning path detail screen, charge 5 credits via `CreditTransactions.Reason='premium_path'`, `CreditTransactions.Amount=-5`, `CreditTransactions.RelatedPathId=LearnerPathId`. Use existing `ICreditLedger.ChargeAsync` pattern (see CreditLedgerTests for debit mechanics). Before charging, check if a transaction with `Reason='premium_path' AND RelatedPathId={id}` already exists for this user — if yes, skip charge and allow access (idempotent unlock). After successful charge, derive unlock state by querying CreditTransactions for existence of the premium_path charge (ledger is append-only truth).

**Free vs premium lessons**: PathModuleItems.IsPremiumOnly bit controls lesson paywall. Free paths have all items with IsPremiumOnly=0; premium paths have IsPremiumOnly=1 on lesson items. A learner who has not unlocked a premium path sees lesson items as locked with "Unlock path (5 credits)" prompt. After unlock, all lessons in that path become accessible.

**Credit cost display**: Path detail screen shows "Cost: Premium · 5 credits" if any item has IsPremiumOnly=1 and path is not yet unlocked. After unlock, shows "Cost: Unlocked · 0 credits" (no per-lesson charges). Weakness analysis remediation links check IsPremiumOnly on the target lesson; if premium and not unlocked, show "Unlock via learning path (5 credits)" interstitial before lesson content.

### Category-Based Remediation Linking

**Weakness analysis integration**: Existing weakness-analysis.html shows per-section and per-category accuracy. Add a "Learn this concept" link next to each weak CategoryId row. Link target: `lesson-view.html?lesson={LessonId}` where LessonId is looked up via `SELECT LessonId FROM Lessons WHERE CategoryId=@cid AND CertId=@certId LIMIT 1`. If multiple lessons exist for the same CategoryId, show the first by DisplayOrder. If no lesson exists for that CategoryId, omit the link (not all categories have lessons yet — content authoring is incremental).

**Practice session error linking**: After a learner submits a practice session and views the answer review, show a per-question link "Why did I get this wrong? → [Lesson title]" if Questions.CategoryId has a matching Lesson. Stores no state; link is rendered conditionally at view time. Clicking it navigates to lesson-view.html and records LearnerLessonProgress as usual.

**Path module progression**: When a LearnerPathStep references a PathModuleItem of ItemType='lesson', the step's Status progresses: locked → available (when prior step is done) → in_progress (first card viewed) → done (IsCompleted=1 in LearnerLessonProgress). The step's Score column is NULL for lessons (no numeric score; completion is binary). StartedAt = LearnerLessonProgress.FirstOpenedAt. CompletedAt = LearnerLessonProgress.CompletedAt.

### Prototype Screens

Three modifications in `prototype/student/`:

1. **lesson-view.html** (new): Interactive walkthrough deck. States: loading, locked (premium not unlocked), error, success. Success state renders one card at a time with progress indicator "Card 3 of 4". Card content area switches based on CardType (rule/pitfall/example/micro_check). Navigation: "Previous" / "Next" buttons (Previous disabled on card 1, Next becomes "Complete" on final card). Micro-check renders 4 radio options + "Check answer" button; on check, disables radios and shows explanation inline. Context bar shows: Lesson title, Skill, Progress (3 of 4 cards), Cost (Free / Premium unlocked / Premium locked with unlock prompt).

2. **path-detail.html update**: Extend existing module item list to include lesson items. Render as a card with icon-less label "LESSON", title from Lessons.Title, estimated time from Lessons.EstimatedMinutes, and status badge (Locked / Not started / In progress / Done). Locked state shows lock icon placeholder (text "LOCKED" per design system no-icon rule). Click opens lesson-view.html if available/in_progress/done; shows unlock modal if locked due to premium.

3. **weakness-analysis.html update**: Add "Learn this concept" link next to each weak CategoryId row in the per-category breakdown. Link styled as a secondary button or text link (not primary — keeps "Practice this section" as primary action). Link text: "Review the rule" or "See lesson". Opens lesson-view.html?lesson={LessonId}.

**Design system tokens**: All three screens use tokens.css variables (--color-sign-blue, --color-sign-gold, --color-ink, --font-headline, --font-body) from DESIGN.md. No custom colors or font families outside the token system.

### Service Layer Design

**LessonService** (new): Encapsulates lesson and progress operations.

- `GetLessonAsync(int lessonId, int certId)`: Fetch lesson + all cards in DisplayOrder. Returns LessonDetailDto (Title, Summary, SkillId, CategoryId, EstimatedMinutes, Cards[]). Throws if lesson does not belong to certId (certificate isolation).
- `GetProgressAsync(int userId, int lessonId)`: Fetch LearnerLessonProgress or return default (CardsCompleted=0, IsCompleted=false) if not started.
- `RecordCardViewAsync(int userId, int lessonId, int cardId)`: Increment CardsCompleted if this card is new (cardId > LastViewedCardId in display order), update LastViewedCardId. If CardsCompleted now equals TotalCards, set IsCompleted=1 and CompletedAt.
- `GetLessonByCategoryAsync(int categoryId, int certId)`: Return first lesson for this category, or null if none exists. Used by weakness analysis remediation links.

**PremiumPathService** (extend existing or new): Manage premium path unlock.

- `UnlockPathAsync(int userId, int learnerPathId)`: Check if already unlocked (query CreditTransactions for premium_path charge on this path). If not unlocked and user has ≥5 credits, charge 5 credits via ICreditLedger.ChargeAsync with Reason='premium_path'. Return success/failure. Throws if insufficient credits.
- `IsPathUnlockedAsync(int userId, int learnerPathId)`: Query CreditTransactions for existence of premium_path charge. Return bool. Used by UI to determine whether to show unlock prompt or lesson content.

**LearningPathService** (extend existing): Add lesson step progression.

- Existing `GetPathProgressAsync` should recognize ItemType='lesson' and join to LearnerLessonProgress to derive step status (locked/available/in_progress/done based on IsCompleted and prior step state).
- Existing `CompleteStepAsync` should handle lesson completion: when a lesson step is completed (IsCompleted=1 in LearnerLessonProgress), mark LearnerPathSteps.Status='done', set CompletedAt, unlock next step.

## Testing Decisions

### What Makes a Good Test

**External behavior, not implementation details**: Test that a learner can view a lesson, record progress, and complete it. Do not test internal DbContext query mechanics or specific EF Include paths. Test that premium unlock charges 5 credits and prevents double-charging. Do not test the SQL generated by the charge transaction.

**Service layer as seam**: Following existing test patterns (LearningPathServiceTests, CreditLedgerTests), test LessonService and PremiumPathService methods against LocalDB. Seed minimal fixtures (certificate, skill, section, lesson, cards) and assert on returned DTOs and database state after operations.

### Modules to Test

1. **LessonService**:
   - `GetLessonAsync` returns lesson with cards in DisplayOrder
   - `GetLessonAsync` throws if lesson does not belong to specified certId (certificate isolation)
   - `GetProgressAsync` returns default progress for new learner
   - `RecordCardViewAsync` increments CardsCompleted and updates LastViewedCardId
   - `RecordCardViewAsync` sets IsCompleted when all cards viewed
   - `RecordCardViewAsync` timestamps FirstOpenedAt on first view, CompletedAt on completion
   - `GetLessonByCategoryAsync` returns correct lesson for category or null if none

2. **PremiumPathService**:
   - `UnlockPathAsync` charges 5 credits and records premium_path transaction
   - `UnlockPathAsync` is idempotent (does not charge twice for same path)
   - `UnlockPathAsync` throws if user has insufficient credits
   - `IsPathUnlockedAsync` returns true after unlock, false before

3. **LearningPathService** (extend existing tests):
   - `GetPathProgressAsync` correctly derives lesson step status from LearnerLessonProgress
   - `CompleteStepAsync` marks lesson step done when IsCompleted=1 in progress

### Prior Art

- **LearningPathServiceTests.cs**: Seeds path fixtures with templates, modules, items, and steps. Uses `SeedPathFixtureAsync` helper to create certificate, skills, sections, exams, and path structure. Follow this pattern for seeding lessons.
- **CreditLedgerTests.cs**: Tests ledger charge/refund mechanics and idempotency. Follow this pattern for premium unlock charge tests.
- **DatabaseFixture.cs**: Manages LocalDB lifecycle and connection string per test collection. All lesson tests should use `[Collection(DatabaseCollection.Name)]` and inject `DatabaseFixture`.
- **DbContextFactory.cs**: Creates CompanionDbContext instances for tests. Use `DbContextFactory.Create(database.ConnectionString)` in each test method.

## Out of Scope

1. **Video lessons**: Schema includes PathModuleTopics.VideoPath for future premium video content, but video hosting, encoding, and playback are not part of this spec. Lessons are text-and-MCQ only.

2. **AI-generated lesson content**: Admin manually authors all lesson content. AI generation of rule/pitfall/example cards is a future enhancement.

3. **Adaptive difficulty within lessons**: All learners see the same 4-card deck for a given lesson. No branching based on micro-check answers.

4. **Lesson versioning**: Once a lesson is created, updates replace content in place. No version history or learner notification of content changes.

5. **Lesson search**: Learners access lessons only via learning paths or weakness analysis remediation links. No standalone lesson catalog or search screen.

6. **Lesson ratings or feedback**: No learner rating or "Was this helpful?" feedback mechanism. Future quality improvement feature.

7. **Lesson completion certificates**: Completing all lessons in a path does not generate a certificate or badge. Completion is tracked in LearnerPathSteps but not surfaced as a standalone achievement.

8. **Multi-language lessons**: All lesson content is Vietnamese/English as authored. No i18n or translation layer.

9. **Offline lesson access**: Lessons require online access to load content and record progress. No offline caching or download.

10. **Lesson prerequisites beyond path sequence**: Lessons unlock in path DisplayOrder. No DAG-based prerequisite graph.

11. **Admin lesson authoring UI**: This spec covers learner-facing consumption only. Admin screens to create/edit Lessons and LessonCards are deferred to future admin feature work.

## Further Notes

### Schema Validation

After updating `docs/database-schema.sql`, run `docs/schema-tests/run-all.sh` to verify all composite foreign keys and check constraints are valid. Pay special attention to:
- CK_PMI_Type includes 'lesson'
- CK_PMI_RefMatchesType handles lesson case correctly
- CK_LessonCards_Type includes all four card types
- CK_LessonCards_Correct allows NULL (for non-micro_check cards)

Scaffold the new tables with `dotnet ef dbcontext scaffold` per CLAUDE.md instructions, ensuring each trigger (if any) has a `HasTrigger` in CompanionDbContext.

### Prototype State Switching

All three prototype screens use `data-states` attribute and state-switch.js for conditional rendering. Lesson-view.html needs states: loading, locked, error, success (with success further subdivided by card index via JS state, not HTML data-states). Follow the existing pattern in path-detail.html for locked/available/done state rendering.

### Micro-Check Not Scored

Critically: micro-check answers are purely formative and never written to ExamAttempts, PracticeAnswers, or any scoring table. They are ephemeral client-side validation. If a future requirement demands persistent tracking of micro-check performance, add a new LessonCardAnswers table; do not pollute the exam scoring tables with formative data.
