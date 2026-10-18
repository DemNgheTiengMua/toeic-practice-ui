# Comprehensive Manual Test Checklist: Issues #63 to #73

This test checklist provides end-to-end manual verification steps for all 11 issues resolved during this session across Admin, Learner, and Authentication flows.

---

## Environment Setup & Credentials

Before testing, start the crawl environment or local servers:
```sh
# Option 1: Clean disposable crawl environment
scripts/crawl-env.sh fresh

# Option 2: Local development servers
# Terminal 1: WebAPI (port 5001)
dotnet run --project src/WebAPI

# Terminal 2: WebMVC (port 5000)
dotnet run --project src/WebMVC
```

### Test Accounts (Password: `Demo1234!`)
| Role | Email | Purpose |
|---|---|---|
| **Administrator** | `admin@demo.local` | Admin portal, exam studio, lesson editor, complaints, users & orders |
| **Active Learner** | `learner@demo.local` | Wallet, exam sitting, writing & speaking practice, guided learning path |
| **Fresh Learner** | `learner.fresh@demo.local` | Clean state for diagnostic placement and onboarding |

---

## Quick Reference Index

| Issue # | Domain / Flow | Primary URL | Role | Key Verification Point |
|---|---|---|---|---|
| [**#72**](#issue-72-admin-layout-on-users-and-orders) | Admin Portal Layout | `/admin/users`, `/admin/orders` | Admin | Left sidebar navigation and admin header render with 0 border-radius |
| [**#69**](#issue-69-retire-legacy-question-editor) | Exam Question Authoring | `/admin/exams/{id}/questions` | Admin | Legacy editor retired; 302 redirects smoothly to modern Exam Studio |
| [**#68**](#issue-68-admin-exams-reorganization) | Exam Management | `/admin/exams` | Admin | Certificate & Purpose filters; scoped Audio column; streamlined actions |
| [**#67**](#issue-67-wallet-timezone-formatting) | Credit Wallet & Orders | `/wallet` | Learner | Timestamps formatted in profile timezone (`Asia/Ho_Chi_Minh` / GMT+7) |
| [**#73**](#issue-73-section-audio-upload) | Exam Section Audio | `/admin/exams/{id}/edit?step=2` | Admin | File input (`accept="audio/*"`), client metadata extraction, 60 MB limit |
| [**#70**](#issue-70-lesson-import-modal-ui) | Lesson Management | `/admin/lessons` | Admin | Import modal renders in centered overlay with backdrop click & Escape dismiss |
| [**#71**](#issue-71-consolidate-exam-package-import) | Exam Package Ingest | `/admin/exams`, `/admin/imports` | Admin | Integrated import modal on `/admin/exams`; `/admin/imports` redirects cleanly |
| [**#63**](#issue-63-password-reset-confirmation-banner) | Authentication | `/auth/sign-in?reset=1` | Guest | Sign Green confirmation banner displays after password reset |
| [**#64**](#issue-64-exam-sitting-option-marker-click) | Exam Sitting Sheet | `/exams/take/{attemptId}` | Learner | Option marker badge does not intercept clicks; whole row triggers radio |
| [**#65**](#issue-65-module-heading-deduplication) | Guided Learning Path | `/learning-path/modules/{id}` | Learner | Single normalized `"Module N: "` prefix without duplication |
| [**#66**](#issue-66-form-validation-and-modal-accessibility) | Accessibility & Polish | `/admin/complaints/{id}`, `/writing/take/{id}`, `/speaking/take/{id}` | Mixed | Visual field error placards in Sign Red; modal `modal-overlay--open` & Escape |

---

## Detailed Test Cases

### Issue #72: Admin Layout on Users and Orders
- **Goal**: Verify that `/admin/users` and `/admin/orders` render inside the standardized `_AdminLayout` with the left rail navigation.
- **Role**: `admin@demo.local`
- **Steps**:
  1. Navigate to `http://localhost:5000/auth/sign-in` and sign in as `admin@demo.local`.
  2. Click **Users** in the admin sidebar or go to `http://localhost:5000/admin/users`.
  3. Verify that the left rail navigation sidebar is present with **Users** active (solid ink inversion).
  4. Click **Orders** in the admin sidebar or go to `http://localhost:5000/admin/orders`.
  5. Verify that the left rail navigation sidebar is present with **Orders** active.
- **Expected Visuals**:
  - Full admin sidebar on the left.
  - Page header displays admin title placard with flat 2px ink borders.
  - No missing CSS styles or uncontained raw tables.
- **Automated Test**: `WebMVC.E2ETests.Journeys.AdminExamStudioTests.Should_Render_Admin_Sidebar_And_Layout_On_Users_And_Orders_Pages`.

---

### Issue #69: Retire Legacy Question Editor
- **Goal**: Verify that the broken legacy Question Editor view is completely deleted and requests redirect to Exam Studio.
- **Role**: `admin@demo.local`
- **Steps**:
  1. With admin session, navigate directly to `http://localhost:5000/admin/exams/1/questions`.
  2. Observe browser location and HTTP status code.
  3. Navigate to `http://localhost:5000/admin/exams/1/edit?step=3`.
  4. Click the link to author/edit questions.
- **Expected Visuals**:
  - Direct HTTP 302 redirect from `/admin/exams/1/questions` to `http://localhost:5000/admin/exams/1/studio`.
  - Step 3 in `ExamEditor.cshtml` links directly to Exam Studio (`/admin/exams/1/studio`).
  - No 500 error or reference to deleted `QuestionEditor.cshtml`.
- **Automated Test**: `WebMVC.E2ETests.Journeys.AdminExamStudioTests.Should_Redirect_Legacy_Questions_Route_Directly_To_Exam_Studio`.

---

### Issue #68: Admin Exams Reorganization
- **Goal**: Verify toolbar Certificate & Purpose dropdown filtering, scoped Audio column, and streamlined row action buttons.
- **Role**: `admin@demo.local`
- **Steps**:
  1. Navigate to `http://localhost:5000/admin/exams`.
  2. Inspect the toolbar: observe the **Certificate** and **Purpose** dropdown filters.
  3. Filter by **Purpose: Mock Exam** and click Filter: only mock exams should display.
  4. Filter by **Purpose: Placement Diagnostic**: only diagnostic exams should display.
  5. Inspect the exam rows:
     - Each exam displays a bold Certificate placard (e.g., `[TOEIC]`, `[IELTS]`) and Purpose badge.
     - For exams with listening sections, the **Audio** column displays audio completion status.
     - For reading/writing/text-only exams, the **Audio** column displays a muted `—` (N/A) rather than misleading "Missing audio".
  6. Inspect row actions:
     - Primary button: **Studio** (links to `/admin/exams/{id}/studio`).
     - Secondary button: **Edit Info** (links to `/admin/exams/{id}/edit`).
     - Danger button: **Delete** (only visible if exam has 0 student attempts).
- **Automated Test**: `WebMVC.Tests.AdminExamsControllerTests.Index_FiltersByCertificateAndPurpose`.

---

### Issue #67: Wallet Timezone Formatting
- **Goal**: Verify that ledger timestamps render in the user's configured profile timezone rather than raw UTC.
- **Role**: `learner@demo.local`
- **Steps**:
  1. Sign in as `learner@demo.local`.
  2. Navigate to `http://localhost:5000/wallet`.
  3. Inspect the transaction ledger table under **Credit History** and **Recent Orders**.
  4. Compare the rendered timestamps against your expected local time (e.g. `18 Oct 2026, 17:30` rather than UTC `10:30`).
- **Expected Visuals**:
  - Consistent format `dd MMM yyyy HH:mm` formatted through `DateTimeFormatter`.
  - No raw ISO strings or UTC offset mismatches.
- **Automated Test**: `WebMVC.Tests.WalletControllerTests.Wallet_Renders_Timestamps_With_DateTimeFormatter`.

---

### Issue #73: Section Audio Upload
- **Goal**: Verify that administrators can upload audio files with client-side metadata extraction and automatic size/duration calculation.
- **Role**: `admin@demo.local`
- **Steps**:
  1. Navigate to `http://localhost:5000/admin/exams/1/edit?step=2`.
  2. Locate a listening section (e.g., Part 1: Photographs).
  3. Inspect the audio form: observe standard file input (`Choose file` / `Browse`) accepting `.mp3, .wav, audio/*`.
  4. Select a sample `.mp3` file from your local drive.
  5. Observe the client-side metadata extraction:
     - Duration in seconds is automatically calculated via HTML5 Audio and populated.
     - File size in bytes is automatically calculated and populated.
  6. Expand the `<details>` dropdown labeled **Advanced: manual metadata override** to verify manual override fields are tucked away safely.
  7. Submit the form: audio is uploaded via `api.UploadAdminExamMediaAsync` and saved to section audio.
- **Negative Case**: Try selecting a file larger than 60 MB: observe client error placard rejecting the upload before transmission.
- **Automated Test**: `WebMVC.Tests.AdminExamsControllerTests.SaveAudio_WithFile_UploadsMediaAndSavesSection`.

---

### Issue #70: Lesson Import Modal UI
- **Goal**: Verify that the Lesson import modal on `/admin/lessons` is a centered modal overlay and does not render permanently inline below the table.
- **Role**: `admin@demo.local`
- **Steps**:
  1. Navigate to `http://localhost:5000/admin/lessons`.
  2. Inspect the page on initial load: verify NO open modal or broken dialog is visible below the table.
  3. Click **Import lesson package (.zip / .json)** in the toolbar.
  4. Observe the modal overlay:
     - Displays centered over a dark scrim (`background: var(--color-overlay)`).
     - Flat 2px ink frame, `border-radius: 0`, `box-shadow: none`.
  5. Press the **Escape** key: verify the modal closes immediately.
  6. Reopen the modal and click outside the white dialog box on the backdrop: verify the modal closes.
- **Automated Test**: `WebMVC.E2ETests.Journeys.AdminLessonsTests.Should_Open_Import_Dialog_And_Import_Lesson_Manifest`.

---

### Issue #71: Consolidate Exam Package Import
- **Goal**: Verify that exam package import is integrated directly into `/admin/exams`, while the legacy `/admin/imports` route redirects cleanly.
- **Role**: `admin@demo.local`
- **Steps**:
  1. Inspect the admin sidebar in `_AdminLayout`: verify the confusing **Question import** link is completely gone from the sidebar.
  2. Navigate to `http://localhost:5000/admin/exams`.
  3. In the top action bar next to "Create new exam", verify the **Import Exam (.zip / .json)** button.
  4. Click the button: the `#import-exam-dialog` modal appears with:
     - Target Certificate selector (pre-selected if toolbar filter is active).
     - File upload input for `.zip` or `.json`.
  5. Close the modal, then navigate in the browser URL bar to `http://localhost:5000/admin/imports`.
  6. Observe redirect to `http://localhost:5000/admin/exams?import=true`, which automatically opens the import dialog.
- **Automated Test**: `WebMVC.E2ETests.Journeys.AdminPortalTests.Should_Show_Import_Exam_Modal_On_Admin_Exams_Page`.

---

### Issue #63: Password Reset Confirmation Banner
- **Goal**: Verify that completing a password reset or landing on `/auth/sign-in?reset=1` renders a Sign Green confirmation alert banner.
- **Role**: Guest / Learner
- **Steps**:
  1. Navigate directly to `http://localhost:5000/auth/sign-in?reset=1`.
  2. Observe the alert banner rendered above the sign-in card.
  3. Verify the copy matches:
     *"Your password has been reset successfully. Please sign in with your new password."*
  4. Verify the alert styling: flat border in Sign Green (`#0a6b3c`), role="status".
  5. Navigate to plain `http://localhost:5000/auth/sign-in` without parameters: verify no alert banner is rendered.
  6. Navigate to `http://localhost:5000/auth/sign-in?locked=1`: verify the account locked error banner displays:
     *"This account is locked. Contact support."*
- **Automated Test**: `WebMVC.Tests.AuthControllerTests.SignIn_WithResetConfirmation_SetsNoticeBanner`.

---

### Issue #64: Exam Sitting Option Marker Click Interception
- **Goal**: Verify that clicking directly on the styled option badge (`A`, `B`, `C`, `D`) triggers radio selection without pointer event interception.
- **Role**: `learner@demo.local`
- **Steps**:
  1. Sign in as `learner@demo.local`.
  2. Navigate to `http://localhost:5000/exams`.
  3. Start or resume any exam attempt to enter `/exams/take/{attemptId}`.
  4. In the question answer sheet, locate Question 1 with options A, B, C, D.
  5. Click with your mouse directly in the center of the square marker `[A]`.
  6. Verify that option A is immediately selected (marker inverts to solid ink `#14181d` with white text, answer sheet records selection).
  7. Click directly on option label `[B]`: verify smooth selection transfer.
  8. Open browser DevTools: inspect `.option__marker` and `.option__text`: verify `pointer-events: none` is computed.
- **Automated Test**: `WebMVC.Tests.ExamsViewTests.ComponentsCss_OptionMarkerAndText_MustHavePointerEventsNone`.

---

### Issue #65: Module Heading Deduplication
- **Goal**: Verify that guided learning path module details display a single normalized `"Module N: "` prefix.
- **Role**: `learner@demo.local`
- **Steps**:
  1. Navigate to `http://localhost:5000/learning-path`.
  2. Click on Module 2 (or any unlocked module).
  3. Inspect the breadcrumb path at the top: `Home › My learning path › Module 2`.
  4. Inspect the `<h1>` headline on the page.
  5. Verify that the title renders as:
     `Module 2: Sentence Structure & AI Speaking Drill` (or similar clean title).
  6. Verify there is **NO** duplicate `"MODULE 2: MODULE 2: ..."` in the headline or browser tab title (`ViewData["Title"]`).
- **Automated Test**: `WebMVC.Tests.LearningPathModuleTests.FormattedHeading_NormalizesDuplicatedModulePrefix`.

---

### Issue #66: Form Validation Styling and Modal Accessibility
- **Goal**: Verify error state placards in complaint resolution and accessible open/close transitions in Writing and Speaking modals.
- **Role**: `admin@demo.local` (Complaints) & `learner@demo.local` (Writing / Speaking)
- **Part A: Complaint Form Validation (`/admin/complaints/{id}`)**:
  1. Sign in as `admin@demo.local` and go to `http://localhost:5000/admin/complaints`.
  2. Open an open or reviewing complaint (e.g. `/admin/complaints/1`).
  3. In the Resolution card, leave **Reply to learner** and **Internal notes** blank.
  4. Click the **Resolve complaint** button.
  5. Verify that the submission is intercepted on the client:
     - The textareas receive red borders (`var(--sign-red)`).
     - `.field__error-placard` messages appear immediately below the inputs in Sign Red.
     - The first invalid field receives focus.
  6. Type text into the fields: verify error borders and placards disappear in real-time.
- **Part B: Writing Practice Modal Accessibility (`/writing/take/{id}`)**:
  1. Sign in as `learner@demo.local` and enter a writing attempt at `/writing/take/{id}`.
  2. Click **Finish and submit for AI grading**.
  3. Verify that `#submit-modal` receives `.modal-overlay--open` and `aria-hidden="false"`.
  4. Press the **Escape** key: verify the modal closes cleanly and `aria-hidden="true"`.
  5. Reopen the modal and click outside the dialog box: verify it closes.
- **Part C: Speaking Practice Modal Accessibility (`/speaking/take/{id}`)**:
  1. Enter a speaking attempt at `/speaking/take/{id}`.
  2. Click **Finish & Submit**.
  3. Verify identical accessible behavior: `.modal-overlay--open`, `Escape` key closes, backdrop click closes.
- **Automated Tests**:
  - `WebMVC.Tests.AdminViewsLayoutTests.ComplaintDetailView_MustContainValidationErrorPlacardsAndClientValidation`
  - `WebMVC.Tests.AdminViewsLayoutTests.WritingTakeView_SubmitModal_MustHaveAccessibleOverlayAndKeyboardDismiss`
  - `WebMVC.Tests.AdminViewsLayoutTests.SpeakingTakeView_SubmitModal_MustHaveAccessibleOverlayAndKeyboardDismiss`

---

## Sign-Off Checklist Summary

- [ ] #72: Admin layout present on `/admin/users` and `/admin/orders`
- [ ] #69: Legacy question editor deleted; 302 redirects to Exam Studio
- [ ] #68: Exams toolbar has Certificate + Purpose filters; Audio column scoped
- [ ] #67: Wallet timestamps formatted in user local timezone
- [ ] #73: Audio upload uses HTML5 file picker with automatic duration/size calculation
- [ ] #70: Lesson import modal behaves as an overlay dialog with Escape dismissal
- [ ] #71: Standalone `/admin/imports` retired; package import modal live on `/admin/exams`
- [ ] #63: Password reset confirmation alert banner appears on `/auth/sign-in?reset=1`
- [ ] #64: Option marker `A/B/C/D` badges do not block pointer click events
- [ ] #65: Guided learning path module headlines do not repeat `"Module N: "`
- [ ] #66: Complaint validation highlights invalid fields; Writing/Speaking modals support Escape & ARIA
- [ ] All 895 automated tests passing (`Services.Tests`, `WebMVC.Tests`, `WebMVC.E2ETests`)
- [ ] Remote `main` updated and merged at commit `b7c74d7`
