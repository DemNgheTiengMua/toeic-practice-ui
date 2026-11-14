SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'S','speaking',1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'w@x.com',0x00); DECLARE @u3 INT=SCOPE_IDENTITY();

-- Human mentor profile with fixed MeetLink and PriceCredits
INSERT INTO MentorProfiles (DisplayName,CertId,SkillId,MeetLink,PriceCredits,IsActive)
  VALUES (N'Coach',@c,@sk,N'https://meet.google.com/abc-defg-hij',2,1); DECLARE @m INT=SCOPE_IDENTITY();

-- Weekly availability schedule
INSERT INTO MentorWeeklySlots (MentorId,DayOfWeek,StartTime,EndTime)
  VALUES (@m,1,'09:00:00','10:00:00'), (@m,3,'19:00:00','20:00:00');
PRINT 'valid MentorWeeklySlots accepted';

-- reject: DayOfWeek out of range 1..7
BEGIN TRY
  INSERT INTO MentorWeeklySlots (MentorId,DayOfWeek,StartTime,EndTime)
    VALUES (@m,0,'09:00:00','10:00:00');
  THROW 50000,'EXPECT_REJECT FAILED: DayOfWeek 0 accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected DayOfWeek 0 as expected';
END CATCH

BEGIN TRY
  INSERT INTO MentorWeeklySlots (MentorId,DayOfWeek,StartTime,EndTime)
    VALUES (@m,8,'09:00:00','10:00:00');
  THROW 50000,'EXPECT_REJECT FAILED: DayOfWeek 8 accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected DayOfWeek 8 as expected';
END CATCH

-- reject: EndTime <= StartTime on MentorWeeklySlots
BEGIN TRY
  INSERT INTO MentorWeeklySlots (MentorId,DayOfWeek,StartTime,EndTime)
    VALUES (@m,2,'10:00:00','09:00:00');
  THROW 50000,'EXPECT_REJECT FAILED: EndTime <= StartTime accepted on MentorWeeklySlots',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected invalid weekly slot range as expected';
END CATCH

-- reject: PriceCredits <= 0 on MentorProfiles
BEGIN TRY
  INSERT INTO MentorProfiles (DisplayName,CertId,MeetLink,PriceCredits,IsActive)
    VALUES (N'BadPrice',@c,N'https://meet.google.com/new',0,1);
  THROW 50000,'EXPECT_REJECT FAILED: PriceCredits 0 accepted on MentorProfiles',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected PriceCredits <= 0 on MentorProfiles as expected';
END CATCH

INSERT INTO MentorSlots (MentorId,StartAt,EndAt,Status)
  VALUES (@m,SYSDATETIMEOFFSET(),DATEADD(hour,1,SYSDATETIMEOFFSET()),'booked'); DECLARE @slot INT=SCOPE_IDENTITY();
INSERT INTO Bookings (UserId,MentorId,SlotId,PriceCredits,Status,MeetLink)
  VALUES (@u,@m,@slot,2,'confirmed',N'https://meet.google.com/abc-defg-hij');
PRINT 'valid booking accepted';

-- reject (Review Focus #5): a second booking on the same booked slot
BEGIN TRY
  INSERT INTO Bookings (UserId,MentorId,SlotId,PriceCredits,Status) VALUES (@u,@m,@slot,2,'pending');
  THROW 50000,'EXPECT_REJECT FAILED: double-booked slot accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected double-booked slot as expected';
END CATCH

-- reject: NULL SlotId on Bookings
BEGIN TRY
  INSERT INTO Bookings (UserId,MentorId,SlotId,PriceCredits,Status) VALUES (@u,@m,NULL,2,'confirmed');
  THROW 50000,'EXPECT_REJECT FAILED: NULL SlotId accepted on Bookings',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected NULL SlotId on Bookings as expected';
END CATCH

-- reject: PriceCredits <= 0 on Bookings
INSERT INTO MentorSlots (MentorId,StartAt,EndAt,Status)
  VALUES (@m,DATEADD(hour,4,SYSDATETIMEOFFSET()),DATEADD(hour,5,SYSDATETIMEOFFSET()),'open'); DECLARE @slotPrice INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO Bookings (UserId,MentorId,SlotId,PriceCredits,Status) VALUES (@u,@m,@slotPrice,0,'confirmed');
  THROW 50000,'EXPECT_REJECT FAILED: PriceCredits 0 accepted on Bookings',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected PriceCredits <= 0 on Bookings as expected';
END CATCH

-- reject: review rating out of 1-5
BEGIN TRY
  INSERT INTO Reviews (BookingId,UserId,Rating,Text)
    SELECT TOP 1 BookingId,@u,7,N'x' FROM Bookings;
  THROW 50000,'EXPECT_REJECT FAILED: rating 7 accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected bad rating as expected';
END CATCH

-- ===== Batch 2: mentor ownership & aggregates =====
-- reject (PartC-1): MentorId disagreeing with the slot's owner
INSERT INTO MentorProfiles (DisplayName,CertId,MeetLink,PriceCredits,IsActive)
  VALUES (N'OtherMentor',@c,N'https://meet.google.com/xyz-abcd-efg',1,1); DECLARE @m2 INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO Bookings (UserId,MentorId,SlotId,PriceCredits,Status) VALUES (@u,@m2,@slotPrice,1,'pending');
  THROW 50000,'EXPECT_REJECT FAILED: booking MentorId disagrees with slot owner',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected booking/slot mentor mismatch as expected';
END CATCH

-- reject (M-15): a booking on a closed slot
INSERT INTO MentorSlots (MentorId,StartAt,EndAt,Status)
  VALUES (@m,DATEADD(hour,2,SYSDATETIMEOFFSET()),DATEADD(hour,3,SYSDATETIMEOFFSET()),'closed'); DECLARE @closedSlot INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO Bookings (UserId,MentorId,SlotId,PriceCredits,Status) VALUES (@u,@m,@closedSlot,2,'pending');
  THROW 50000,'EXPECT_REJECT FAILED: booking on closed slot accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected booking on closed slot as expected';
END CATCH

-- reject (PartC-3): a review written by a non-booker
BEGIN TRY
  INSERT INTO Reviews (BookingId,UserId,Rating,Text)
    SELECT TOP 1 BookingId,@u3,4,N'not my booking' FROM Bookings WHERE MentorId=@m;
  THROW 50000,'EXPECT_REJECT FAILED: review by non-booker accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected review by non-booker as expected';
END CATCH

-- reject (PartC-4): a complaint filed by a non-booker
BEGIN TRY
  INSERT INTO Complaints (BookingId,UserId,Type,Reason)
    SELECT TOP 1 BookingId,@u3,'mentor_quality',N'not my booking' FROM Bookings WHERE MentorId=@m;
  THROW 50000,'EXPECT_REJECT FAILED: complaint by non-booker accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected complaint by non-booker as expected';
END CATCH

-- vw_MentorAvgRating returns the expected average
INSERT INTO Reviews (BookingId,UserId,Rating,Text)
  SELECT TOP 1 BookingId,@u,4,N'good' FROM Bookings WHERE MentorId=@m AND BookingId NOT IN (SELECT BookingId FROM Reviews);
DECLARE @avg DECIMAL(3,2) = (SELECT AvgRating FROM vw_MentorAvgRating WHERE MentorId=@m);
IF @avg IS NULL THROW 50000,'vw_MentorAvgRating returned no row for mentor',1;
PRINT 'vw_MentorAvgRating returned expected average: ' + CAST(@avg AS VARCHAR(10));

-- ===== re-review residual (R6): a DELETED booking must free its slot =====
INSERT INTO MentorSlots (MentorId,StartAt,EndAt)
  VALUES (@m,'2026-12-01T10:00:00+07:00','2026-12-01T11:00:00+07:00');
DECLARE @slotDel INT=SCOPE_IDENTITY();
INSERT INTO Bookings (UserId,MentorId,SlotId,PriceCredits,Status)
  VALUES (@u,@m,@slotDel,2,'confirmed');
DECLARE @bkDel INT=SCOPE_IDENTITY();
IF (SELECT Status FROM MentorSlots WHERE SlotId=@slotDel) <> 'booked'
  THROW 50000,'setup: slot did not sync to booked',1;
DELETE FROM Bookings WHERE BookingId=@bkDel;
IF (SELECT Status FROM MentorSlots WHERE SlotId=@slotDel) <> 'open'
  THROW 50000,'EXPECT FAILED: deleting a booking left the slot booked',1;
PRINT 'slot freed after booking delete as expected';

-- ===== Spec #307: verify retired tables and columns are absent =====
IF OBJECT_ID('dbo.MentorPayouts') IS NOT NULL
   OR OBJECT_ID('dbo.MentorPayoutItems') IS NOT NULL
   OR OBJECT_ID('dbo.vw_MentorPayoutTotals') IS NOT NULL
  THROW 50000,'EXPECT FAILED: retired MentorPayouts/MentorPayoutItems/vw_MentorPayoutTotals still exist',1;

IF COL_LENGTH('dbo.MentorProfiles', 'Type') IS NOT NULL
   OR COL_LENGTH('dbo.MentorProfiles', 'PricePerSlot') IS NOT NULL
   OR COL_LENGTH('dbo.MentorProfiles', 'UserId') IS NOT NULL
   OR COL_LENGTH('dbo.Bookings', 'MentorType') IS NOT NULL
  THROW 50000,'EXPECT FAILED: retired MentorProfiles/Bookings columns still exist',1;
PRINT 'retired mentor payout tables and legacy columns confirmed absent';
