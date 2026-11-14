# Human-only mentors, weekly recurring availability, and credit-based payout export

## Status

Accepted.

## Context

The initial mentor domain mixed two unrelated concepts under one table (`MentorProfiles.Type IN ('human','ai')`): human 1-on-1 consultations and an "AI mentor" shortcut that charged mentor credits only to launch a speaking practice attempt. Human mentors also required a linked `Users` account (`CK_Mentor_TypeUserId`), generated fake random Google Meet URLs per booking, required admins to create one-off timestamped slots manually (`MentorSlots`), hid already-booked slots from learners instead of showing them as taken, and carried a separate `PricePerSlot` cash column alongside unimplemented `MentorPayouts` / `MentorPayoutItems` tables with no admin payout export.

## Decision

1. **Full replacement with zero legacy residue**:
   - Remove AI mentors and delete the `Type` / `MentorType` discriminator columns, constraints (`CK_Mentor_Type`, `CK_Mentor_TypeUserId`, `UQ_MentorProfiles_Id_Type`, `CK_Bookings_MentorType`, `FK_Bookings_MentorType`, `CK_Bookings_MentorTypeSlot`), and enums (`MentorType`, `MentorTypeDto`) across all layers.
   - Remove the unused `MentorPayouts`, `MentorPayoutItems`, and `vw_MentorPayoutTotals` database objects and their EF Core entity classes, as well as the separate `MentorProfiles.PricePerSlot` cash column and `MentorProfiles.UserId` requirement.
   - Remove the legacy manual slot creation/closing endpoints (`CreateMentorSlotRequest`, `CreateMentorSlotAsync`, `CloseMentorSlotAsync`), the speaking-attempt redirect coupling on `MentorService` (`ExamId`, `AttemptId`, `RedirectUrl`), and the standalone instructor console (`/instructor/sessions`).
2. **Admin-managed human mentors with fixed Meet links**:
   - Every row in `MentorProfiles` is an admin-authored human educator profile with a required `MeetLink` (`NVARCHAR(400) NOT NULL`) and credit price (`PriceCredits INT NOT NULL`), snapshotted onto `Bookings.MeetLink` and `Bookings.PriceCredits` at booking time.
3. **Weekly recurring availability (`MentorWeeklySlots`)**:
   - Admin configures a mentor's availability as recurring weekly windows `(DayOfWeek [1=Monday..7=Sunday], StartTime, EndTime)` in Vietnam reporting time (`UTC+7`). Multiple non-overlapping windows per day of the week are supported.
   - For each weekly window, the system automatically computes and materializes the next upcoming concrete occurrence (`StartAt`, `EndAt` > `now` within the rolling 7-day window) in `MentorSlots`. Editing or removing a weekly window closes or removes unbooked future occurrences while preserving any occurrence that already has a confirmed booking.
4. **Booked slots stay visible and disabled**:
   - Learner mentor detail queries return all upcoming occurrences for the mentor's weekly schedule, including `booked` slots (`Bookings.SlotId IS NOT NULL`), so the UI greys out taken time windows instead of hiding them. Once a booked occurrence passes, the next week's occurrence automatically becomes available.
5. **Credit-denominated compensation (`1 Credit = 20,000 VND`) and CSV export**:
   - A mentor carries a single price in credits (`PriceCredits`), snapshotted onto `Bookings.PriceCredits` at booking time (with VND payout derived via `CreditExchangeRate.VndPerCredit = 20,000 VND`).
   - A booking counts toward mentor payout once its session has ended (`EndAt <= now`), its status is not `cancelled`, and it has no resolved `mentor_no_show` complaint with a make-good credit grant. Admin can filter completed sessions by `ReportingPeriod` (`UTC+7` `from`..`to` dates) and export a UTF-8 BOM CSV following the same RFC 4180 contract as `RefundExportCsv`.
6. **Learner dashboard reminder**:
   - `LearnerDashboardDto` includes the learner's upcoming confirmed mentor bookings (`EndAt > now`) across certificates so the dashboard renders a reminder banner with session time, focus area, and a direct Join Meet link.
