# Backend fixes requested by the mobile client port

Three defects found while porting the Flutter learner app against this backend. All were
reproduced live against the running stack (`docker compose`, API on `:5001`, SQL Server on
`:1433`) on the demo seed. **None are caused by the mobile client** — the app surfaces each as a
reported failure rather than a crash, but each is a genuine backend bug.

Line references are against `integ/react-heroui-full-port` at the time of writing. Verify
before editing; that branch is moving.

---

## 1. `GET /api/learner/exams/history` and `GET /api/learner/exams` return 500 permanently, and get worse on every retry

**Severity: high.** A learner is locked out of the EXAMS tab for good.

### Reproduce

The failure is state-dependent, so it needs an expired skill-practice attempt to exist first.

**Current state of the demo seed: NOT reproducing**, because I cancelled the poisoned attempt by
hand during the client port (see "Manual unblock" below). `GET /api/learner/exams/history`
currently returns `200`. To reproduce:

1. Start a speaking- or writing-practice attempt and let it pass `ExpiresAt` while still
   `in_progress`. (Or set a row directly:
   `UPDATE ExamAttempts SET Status='in_progress', ExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME())
    WHERE AttemptId=<a skill-practice attempt>`.)
2. Then:
   ```
   POST /api/auth/sign-in  {"email":"learner@demo.local","password":"Demo1234!"}
   GET  /api/learner/exams/history        -> 500 {"code":"UNEXPECTED"}
   GET  /api/learner/exams                -> 500 {"code":"UNEXPECTED"}
   ```

`learner.fresh@demo.local` returns 200 both before and after, because it has no skill-practice
attempt — so the bug is data-dependent, not request-dependent.

Observed when it fired: attempting `attemptId 1006` (purpose `speaking_practice`, expired
`2026-11-13T18:08:05Z`, `TotalScore NULL`, `status in_progress`).

### Root cause

A self-perpetuating loop:

1. `MockExamService.GetExamHistoryAsync` calls
   `AttemptEngine.AutoSubmitExpiredAttemptsAsync(userId, certId, ct)` **on every read**.
   (`src/Services/MockExamService.cs`)
2. That method selects *all* expired in-progress attempts for the learner — with **no filter on
   exam purpose**:
   ```csharp
   // src/Services/Attempts/AttemptEngine.Lifecycle.cs  ~L230
   var expiredAttempts = await db.ExamAttempts
       .Where(a => a.UserId == userId && a.CertId == certId
                && a.Status == AttemptStatus.InProgress.ToCode()
                && DateTimeOffset.UtcNow >= a.ExpiresAt)
       .Select(a => a.AttemptId)
       .ToListAsync(cancellationToken);

   foreach (var expiredId in expiredAttempts)
       await SubmitAttemptAsync(expiredId, userId, isAutoSubmit: true, cancellationToken);
   ```
3. `SubmitAttemptAsync` writes `attempt.TotalScore` unconditionally:
   ```csharp
   // src/Services/Attempts/AttemptEngine.Submission.cs  ~L288
   attempt.TotalScore = totalScore;
   ```
4. The DB trigger refuses that for a skill-practice attempt:
   ```sql
   -- docs/database-schema.sql:2215
   CREATE TRIGGER trg_Attempts_NoTotalScoreForSkillPractice
   ON ExamAttempts AFTER INSERT, UPDATE AS
   ...
   WHERE e.Purpose IN ('speaking_practice','writing_practice') AND i.TotalScore IS NOT NULL
   ...
   THROW 50041, 'A speaking- or writing-practice attempt cannot carry a total score.', 1;
   ```
   → `SqlException 50041` → 500.
5. The throw rolls the transaction back, so the attempt **stays `in_progress`**. The next read
   selects it again and fails identically. It never recovers on its own.

Note `SpeakingPracticeService.cs:499` and `WritingPracticeService.cs:429` both set
`attempt.TotalScore = null` with the comment `trg_Attempts_NoTotalScoreForSkillPractice (50041)`
— they know the rule. The generic submit path does not.

### Proposed fix

Either is sufficient; the first is smaller and safer.

**Option A (recommended)** — Exclude skill-practice attempts from the sweep, because a
skill-practice attempt is submitted by its own service, not by the mock-exam lifecycle:

```csharp
var expiredAttempts = await db.ExamAttempts
    .Where(a => a.UserId == userId && a.CertId == certId
             && a.Status == AttemptStatus.InProgress.ToCode()
             && DateTimeOffset.UtcNow >= a.ExpiresAt
             && !SkillPracticePurposes.Contains(a.Exam.Purpose))
    .Select(a => a.AttemptId)
    .ToListAsync(cancellationToken);
```

**Option B** — In `SubmitAttemptAsync`, derive the purpose and leave the total null:

```csharp
var isSkillPractice = attempt.Exam.Purpose is "speaking_practice" or "writing_practice";
attempt.TotalScore = isSkillPractice ? null : totalScore;
```

**Do both if cheap.** Option B is defence in depth: any future caller that submits a
skill-practice attempt through the generic path would reintroduce this.

### Related, worth fixing at the same time

`GET /api/learner/exams` fails for the same reason — it too runs the sweep. Any other read that
calls `AutoSubmitExpiredAttemptsAsync` inherits the bug; consider making the sweep itself refuse
to touch skill-practice attempts rather than fixing each caller.

### Manual unblock (for a demo, not a fix)

```bash
curl -X POST http://localhost:5001/api/learner/attempts/{id}/cancel \
     -H "Authorization: Bearer $TOKEN"
```

Cancelling moves the attempt out of `in_progress`, so the sweep no longer selects it. A reseed
that leaves a skill-practice attempt to expire will bring the 500 straight back.

---

## 2. `POST /api/learner/practice/sessions` returns 500 for sections W1 and W2

**Severity: high.** Two writing sections cannot be drilled at all.

**Currently reproducing — verified just now.**

### Reproduce

```
GET  /api/learner/practice/catalog          # note W1 = sectionId 13, W2 = sectionId 14
POST /api/learner/practice/sessions {"sectionId": 13}  -> 500 {"code":"UNEXPECTED"}
POST /api/learner/practice/sessions {"sectionId": 14}  -> 500 {"code":"UNEXPECTED"}
POST /api/learner/practice/sessions {"sectionId": 5}   -> 200   (P5 works)
```

For contrast, `sectionId 15` (W3) returns a legitimate `404 PRACTICE_NO_QUESTIONS_AVAILABLE`,
and S1–S5 return a legitimate `400 PRACTICE_MODALITY_NOT_ALLOWED`.

### Root cause

`SqlException 50041`-style unique-index violation:

```
Cannot insert duplicate key row in object 'dbo.PracticeAnswers'
with unique index 'UQ_PracticeAnswers_Version'. The duplicate key value is (1007, 40).
   at Services.PracticeService.StartSectionSessionAsync(...) PracticeService.Lifecycle.cs:line 199
```

The index is:

```sql
-- docs/database-schema.sql
CREATE UNIQUE INDEX UQ_PracticeAnswers_Version ON PracticeAnswers(SessionId, ItemVersionId)
WHERE ItemVersionId IS NOT NULL;
```

So one session may hold each item version **once**. But `StartSectionSessionAsync` inserts one
`PracticeAnswer` per element of the plan, and the plan can contain the same item twice:

```csharp
// src/Services/Practice/PracticeService.Lifecycle.cs  ~L186-200
for (var i = 0; i < plan.Count; i++)
{
    db.PracticeAnswers.Add(new PracticeAnswer
    {
        SessionId = session.SessionId,
        ItemId = plan[i],
        ItemVersionId = versionByItem[plan[i]],   // same ItemId => same version
        ...
    });
}
```

The duplicate comes from the candidate query having no `DISTINCT`, so a section that binds one
item twice yields it twice:

```csharp
// src/Services/Practice/PracticeService.Catalog.cs  — LoadSectionCandidatesAsync
var rows = await db.ExamItems
    .AsNoTracking()
    .Where(ei => ei.CertId == certId && ei.SectionId == sectionId)
    .Join(db.Items.AsNoTracking(), ei => ei.ItemId, i => i.ItemId, (ei, i) => new { ei, i })
    .Where(x => x.i.Modality != SpeakingCode && x.i.LifecycleStatus == ItemSelector.ActiveLifecycle)
    .Select(x => new { x.i.ItemId, x.i.TopicId, x.i.CefrLevel })
    .ToListAsync(cancellationToken);       // <-- no Distinct; ExamItems can bind an item twice
```

### Evidence that this is seed data + missing dedupe

```sql
SELECT SectionId, ItemId, COUNT(*) FROM ExamItems
WHERE SectionId IN (13,14) GROUP BY SectionId, ItemId HAVING COUNT(*) > 1;
-- SectionId 13, ItemId 39, 2
-- SectionId 14, ItemId 40, 2
```

Five `(SectionId, ItemId)` groups are duplicated across the whole seed. W1/W2 are simply the two
that are also *active and non-speaking*, so they are the two that reach the insert.

Both sides deserve attention:

- **The query should be duplicate-proof.** `DISTINCT` on the projection (or `GroupBy` on
  `ItemId`) makes the plan's inputs unique regardless of what the item bank binds.
- **The seed should probably not bind an item twice to one section.** If duplicate bindings are
  legitimate (e.g. the same passage appearing in two question groups), then the plan needs to
  dedupe *after* grouping per section, not in the candidate query. Decide which invariant is
  intended — the `UQ_PracticeAnswers_Version` index implies "one answer per version per session",
  so duplicates can never be legitimate for a single drill.

### Suggested minimal fix

```csharp
var rows = await db.ExamItems
    .AsNoTracking()
    .Where(ei => ei.CertId == certId && ei.SectionId == sectionId)
    .Join(db.Items.AsNoTracking(), ei => ei.ItemId, i => i.ItemId, (ei, i) => new { ei, i })
    .Where(x => x.i.Modality != SpeakingCode && x.i.LifecycleStatus == ItemSelector.ActiveLifecycle)
    .GroupBy(x => new { x.i.ItemId, x.i.TopicId, x.i.CefrLevel })
    .Select(g => new { g.Key.ItemId, g.Key.TopicId, g.Key.CefrLevel })
    .ToListAsync(cancellationToken);
```

Also consider clamping `plan` defensively before the insert loop, so no future path can violate
the index:

```csharp
var plan = (await PlanSmartPracticeAsync(...)).Distinct().ToList();
```

### Test the fix should have

A schema/plane test: bind one item twice into a section, start a session, assert `200` and that
the session holds that item exactly once. Today nothing covers it, which is why it reached the
seed.

---

## 3. No media route serves the paths the DTOs publish

**Severity: medium.** No audio can play anywhere in the client; the audio device check cannot
pass.

### Reproduce

Every one of these 404s:

```
GET /media/placement-listening-placeholder.wav        -> 404
GET /audio/toeic/placement-p1.mp3                     -> 404
GET /img/part1-sample.svg                             -> 404
GET /api/media/exams/1/test1-p1.mp3                   -> 404
```

### Root cause

Only two media routes are registered, both requiring auth and both restricted to generated
filenames:

```csharp
// src/WebAPI/Endpoints/AdminEndpoints.Media.cs
app.MapGet("/api/media/exams/{examId:int}/{fileName}", ...)
   .RequireAuthorization();

// src/WebAPI/Endpoints/AdminImportEndpoints.Media.cs
app.MapGet("/api/media/imports/{batchId:int}/{fileName}", ...)
```

`Contracts.MediaUrlHelper.NormalizeMediaUrl` only rewrites paths beginning `exams/` or
`imports/` into `/media/...`, and explicitly leaves `/audio/...` and `/images/...` untouched:

```csharp
if (trimmed.StartsWith("/media/") || trimmed.StartsWith("/audio/")
 || trimmed.StartsWith("/images/")) return trimmed;   // unchanged
```

But the values the DTOs actually carry are `/audio/toeic/...` and `/img/...`:

- `preflightStatus.audioUrls[]` → `/audio/toeic/placement-p1.mp3`
- `attemptQuestion.audioPath` → paths of that shape
- `practiceQuestion.audioPath` and `savedQuestion.audioPath` → same

So the client is handed URLs that no route answers, and `/img/` is not even in
`NormalizeMediaUrl`'s allow-list (it knows `/images/`).

### Options

1. **Serve the seed's media.** Put the demo files under the path the DTOs name and add the
   routes (`/audio/**`, `/img/**`), or a `UseStaticFiles` mount. Simplest for a demo build.
2. **Make the DTOs publish resolvable URLs.** Route `audioPath`/`imagePath` through
   `NormalizeMediaUrl` at serialization *and* store demo media under `exams/{examId}/` so the
   existing route serves it. Then `/api/media/exams/{id}/{file}` works — but note it currently
   requires authorization, which is wrong for a public media asset the client plays with a
   plain URL.
3. **Fix `/img/` in `NormalizeMediaUrl`** if `/images/` is the intended prefix — the mismatch
   means image paths are never normalised.

Whichever you pick, the front-end contract is "the URL in the payload is fetchable without
special handling". Right now it is not, and the mobile client cannot work around it — it has no
way to know which of four possible prefixes the backend meant.

### How the client degrades today (for context)

The audio preflight check removes its hardcoded fallback and, when the backend supplies no
playable sample, asks the learner to confirm they can hear rather than blocking entry. Listening
and saved-question audio simply cannot play. Once a real URL is served, no client change is
needed — it already plays whatever the payload names.

---

## Appendix — what is *not* a bug

Checked and behaving correctly; listed so nobody re-investigates:

| Observation | Verdict |
|---|---|
| `GET /api/learner/insights/path` → 404 `LEARNING_PATH_NOT_FOUND` | **By design.** No active path means no path insight. |
| `GET /api/learner/placement/take/{id}/resume` → 400 `placement_not_active` on a finished sitting | **By design**, and the client treats it as terminal. |
| `POST /practice/sessions` for S1–S5 → 400 `PRACTICE_MODALITY_NOT_ALLOWED` | **By design.** Speaking has no typed drill. |
| Section W3 → 404 `PRACTICE_NO_QUESTIONS_AVAILABLE` | **By design.** Zero active items. |
| `GET /api/learner/lessons/cell?level=Z9` → 500 `UNEXPECTED` | Arguably should be 400 — an unvalidated level string reaches the query. Low priority, noted only. |
| `saved-questions?sectionId=&categoryId=` ignored | Facets are returned but neither filter applies. `?source=` does work. The client filters locally. |

## Appendix — verification I ran

- Reproduced §2 by starting sessions on every section (1–7, 13–15) and capturing the SQL
  exception from `docker logs extra-efficiency-api-1`. It fails now, on the current seed.
- Confirmed §2's duplicate bindings with the `ExamItems` query above
  (`SectionId 13/ItemId 39` and `SectionId 14/ItemId 40`, two bindings each).
- §3: all four candidate media paths 404, re-checked at the time of writing.
- §1: **not currently reproducible**, because I cancelled the poisoned attempt
  (`attemptId 1006`) to unblock the client. The chain is proven from the stack trace and the
  trigger definition, and the failure was observed live before the cancel; the reproduction
  recipe above recreates it. Please re-verify before and after your fix.
- Both demo accounts signed in, every learner GET endpoint swept (36 probes): one designed 404,
  zero 5xx in the current state.
- 14 bad-id / invalid-input probes (`attempts/999999`, `placement/take/999999`,
  `sessions/999999/take`, `mentors/999999`, …): all returned proper 404/400 ProblemDetails, no 5xx.

The mobile-side audit log lives in the client repo at `.audit-log.md` if you want the full list of
symptoms these three produce.
