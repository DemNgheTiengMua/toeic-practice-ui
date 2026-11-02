# Screenshot walk — every screen, grouped by the flow that reaches it

Captured 2026-11-03 against `scripts/seed-manual-test.sh fresh` (payment = mock, AI = fake), signed
in as the demo learner and the demo admin on the same database. 77 images in `docs/audit/screens/`.

The numbering is the capture order, but the grouping below is what matters: each section is one
journey, and the arrows mark where one screen's action changes the next one's data.

---

## 1. Public → identity

| # | Screen | Reached by |
|---|---|---|
| 01 | Landing page | `/` while signed out |
| 02 | Sign in | `/auth/sign-in` |
| 03 | Sign up | `/auth/register` |
| 04 | Forgot password | `/auth/forgot-password` |
| 82 | Not allowed | `/auth/denied`, the page a role mismatch lands on |

## 2. Learner: the three-block dashboard and its sources

| # | Screen |
|---|---|
| 05, 24 | Dashboard — Hero Next Action, Goal Standing, Skill Cards |
| 06, 22 | Learning path, before and after a practice session |
| 07, 23 | Weakness analysis, before and after a practice session |
| 08 | Practice catalogue (Smart Practice + one card per section) |
| 09 | Certificate switcher |

**Flow:** 05 → 08 → 19 → 20 → **22/23/24 change** because 20 recorded answers that the path and
the weakness views read.

## 3. Learner: practice sit → summary → insight

| # | Screen |
|---|---|
| 19 | Practice take (question 1 of a 10-question Smart Practice session) |
| 20 | Practice summary, including the AI insight panel |

Ten answers were submitted through the real endpoint to produce 20; the session finished and its
learning-path step closed.

## 4. Learner: writing, end to end, with real AI grading

| # | Screen | What it proves |
|---|---|---|
| 25 | Writing door | The list of published writing exams |
| 26 | Instructions | Credit cost stated before the commitment |
| 27 | Take, empty | Prompt + word budget |
| 28 | Take, draft saved | Autosave wrote (`Saved 02:54:03`) |
| 29 | Submit confirmation | The modal names unanswered questions before submitting |
| 30 | Pending | The AI grading queue is visible, not a spinner forever |
| 31 | **Review, graded** | 14/40, per-criterion scores, located feedback |
| 32 | Door after the attempt | Credit consumed, attempt now in progress/review |

The answer was a real 84-word letter; the grader returned 3.5/5 on four criteria and an
`Estimated band: A2`.

## 5. Learner: speaking

| # | Screen |
|---|---|
| 33 | Speaking door |
| 34 | Speaking instructions |

Recording needs a microphone, so the sit itself is not captured; the pipeline is proven separately
in `docs/audit/152-speechsuper-live-scores.md` with a real audio file.

## 6. Learner: mock exam, full lifecycle

| # | Screen |
|---|---|
| 35, 38 | Instructions, unchecked and ready (Start unlocks on the checkbox) |
| 39 | Pre-flight readiness check (audio + device verification) |
| 40 | Sitting, question 1 of 200 — timer, 18 palette cells |
| 41 | **Result** — see the defect note below |
| 42 | Review, per-question |
| 44 | Exam history, with every attempt |

**Flow:** 35 → 39 → 40 → 41 → 42. Submitting created attempt 4, which then appears in 44 and in
the admin's user view.

### Two defects found here and fixed

| Defect | Before | After |
|---|---|---|
| Band printed as its database key | `below_A1` | `BELOW A1` |
| Score delta always painted success-green | `-710` on green | `-710` on red |

Both were invisible to the test suite because nothing rendered this view with a scored attempt.
`tests/WebMVC.Tests/ExamResultPresentationTests.cs` now pins them.

## 7. Learner: money

| # | Screen |
|---|---|
| 45 | Wallet, after two paid attempts |
| 47 | Pricing — packages, one of which the admin created during this walk |
| 46, 48 | Confirm order, with the real total (100,000 ₫) |
| 49 | **Mock gateway** — the four branches: success, failure, slow/reconcile, expired |
| 50 | Payment result, success |

**Flow:** 47 → 48 → 49 → 50 → back to 45 with the new balance. The payment then appears in the
admin's order list (80).

## 8. Learner: mentors

| # | Screen |
|---|---|
| 51 | Mentor catalogue |
| 52 | Mentor detail |
| 53 | Booking confirmation (names the credit cost) |
| 54 | My bookings |

## 9. Learner: remaining surfaces

| # | Screen |
|---|---|
| 10 | Placement index |
| 11 | Target band / goal form |
| 12 | Wallet (first visit) |
| 13 | Mentor catalogue (first visit) |
| 14 | Exam catalogue |
| 15 | Exam history (empty at that point) |
| 16 | Saved questions |
| 17, 18 | Profile, change password |
| 83 | Learning-path module detail |
| 84 | Lesson player with a micro-check |

## 10. Admin: the same data, seen from the other side

| # | Screen |
|---|---|
| 55 | Dashboard |
| 56 | Certificates, including the new `SectionNoun` column's effect |
| 57 | Certificate blueprint wizard |
| 58, 69 | Exams list, create |
| 65 | **Exam studio** — two panes, one Category dropdown per question |
| 66 | Exam editor |
| 68 | Exam preview (the real sitting view, read-only, works on a draft) |
| 59, 73, 71, 72 | Lessons: list, create, editor, preview |
| 60 | Students — shows `learner@demo.local` |
| 61, 80 | Orders — shows the payment this walk made |
| 62, 76, 77, 78 | Packages: list, create form, filled, and the new package listed |
| 63, 74 | Mentors: list, slots |
| 64 | Complaints |

### Cross-role proof

Two directions, both captured:

- **Admin → learner:** the admin created "Screenshot Demo Pack" (76, 77). It then appeared on the
  learner's pricing page (79) without any restart.
- **Learner → admin:** the learner's mock exam (41) and payment (50) appear in the admin's user and
  order views (60, 80, 81).

---

## Pages that are JSON endpoints, not screens

These look like pages in the route table but return JSON for the browser to render, so they have no
screenshot and no `<title>`. Recording them here so a future sweep does not mistake them for broken
pages:

`/practice/insight/{id}`, `/exams/insight/{id}`, `/admin/exams/{id}/audit`,
`/admin/orders/{id}/detail`.

## Not captured, and why

| Route | Reason |
|---|---|
| `/admin/exams/import` | POST-only; a GET is correctly refused |
| Speaking sit | Needs a microphone; proven separately with a real audio file |
| Email verification / password reset completion | Needs a mailbox; the request forms are captured (04) |
| Complaint submission | No seeded complaint category for the learner to file against |

## What the sweep did not find

No 500s, no unhandled exceptions, and `scripts/seed-manual-test.sh errors` was clean for both
processes at the end. Every navigation that succeeded rendered its own `<title>`; the only empty
titles were the JSON endpoints listed above.

The two defects it did find were both on the exam result page and both are now fixed and pinned.
