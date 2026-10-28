# MANUAL TEST CREDENTIALS

Generated for the **ExtraEfficiency** local database (`(localdb)\MSSQLLocalDB`, DB `ExtraEfficiency`).

> ⚠️ **Local dev only.** Every password below is a shared demo constant that is seeded by
> `docs/seed-demo.sql` into a LocalDB instance on this machine. Do not reuse them, do not
> point any deployment at this database, and never copy this file into a public repo.
> `*.local` email addresses are non-routable by design (see `.gitignore`: `.env*` is ignored,
> this file is dev documentation — keep it out of any release artifact).

---

## 1. Start the environment

```sh
# One command: stop → drop/recreate DB → schema → both seeds → build → start both apps
./scripts/seed-manual-test.sh fresh

# Then open
http://localhost:5000/auth/sign-in
```

Other subcommands:

| Command | What it does |
| :--- | :--- |
| `./scripts/seed-manual-test.sh fresh` | Full reset + seed + build + start (use before each manual pass) |
| `./scripts/seed-manual-test.sh seed` | Stop apps, drop DB, reload schema + both seeds (no start) |
| `./scripts/seed-manual-test.sh start` | Build and start WebAPI (5001) + WebMVC (5000) |
| `./scripts/seed-manual-test.sh stop` | Stop whatever listens on 5000 / 5001 |
| `./scripts/seed-manual-test.sh status` | Ports, learner credit balance, seeded row counts |
| `./scripts/seed-manual-test.sh errors` | Unhandled exceptions from `logs/manual/{api,mvc}.log` |

Runtime mode: **payment = `mock`**, **AI = `fake`** (deterministic offline fakes).
Neither spends a real credit nor a real SpeechSuper/Claude call. Add `--real-ai` to
`start`/`fresh` if you deliberately want live AI grading (needs the keys in `.env`).

---

## 2. Accounts — password for **all four**: `Demo1234!`

| Role | Email | Password | Lands on | Seeded state |
| :--- | :--- | :--- | :--- | :--- |
| **Admin** | `admin@demo.local` | `Demo1234!` | `/Admin` | Full admin console: 5 certificates, 18 exams, users, packages, orders, complaints, mentors, lessons, score conversions |
| **Learner (rich history)** | `learner@demo.local` | `Demo1234!` | `/Dashboard` | **8 credits**, active TOEIC cert, target band **B2**, placement result **B1 / 650**, 3 practice sessions, 2 graded exam attempts (650 B1, 720 B1), active learning path, 2 saved questions, 2 mentor bookings, 2 complaints, 4 AI insights, 4 recommendations |
| **Learner (clean slate)** | `learner.fresh@demo.local` | `Demo1234!` | `/Dashboard` | **0 credits**, no attempts, no history — use for first-run / empty-state and checkout tests |
| **Mentor** | `mentor.khoa@demo.local` | `Demo1234!` | `/Dashboard` | Profile "Dr. Trần Minh Khoa", mentor profile + slots (role is `learner`, appears in `/mentors`) |

Password hash is PBKDF2-SHA256 (`v1` format), identical on every seed re-run by design.

---

## 3. Seeded catalog you can test against

### Certificates (5)
`TOEIC` · `IELTS` · `VSTEP` · `TOEIC_CAT` · `TOEIC_SW`

### Exams by id (18 total, all `published`)
| ExamId | Cert | Name | Purpose | Questions |
| ---: | :--- | :--- | :--- | ---: |
| 1 | TOEIC | ETS 2024 — Test 1 | mock | 9 |
| 2 | TOEIC | TOEIC Placement Test — Standard | placement | 4 |
| 3 | TOEIC | TOEIC Placement Test — Quick | placement | 2 |
| 4 | TOEIC | ETS TOEIC Speaking Practice Exam 1 | speaking_practice | 3 |
| 5 | TOEIC | ETS TOEIC Writing Practice Test 1 | writing_practice | 2 |
| 6 | IELTS | Cambridge IELTS 18 — Academic Test 1 | mock | 2 |
| 7 | IELTS | IELTS-DIAG-01 | placement | 10 |
| 8 | IELTS | Cambridge IELTS 18 — Speaking Practice Test 1 | speaking_practice | 2 |
| 9 | IELTS | Cambridge IELTS 18 — Writing Practice Test 1 | writing_practice | 2 |
| 10 | VSTEP | VSTEP Sample Test 1 | mock | 2 |
| 11 | VSTEP | VSTEP-DIAG-01 | placement | 10 |
| 12 | VSTEP | VSTEP Speaking Practice Test 1 | speaking_practice | 2 |
| 13 | VSTEP | VSTEP Writing Practice Test 1 | writing_practice | 2 |
| 14 | TOEIC | Diagnostic Benchmark 45M | placement | 32 |
| 15 | IELTS | IELTS-DIAG-01 Writing Extension | writing_practice | 2 |
| 16 | VSTEP | VSTEP-DIAG-01 Writing Extension | writing_practice | 2 |
| 17 | TOEIC_CAT | TOEIC CAT Benchmark Test 1 | mock | 7 (blueprint says 11; all 6 component types) |
| 18 | TOEIC_SW | TOEIC Speaking & Writing Mock Test 1 | mock | 3 |

`QuestionCount` in the `Exams` table is the **blueprint** count (e.g. 200 for ExamId 1);
the number of questions actually seeded is in the table above.

### Credit packages (`/pricing`, `/checkout`)
| PackageId | Name | Credits | Price (VND) | Featured |
| ---: | :--- | ---: | ---: | :--- |
| 2 | Starter Pack | 5 | 100,000 | |
| 1 | Intensive Pack | 10 | 190,000 | ✔ |
| 3 | Master Pack | 20 | 360,000 | |

### Exams with a pre-graded attempt for `learner@demo.local`
| AttemptId | ExamId | Status | Score | Band |
| ---: | ---: | :--- | ---: | :--- |
| 1 | 2 (TOEIC placement standard) | graded | 650 | B1 |
| 2 | 1 (ETS 2024 — Test 1) | graded | 720 | B1 |

Practice sessions for `learner@demo.local`: `SessionId` 1 (P1 listening, 1/1),
2 (P2 listening, 1/1), 3 (P5 reading, 2/3), all `finished` / `immediate` feedback.

---

## 4. Payment testing (mock gateway)

1. Sign in as `learner.fresh@demo.local` (0 credits) → `/pricing`.
2. Pick any package → `/checkout?packageId=1` → submit the order.
3. You land on **Payment Gateway (simulated)** with four buttons:

| Button | Order status after | Result |
| :--- | :--- | :--- |
| **Payment successful** | `paid` | credits added to the ledger, `/payment/result` shows success; wallet balance rises immediately |
| **Payment failed** | `failed` | error code 24, no credits |
| **Slow gateway (reconcile)** | `reconciling` | no credits yet; exercises the admin reconcile path at `/admin/orders` |
| **Order expired** | `expired` | no credits |

All four verified against this seed. Note: only **one order may be open per learner** at a
time (`order_already_open` 409), so test the branches one per order — after a branch that
leaves the order open (`reconciling`), finish or expire it before creating the next one.
No real payOS call is made in `mock` mode.

---

## 5. One-command API smoke test

```sh
TOKEN=$(curl -s -X POST http://localhost:5001/api/auth/sign-in \
  -H "Content-Type: application/json" \
  -d '{"email":"learner@demo.local","password":"Demo1234!"}' \
  | sed 's/.*"token":"\([^"]*\)".*/\1/')

curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5001/api/auth/me
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5001/api/learner/wallet
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5001/api/learner/weakness-analysis
```

Health check: `curl http://localhost:5001/healthz` → `Healthy`.

---

## 6. What is NOT covered by the seed

- **Real AI grading.** `AiGradings` is empty by design; with `AI_PROVIDER=fake` a submitted
  speaking/writing response is graded by the deterministic fake and appears in
  `AiGradings`/`AiGradingScores` within ~2s (background worker interval).
- **Real payOS.** `appsettings.json` still says `"Gateway": "payos"` with placeholder
  credentials — that is why checkout 500s if you run the app without the mock override.
  Always start via `scripts/seed-manual-test.sh` (or pass `Payment__Gateway=mock`).
- **Real audio beyond the seeded mp3s.** Files live in `src/WebMVC/wwwroot/audio/{toeic,ielts,vstep}/`.
  Importing new audio writes to `App_Data/audio/` (gitignored).
- **Activation / reset emails.** `Email:DropFolder` writes to `mail-drop/` (gitignored);
  no SMTP is configured.
