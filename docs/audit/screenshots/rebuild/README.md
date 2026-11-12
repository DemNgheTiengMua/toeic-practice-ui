# Browser pass — screenshots and what each one shows

Taken by `tests/WebMVC.E2ETests/Journeys/AuditScreenshotTests.cs` against
`scripts/crawl-env.sh fresh` (WebMVC :5000, WebAPI :5001, DB `ExtraEfficiencyCrawl`, payment=mock,
ai=fake). Reproducible: the same seed and the same steps, and a re-run overwrites each file.

    E2E_BASE_URL=http://localhost:5000 dotnet test tests/WebMVC.E2ETests \
      --filter "FullyQualifiedName~AuditScreenshotTests" --nologo

## Learner flow

| File | Screen | What it proves |
|---|---|---|
| `learner--01-sign-in.png` | `/auth/sign-in` | The sign-in page renders, with the cost table that says placement/practice/path are free and a mock costs 1 credit |
| `learner--02-certificates.png` | `/certificates` | The certificate catalogue renders, so the seeded certificates and skills read through the pool |
| `learner--03-dashboard.png` | `/Dashboard` | The dashboard's ONE-block unplaced branch — see the note below |
| `learner--04-practice-hub.png` | `/practice` | The practice hub renders with its cells |
| `learner--05-placement.png` | `/placement` | The placement entry point, free |

## Admin flow

| File | Screen | What it proves |
|---|---|---|
| `admin--01-admin-dashboard.png` | `/Admin` | The admin console loads for `admin@demo.local` |
| `admin--02-exam-catalogue.png` | `/Admin/Exams` | The exam catalogue, with each paper's purpose, duration and status |
| `admin--03-exam-studio.png` | `/Admin/Exams/1/studio` | **The admin→learner link.** Exam 1 "ETS 2024 — Test 1", P1 Photographs 1/6, the curriculum rail, the publish-readiness audit naming 4 invariant blocks, and the cluster editor with its accessibility alt text and audio slice controls |
| `admin--04-exam-create.png` | `/Admin/Exams/create` | The create form, so the authoring door is reachable |

## The admin→learner evidence, stated plainly

`admin--03-exam-studio.png` shows the operator's view of **exam 1**: 9 questions, P1 Photographs holding
1 of 6 declared positions, a publish audit that blocks with named deficits. `learner--02-certificates.png`
and the sitting journeys show the same exam 1 reachable and sittable from the learner side. Same paper, two
roles, one row — that is the proof the objective asked for.

## The defect these screenshots found

`learner--03-dashboard.png` renders **one block** and the sentence "Until you take it there is no band to
show". But the same learner has:

```
ExamAttempts:      2 rows, Status='graded', BandCode='B1', TotalScore set
AttemptSkillScores: 0 rows
LearnerSkillStates: 0 rows
```

`DashboardViewModel.IsUnplaced` is `Dashboard.Goal.HasNoMeasurement`, which resolves to
`LearnerSkillStates.Count == 0` — and the goal payload in the same response carries
`currentBandCode: "B1"` with `hasNoMeasurement: true`. So one page states a band and states that no band
exists. The page is right about what it was told; the seed never wrote the per-skill rows the band was
derived from. Tracked on #278.

## Not captured

- The palette-lock state: the seeded paper has one unit per section, so no chip can be locked. The journey
  is skipped with that reason in the tree (#278).
- A sitting in progress: `SittingAndPracticeQolTests` and the placement journey cover it as assertions;
  the screenshot set stops at the entry points, which is what an audit of the shells needs.
