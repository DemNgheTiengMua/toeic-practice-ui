# Mentor booking rebuild: admin-managed human mentors, weekly recurring availability, credit-based payout CSV, and learner dashboard reminders

**Status:** ready-for-agent
**Date:** 2026-11-14
**ADRs touched:** ADR-0001 (database-first EF Core), ADR-0014 (UTC+7 reporting period), ADR-0015 (React + HeroUI v3 SPA), ADR-0016 (human-only mentors, weekly recurring availability, and credit-based payout export)

---

## Problem Statement

The current mentor booking system suffers from six structural and product defects:

1. **AI mentor is a mislabelled speaking-exam shortcut.** The product mixes human 1-on-1 consultations with an `ai` mentor type under one schema and service. Booking an AI mentor charges mentor credits only to start a speaking practice attempt and redirect the learner into the exam room, duplicating the speaking-practice flow while polluting every mentor table, enum, endpoint, and screen with `human` vs `ai` branching.
2. **Human mentors require a dummy user account and have no real meeting link.** Creating a human mentor requires linking a separate `Users` row, even though mentors do not log in to manage the platform. Worse, a mentor profile cannot store a real meeting link: booking invents a random fake `https://meet.google.com/xxx-xxxx-xxx` URL on the fly.
3. **One-off manual slot scheduling does not match how mentors work.** Admins must manually insert individual timestamped slots one date at a time. When those dates pass, the mentor appears permanently out of slots unless an admin manually enters next week's dates again.
4. **Booked slots vanish instead of showing as taken.** When a learner books a slot, the mentor detail query filters out non-open slots. Other learners looking at that mentor cannot tell whether the mentor teaches at that time and is already booked for the week, or never taught at that time at all.
5. **Mentor compensation is split across two currencies and cannot be exported.** Each mentor carries both a cash `PricePerSlot` and a `PriceCredits` figure, while three dead schema objects (`MentorPayouts`, `MentorPayoutItems`, `vw_MentorPayoutTotals`) sit unimplemented. Admins have no screen to see how many credits and how much VND a mentor earned from completed sessions in a reporting period, and no CSV export to pay them from.
6. **Learners have no reminder or join link on their dashboard.** After booking a mentor session, the learner's main dashboard shows nothing about their upcoming appointment; they must remember to navigate into the bookings sub-page to find the time and meeting link.

---

## Solution

**Replace the legacy mentor implementation completely, leaving zero dead columns, tables, enums, or routes behind.**

1. **Human mentors only, managed entirely by Admin, with a fixed meeting link.**
   - Remove the `ai` mentor concept and delete the mentor-type discriminator across the database, domain models, wire contracts, services, and UI.
   - Remove the linked user-account requirement and the standalone instructor console (`/instructor/sessions`). Admin creates and manages all mentor profiles directly, specifying display name, certificate, optional skill, bio, headline, expertise, session cost in credits, active status, and a **fixed meeting link** (`MeetLink`).

2. **Weekly recurring availability windows with automatic next-slot calculation.**
   - Instead of creating one-off calendar timestamps, Admin configures a mentor's **weekly availability windows**: day of week (`Monday` through `Sunday`) and one or more non-overlapping time ranges (`StartTime` to `EndTime`, in Vietnam time `UTC+7`) per day.
   - Whenever a learner views a mentor or books a session, the system automatically computes and materializes the **next upcoming occurrence** for each weekly availability window within the rolling 7-day window from the current instant (`UTC+7`). If a window's start time on today's day of the week is still in the future, today's occurrence is offered; once that start time passes, the system automatically advances that window to next week's occurrence.

3. **Booked slots stay visible and greyed out for learners.**
   - The mentor detail response returns all upcoming occurrences for the mentor's weekly schedule, both `open` and `booked`.
   - On the learner mentor detail screen, an `open` slot is selectable, whereas a `booked` slot remains visible in the schedule with a disabled (greyed-out) button and a "Booked" indicator so learners see the mentor's full weekly rhythm and know which times are already taken for the upcoming week.
   - When an admin edits or removes weekly availability windows, unbooked future occurrences that no longer match the weekly schedule are closed, while any occurrence that already has a confirmed booking is preserved intact.

4. **Single-unit credit pricing (`1 Credit = 20,000 VND`) and completed-session CSV export.**
   - Remove the separate cash `PricePerSlot` input and the dead `MentorPayouts` / `MentorPayoutItems` / `vw_MentorPayoutTotals` tables.
   - Every mentor profile is priced in **credits** (`PriceCredits`), snapshotted onto each booking at the moment the learner books (`Bookings.PriceCredits`).
   - Platform-wide mentor payout conversion is pinned at **`1 Credit = 20,000 VND`**.
   - A booking counts toward mentor payout once its scheduled session has ended (`EndAt <= now`), its status is not `cancelled`, and it has no resolved `mentor_no_show` complaint carrying a make-good credit grant.
   - In the Admin Mentors workspace, Admin can filter completed sessions by reporting date range (`from` to `to`, cut at `UTC+7` just like `RefundService`), inspect per-mentor session counts, total credits earned, and VND payout amounts, and download a **UTF-8 BOM CSV** formatted with the same RFC 4180 contract as `RefundExportCsv`.

5. **Upcoming session reminder and Join Meet link on the Learner Dashboard.**
   - The learner dashboard payload includes all confirmed mentor bookings whose session has not yet ended (`EndAt > now`), ordered chronologically across certificates.
   - When the learner has one or more upcoming bookings, the dashboard displays a prominent reminder banner showing the mentor's name, certificate and skill, scheduled date and time window, focus area, and a direct **Join Meet** button pointing to the booking's meeting link.

---

## User Stories

### Admin mentor profile & fixed meeting link management

1. As an admin, I want to create a mentor profile without creating or linking a learner/instructor user account, so that onboarding an external educator takes one form rather than two.
2. As an admin, I want to set a mandatory fixed meeting link (e.g., Google Meet URL) on each mentor profile, so that every booking with that mentor uses their permanent consultation room.
3. As an admin, I want to update a mentor's fixed meeting link at any time, so that future bookings use the updated room while past booking records remain intact.
4. As an admin, I want to configure a mentor's price strictly in credits, so that there is no discrepancy between what learners pay in credits and how mentor compensation is tracked.
5. As an admin, I want to assign a mentor to a certificate and optionally a specific skill belonging to that certificate, so that certificate isolation is enforced.
6. As an admin, I want to activate or deactivate a mentor profile, so that inactive mentors are hidden from learners and cannot receive new bookings.
7. As an admin, I want the mentor form and table to contain no AI mentor toggle or AI coach references, so that the workspace reflects our human-only mentoring model.

### Admin weekly recurring availability schedule

8. As an admin, I want to define which days of the week (`Monday` through `Sunday`) a mentor is available, so that I only configure their recurring weekly schedule once.
9. As an admin, I want to add multiple time windows on the same day of the week for a mentor (for example, Monday `08:00–09:00`, `14:00–15:00`, and `19:30–20:30`), so that mentors who teach several sessions on the same weekday are fully supported.
10. As an admin, I want the system to reject a weekly availability window whose end time is not after its start time, so that invalid time ranges cannot be saved.
11. As an admin, I want the system to reject overlapping weekly availability windows on the same day of the week for the same mentor, so that a mentor cannot be double-scheduled against themselves.
12. As an admin, I want weekly availability times to be interpreted in Vietnam time (`UTC+7`), so that `19:00–20:00` always means 7:00 PM to 8:00 PM Vietnam time regardless of the server container's timezone.
13. As an admin, I want removing or editing a weekly availability window to preserve any upcoming session that a learner has already booked, so that a schedule adjustment never silently cancels or orphans a paid learner booking.
14. As an admin, I want removing or editing a weekly availability window to close or remove any unbooked future slot occurrence from the old window, so that learners can no longer book the removed time.

### Automatic next-slot calculation & learner booking experience

15. As a learner browsing the mentor catalog, I want to see only active human mentors filtered by certificate and skill, so that I can find an educator for my target exam.
16. As a learner viewing a mentor's profile, I want the system to automatically calculate the next upcoming date and time for each of the mentor's weekly availability windows within the next 7 days, so that I always see concrete upcoming dates to book.
17. As a learner viewing a mentor's profile on a day when one of the mentor's weekly windows has already started or passed earlier today, I want that window's next occurrence to roll forward to the same weekday next week, so that I am only offered future sessions.
18. As a learner viewing a mentor's profile, I want to see slots that have already been booked by another learner for the upcoming occurrence rendered as greyed-out / disabled buttons marked as booked, so that I know the mentor teaches that slot and that it is taken for this week.
19. As a learner viewing a mentor's profile, I want open upcoming slots to be selectable and clearly show the weekday, date, start time, and end time, so that I know the exact session I am booking.
20. As a learner, I want to enter an optional focus area (e.g., "Listening Part 3 & 4 pacing") when booking a slot, so that the mentor knows what I want to work on.
21. As a learner, I want my credit balance to be checked before the booking is created ("Block before you charge"), so that a booking is refused cleanly if I do not have enough credits.
22. As a learner, I want booking an available slot to deduct the mentor's credit price from my wallet ledger, mark that concrete slot occurrence as `booked`, and snapshot the mentor's fixed meeting link and credit price onto my booking.
23. As a learner, I want a concurrent attempt to book a slot that another learner just took to be refused without deducting my credits, so that double-booking is impossible.
24. As a learner, I want to cancel a confirmed booking at least 24 hours before the session's start time and receive a full refund of the exact credits I paid, freeing that slot occurrence back to `open` for other learners.
25. As a learner, I want cancellation within 24 hours of the session's start time (or after it starts) to be refused, so that late cancellations cannot abuse the mentor's reserved time.
26. As a learner, I want to rate and review a booking once its scheduled session has finished, so that future learners can see verified mentor ratings.

### Learner dashboard reminder & Join Meet link

27. As a learner who has an upcoming confirmed mentor booking, I want to see a reminder banner on my main dashboard (`/dashboard`), so that I never miss a scheduled session.
28. As a learner, I want the dashboard reminder to appear even if I switch my active certificate selector to another certificate, so that a scheduled consultation is never hidden behind a certificate filter.
29. As a learner, I want the dashboard reminder to display the mentor's display name, certificate and skill, session date and time range, and my focus area, so that I have all session details at a glance.
30. As a learner, I want the dashboard reminder to include a direct **Join Meet** button that opens the mentor's meeting link in a new tab, so that I can enter the call in one click from the dashboard.
31. As a learner, I want a booking to disappear from my dashboard reminder once the session's end time (`EndAt`) has passed or if I cancel the booking, so that the dashboard only reminds me of active upcoming or currently running sessions.

### Admin mentor payout summary & CSV export

32. As an admin or accountant, I want mentor compensation to be calculated from completed sessions using the booking's credit price and the platform's fixed conversion rate (`1 Credit = 20,000 VND`), so that credits and VND payouts always reconcile.
33. As an admin or accountant, I want changing a mentor's credit price today to affect only future bookings, leaving the credit count and VND payout of already-booked sessions unchanged.
34. As an admin or accountant, I want only non-cancelled bookings whose session end time has passed (`EndAt <= now`) to count as completed payout sessions, so that future or cancelled bookings are never paid out prematurely.
35. As an admin or accountant, I want any booking that has a resolved `mentor_no_show` complaint with a make-good credit grant to be excluded from mentor payouts, so that a mentor who failed to show up is not paid for that session.
36. As an admin or accountant, I want to filter mentor payout summaries and completed session rows by a reporting date range (`from` to `to`, cut at `UTC+7` based on session end time `EndAt`), so that monthly or weekly payout runs align with Vietnam accounting days.
37. As an admin or accountant, I want to see each mentor's completed session count, total credits earned, and total VND payout amount for the selected reporting period in the Admin Mentors workspace.
38. As an admin or accountant, I want to export the completed mentor sessions for a `from`..`to` reporting date range as a CSV file, so that accounting can process bank transfers in Excel.
39. As an admin or accountant, I want the mentor payout CSV to start with a UTF-8 byte-order mark (BOM), use RFC 4180 quoting, format dates as ISO-8601 (`yyyy-MM-dd`), and output VND amounts as bare decimal numbers (`0.00`), so that Excel on a Vietnamese Windows machine opens diacritics and sums money columns without corruption.
40. As an admin or accountant, I want exporting the mentor payout CSV to be a pure read that modifies no database state, so that exporting the same period twice produces the exact same rows.

---

## Implementation Decisions

### 1. Full replacement & zero technical debt

All legacy artifacts from the dual `human`/`ai` mentor model, manual one-off slot authoring, cash `PricePerSlot`, unimplemented `MentorPayouts` tables, and standalone instructor console are deleted rather than kept as unused fallbacks:

- **Database schema (`docs/database-schema.sql`)**:
  - On `MentorProfiles`:
    - Remove `Type`, `CK_Mentor_Type`, `CK_Mentor_TypeUserId`, `UQ_MentorProfiles_Id_Type`, `PricePerSlot`, `CK_Mentor_Price`, and `UserId` (`Users.MentorProfiles` navigation).
    - Add `MeetLink NVARCHAR(400) NOT NULL`.
    - Keep `PriceCredits INT NOT NULL CONSTRAINT DF_Mentor_PriceCredits DEFAULT 1 CONSTRAINT CK_Mentor_PriceCredits CHECK (PriceCredits > 0)`.
  - Create `MentorWeeklySlots`:
    ```sql
    CREATE TABLE MentorWeeklySlots (
      WeeklySlotId INT IDENTITY(1,1) PRIMARY KEY,
      MentorId     INT NOT NULL REFERENCES MentorProfiles(MentorId),
      DayOfWeek    TINYINT NOT NULL
        CONSTRAINT CK_MentorWeeklySlots_DayOfWeek CHECK (DayOfWeek BETWEEN 1 AND 7),
      StartTime    TIME(0) NOT NULL,
      EndTime      TIME(0) NOT NULL,
      CONSTRAINT CK_MentorWeeklySlots_Range CHECK (EndTime > StartTime)
    );
    ```
    (`DayOfWeek` uses ISO-8601 numbering: `1` = Monday / Thứ 2 through `7` = Sunday / Chủ Nhật).
  - On `MentorSlots`:
    - Keep `(SlotId, MentorId, StartAt, EndAt, Status)` as the materialized concrete occurrence table so `UX_Bookings_OnePerSlot`, `FK_Bookings_SlotMentor`, and `trg_Bookings_SlotStatusGuardAndSync` continue to enforce concurrency and single-booking guarantees in SQL Server.
  - On `Bookings`:
    - Remove `MentorType`, `CK_Bookings_MentorType`, `FK_Bookings_MentorType`, and `CK_Bookings_MentorTypeSlot`.
    - Make `SlotId INT NOT NULL REFERENCES MentorSlots(SlotId)` mandatory on every booking.
    - Add `PriceCredits INT NOT NULL CONSTRAINT DF_Bookings_PriceCredits DEFAULT 1 CONSTRAINT CK_Bookings_PriceCredits CHECK (PriceCredits > 0)` so every booking snapshots the credit cost charged at booking time.
  - Update `trg_CreditTransactions_BookingAmounts`:
    - Check `i.Reason = 'mentor_booking' AND i.Delta <> -b.PriceCredits` (against the booking's snapshotted `PriceCredits`) and `booking_cancel` reversal against the original `mentor_booking` transaction.
  - Drop `MentorPayouts`, `MentorPayoutItems`, and `vw_MentorPayoutTotals` from `docs/database-schema.sql`.
  - Update `DataDisposition` (and `DataDispositionTests` + `EnumCodesTests`):
    - Remove `MentorPayouts` and `MentorPayoutItems` from `DataDisposition.Entries` and assert their absence alongside other retired tables.
    - Register `MentorWeeklySlots` with `Disposition.Preserve`.
    - Remove `MentorType` and `MentorTypeDto` from `BusinessObjects`, `Contracts`, `OperationsLabels`, and `EnumCodesTests`.

### 2. Weekly schedule & automatic next-occurrence slot materialization

- Admin configures a mentor's weekly schedule by replacing or updating the mentor's list of weekly availability windows (`DayOfWeek`, `StartTime`, `EndTime`).
- Service-level validation ensures:
  - `DayOfWeek` is between `1` (Monday) and `7` (Sunday).
  - `EndTime > StartTime`.
  - No two windows on the same `DayOfWeek` for the same mentor overlap (`a.StartTime < b.EndTime && b.StartTime < a.EndTime`).
- **Next-occurrence calculation (`UTC+7`)**:
  - Given `nowUtc` from `TimeProvider`, convert to Vietnam local time (`ReportingPeriod.LocalInstant(nowUtc)` at fixed `+07:00`).
  - For each `MentorWeeklySlot` `(DayOfWeek, StartTime, EndTime)`:
    - Find the earliest calendar date `d >= todayLocal` such that `IsoDayOfWeek(d) == DayOfWeek` and `DateTimeOffset(d, StartTime, +07:00) > nowUtc`.
    - Specifically: if `IsoDayOfWeek(todayLocal) == DayOfWeek` and `StartTime > nowLocal.TimeOfDay`, the next occurrence is on `todayLocal`; otherwise, it is `1..7` days ahead on the next matching weekday.
    - Compute `StartAt = new DateTimeOffset(d.ToDateTime(StartTime), ReportingPeriod.UtcOffset).ToUniversalTime()` and `EndAt = new DateTimeOffset(d.ToDateTime(EndTime), ReportingPeriod.UtcOffset).ToUniversalTime()`.
- **Materialization & synchronization**:
  - When Admin saves a mentor's weekly schedule, or when `GetMentorDetailAsync` / `GetAdminMentorsAsync` / `BookMentorAsync` runs:
    - The service ensures a concrete `MentorSlots` row exists for each active weekly window's next upcoming occurrence `(MentorId, StartAt, EndAt)`.
    - If Admin edits or removes a weekly window, any future concrete `MentorSlots` row (`StartAt > nowUtc`) for that mentor whose `(StartAt, EndAt)` no longer matches any of the mentor's weekly windows and has `Status == 'open'` (not booked) is marked `closed` (or removed if unreferenced). Any slot with `Status == 'booked'` (having an active booking) is **never** closed or deleted, preserving the learner's confirmed session.
- **Learner slot visibility (`GetMentorDetailAsync`)**:
  - Returns all upcoming occurrences (`StartAt > nowUtc` and `Status != 'closed'`) ordered by `StartAt`, including both `Status == 'open'` and `Status == 'booked'`.
  - On the learner UI (`MentorDetailPage`), slots with `Status == 'booked'` are rendered in the schedule list as disabled/greyed-out buttons with a `Booked` badge, preventing selection while showing the mentor's full weekly schedule.

### 3. Credit-based compensation (`1 Credit = 20,000 VND`) & CSV export

- Define `CreditExchangeRate.VndPerCredit = 20_000m` in `BusinessObjects`.
- When a learner books a session:
  - `Bookings.PriceCredits` is set to `mentor.PriceCredits`.
  - `Bookings.MeetLink` is set to `mentor.MeetLink`.
  - No `ISpeakingPracticeService` call or speaking exam lookup happens.
- **Completed-session payout eligibility**:
  - A booking is eligible for mentor payout in a reporting window `[from, to]` (where `from` and `to` are `DateOnly` reporting days cut at `UTC+7` via `ReportingPeriod`) when all of the following hold:
    1. `Booking.Status != 'cancelled'`
    2. `Slot.EndAt <= nowUtc` (the session has already finished)
    3. `Slot.EndAt` falls within `[StartOfDayUtc7(from), StartOfDayUtc7(to.AddDays(1)))`
    4. The booking has no resolved `mentor_no_show` complaint with a make-good grant (`!Booking.Complaints.Any(c => c.Type == 'mentor_no_show' && c.Status == 'resolved' && c.ResolvedByGrantTxnId != null)`).
- **Admin payout summary & export API**:
  - `GET /api/admin/mentors/payouts?from=yyyy-MM-dd&to=yyyy-MM-dd` returns:
    - Per-mentor totals (`MentorId`, `MentorName`, `CertCode`, `CompletedSessionsCount`, `TotalCredits`, `TotalAmountVnd`)
    - Completed session line items (`BookingId`, `SessionDate`, `StartAt`, `EndAt`, `MentorId`, `MentorName`, `LearnerName`, `LearnerEmail`, `CertCode`, `SkillName`, `PriceCredits`, `AmountVnd`)
    - Grand totals across all mentors for the period (`TotalSessions`, `TotalCredits`, `TotalAmountVnd`, `VndPerCredit`).
  - `GET /api/admin/mentors/payouts/export?from=yyyy-MM-dd&to=yyyy-MM-dd` returns `text/csv` (`mentor-payouts-{from}-to-{to}.csv`) produced by a pure formatter `MentorPayoutExportCsv.Format(rows, exportedAt)`:
    - Prepend UTF-8 BOM (`Encoding.UTF8.GetPreamble()`).
    - Header: `"Booking ID,Session date,Start time,End time,Mentor ID,Mentor name,Certificate,Skill,Learner email,Learner name,Credits,Money amount,Exported at"`.
    - Dates formatted as `yyyy-MM-dd` (`UTC+7`), times as `HH:mm` (`UTC+7`), `Money amount` as bare invariant decimal `0.00` (`PriceCredits * 20000.00`), RFC 4180 quoting on all fields.

### 4. Learner Dashboard upcoming session reminder

- Extend `LearnerDashboardDto` with `IReadOnlyList<UpcomingMentorBookingDto> UpcomingBookings` (defaulting to empty list):
  ```csharp
  public sealed record UpcomingMentorBookingDto(
      int BookingId,
      int MentorId,
      string MentorName,
      string CertCode,
      string? SkillName,
      DateTimeOffset StartAt,
      DateTimeOffset EndAt,
      string MeetLink,
      string? FocusArea);
  ```
- In `LearnerDashboardService`, query `db.Bookings` for the learner where `Status == 'confirmed'` and `Slot.EndAt > nowUtc`, ordered by `Slot.StartAt` ascending, across all certificates.
- On `DashboardPage.tsx`, render an upcoming mentor session reminder banner above the Hero Next Action block whenever `upcomingBookings.length > 0`, displaying the mentor name, certificate/skill badge, formatted date & time window, focus area, a link to `/mentors/bookings/{bookingId}`, and a primary **Join Meet** external link (`target="_blank" rel="noopener noreferrer"`) pointing to `meetLink`.

---

## Testing Decisions

A good test asserts observable domain outcomes at the module boundary — database constraints rejecting invalid rows, service calls returning expected DTOs and writing expected ledger transactions, pure CSV formatters returning exact UTF-8 BOM bytes, and UI components rendering disabled states and meeting links — never private helper methods or internal SQL strings.

1. **Schema test harness (`docs/schema-tests/domain09-mentor.sql` via `docs/schema-tests/run-all.sh`)**:
   - **What is tested**: `MentorProfiles` requires `MeetLink` and `PriceCredits > 0`; `MentorWeeklySlots` enforces `DayOfWeek BETWEEN 1 AND 7` and `EndTime > StartTime`; `Bookings` requires `SlotId` and snapshots `PriceCredits`; `trg_CreditTransactions_BookingAmounts` enforces `Delta == -Bookings.PriceCredits`; retired tables (`MentorPayouts`, `MentorPayoutItems`) and columns (`Type`, `MentorType`, `PricePerSlot`) are absent.
   - **Prior art**: `docs/schema-tests/domain09-mentor.sql`, `docs/schema-tests/domain52-refunds.sql`, `tests/Services.Tests/DataDispositionTests.cs`, `tests/Services.Tests/EnumCodesTests.cs`.

2. **Mentor service deep-module tests (`tests/Services.Tests/MentorServiceTests.cs` against real SQL Server)**:
   - **Seam**: `IMentorService`.
   - **What is tested**:
     - Creating and updating human mentors with a fixed `MeetLink`, `PriceCredits`, and weekly availability windows (`DayOfWeek`, multiple windows per day, rejecting overlapping windows on the same day or invalid `MeetLink`).
     - Automatic next-occurrence calculation in `UTC+7`: a window later today materializes for today; a window whose start time today has already passed materializes for next week (`+7 days`).
     - `GetMentorDetailAsync` returns both `open` and `booked` upcoming occurrences so the UI can grey out taken slots; once the clock advances past a booked occurrence's `StartAt`, the next week's occurrence is materialized as `open`.
     - Editing weekly availability windows closes unbooked future slots that no longer match the schedule while preserving any slot that already has a confirmed booking.
     - Booking a slot snapshots `MeetLink` and `PriceCredits` onto `Bookings` and charges `-PriceCredits` on the credit ledger; cancelling ≥24h before `StartAt` refunds the booking's snapshotted `PriceCredits` even if the mentor's `PriceCredits` was later changed by Admin.
     - Querying mentor payouts for a `from`..`to` reporting period (`UTC+7`): includes only non-cancelled bookings with `EndAt <= nowUtc` inside the window, calculates `AmountVnd = PriceCredits * 20,000`, and excludes bookings with a resolved `mentor_no_show` complaint carrying a make-good credit grant.
   - **Prior art**: `tests/Services.Tests/MentorServiceTests.cs`, `tests/Services.Tests/RefundServiceTests.cs`.

3. **Pure CSV formatter tests (`tests/Services.Tests/MentorPayoutExportCsvTests.cs`)**:
   - **Seam**: `MentorPayoutExportCsv.Format`.
   - **What is tested**: UTF-8 BOM prefix, header columns, Vietnamese diacritics preservation, RFC 4180 quoting, ISO-8601 date and `HH:mm` time formatting in `UTC+7`, and bare decimal money formatting (`0.00`).
   - **Prior art**: `tests/Services.Tests/RefundExportCsvTests.cs`.

4. **Learner dashboard service tests (`tests/Services.Tests/LearnerDashboardTests.cs` against real SQL Server)**:
   - **Seam**: `ILearnerDashboardService`.
   - **What is tested**: `GetDashboardAsync` populates `UpcomingBookings` with confirmed bookings whose `EndAt > nowUtc` (ordered by `StartAt`, carrying `MeetLink`, `MentorName`, `FocusArea`), and excludes cancelled or past bookings (`EndAt <= nowUtc`).
   - **Prior art**: `tests/Services.Tests/LearnerDashboardTests.cs`.

5. **Frontend Vitest component tests (`src/web/src/features/mentors/__tests__/mentors.test.tsx`, `src/web/src/features/admin/ops/__tests__/opsHelpers.test.ts`)**:
   - **Seam**: React + HeroUI v3 page components.
   - **What is tested**: `MentorDetailPage` renders `booked` slots as disabled/greyed-out buttons and `open` slots as selectable; `DashboardPage` renders the upcoming mentor booking reminder banner with its `Join Meet` link; `MentorsTab` renders weekly schedule configuration, fixed `MeetLink`, and payout CSV export without AI mentor controls.

---

## Out of Scope

- Automated Google Calendar / Google Meet OAuth API room provisioning (each mentor uses a fixed meeting link configured by Admin).
- Automated bank transfers for mentor payouts (the platform computes the credit and VND totals and exports the accounting CSV, matching the money-refund workflow).
- Multi-week calendar pagination for learners (learners always book the next upcoming occurrence of each weekly availability window within the rolling 7-day horizon).
- Changing the credit-to-VND conversion rate dynamically via a database table (pinned at `1 Credit = 20,000 VND` via `CreditExchangeRate.VndPerCredit` until the broader credit-system redesign).

---

## Further Notes

- All reporting day boundaries and weekly schedule calculations use the fixed Vietnam offset (`UTC+7`, `ReportingPeriod.UtcOffset`) so container timezone settings in Docker or CI never shift a mentor's weekly slot or payout reporting day.
