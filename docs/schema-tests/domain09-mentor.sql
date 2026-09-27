SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'S','speaking',1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'mentor@x.com',0x00); DECLARE @mu INT=SCOPE_IDENTITY();
INSERT INTO MentorProfiles (UserId,DisplayName,CertId,SkillId,Type,PricePerSlot,IsActive)
  VALUES (@mu,N'Coach',@c,@sk,'human',5,1); DECLARE @m INT=SCOPE_IDENTITY();
INSERT INTO MentorSlots (MentorId,StartAt,EndAt,Status)
  VALUES (@m,SYSDATETIMEOFFSET(),DATEADD(hour,1,SYSDATETIMEOFFSET()),'booked'); DECLARE @slot INT=SCOPE_IDENTITY();
INSERT INTO Bookings (UserId,MentorId,SlotId,MentorType,Status,MeetLink) VALUES (@u,@m,@slot,'human','confirmed',N'https://meet');
PRINT 'valid booking accepted';
-- reject (Review Focus #5): a second booking on the same booked slot
BEGIN TRY
  INSERT INTO Bookings (UserId,MentorId,SlotId,MentorType,Status) VALUES (@u,@m,@slot,'human','pending');
  THROW 50000,'EXPECT_REJECT FAILED: double-booked slot accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected double-booked slot as expected';
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
INSERT INTO MentorProfiles (UserId,DisplayName,CertId,Type,PricePerSlot,IsActive)
  VALUES (NULL,N'OtherMentor',@c,'ai',0,1); DECLARE @m2 INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO Bookings (UserId,MentorId,SlotId,MentorType,Status) VALUES (@u,@m2,@slot,'ai','pending');
  THROW 50000,'EXPECT_REJECT FAILED: booking MentorId disagrees with slot owner',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected booking/slot mentor mismatch as expected';
END CATCH

-- AI booking (NULL SlotId) stays legal under MATCH SIMPLE
INSERT INTO Bookings (UserId,MentorId,SlotId,MentorType,Status) VALUES (@u,@m2,NULL,'ai','confirmed');
PRINT 'AI booking with NULL SlotId accepted as expected';

-- reject (M-14): human mentor with NULL UserId
BEGIN TRY
  INSERT INTO MentorProfiles (UserId,DisplayName,CertId,Type,PricePerSlot,IsActive)
    VALUES (NULL,N'BadHuman',@c,'human',5,1);
  THROW 50000,'EXPECT_REJECT FAILED: human mentor with NULL UserId accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected human mentor with NULL UserId as expected';
END CATCH

-- reject (M-14): ai mentor with a UserId
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'w@x.com',0x00); DECLARE @u3 INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO MentorProfiles (UserId,DisplayName,CertId,Type,PricePerSlot,IsActive)
    VALUES (@u3,N'BadAi',@c,'ai',0,1);
  THROW 50000,'EXPECT_REJECT FAILED: ai mentor with a UserId accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected ai mentor with a UserId as expected';
END CATCH

-- reject (M-15): a booking on a closed slot
INSERT INTO MentorSlots (MentorId,StartAt,EndAt,Status)
  VALUES (@m,DATEADD(hour,2,SYSDATETIMEOFFSET()),DATEADD(hour,3,SYSDATETIMEOFFSET()),'closed'); DECLARE @closedSlot INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO Bookings (UserId,MentorId,SlotId,MentorType,Status) VALUES (@u,@m,@closedSlot,'human','pending');
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
  INSERT INTO Complaints (BookingId,UserId,Reason)
    SELECT TOP 1 BookingId,@u3,N'not my booking' FROM Bookings WHERE MentorId=@m;
  THROW 50000,'EXPECT_REJECT FAILED: complaint by non-booker accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected complaint by non-booker as expected';
END CATCH

-- reject (PartC-2): a payout item crossing mentors
INSERT INTO MentorPayouts (MentorId,PeriodStart,PeriodEnd,Status)
  VALUES (@m2,SYSDATETIMEOFFSET(),DATEADD(day,7,SYSDATETIMEOFFSET()),'pending'); DECLARE @payout INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO MentorPayoutItems (PayoutId,BookingId,MentorId)
    SELECT @payout,BookingId,@m2 FROM Bookings WHERE MentorId=@m;
  THROW 50000,'EXPECT_REJECT FAILED: payout item crossing mentors accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-mentor payout item as expected';
END CATCH

-- vw_MentorAvgRating returns the expected average
INSERT INTO Reviews (BookingId,UserId,Rating,Text)
  SELECT TOP 1 BookingId,@u,4,N'good' FROM Bookings WHERE MentorId=@m AND BookingId NOT IN (SELECT BookingId FROM Reviews);
DECLARE @avg DECIMAL(3,2) = (SELECT AvgRating FROM vw_MentorAvgRating WHERE MentorId=@m);
IF @avg IS NULL THROW 50000,'vw_MentorAvgRating returned no row for mentor',1;
PRINT 'vw_MentorAvgRating returned expected average: ' + CAST(@avg AS VARCHAR(10));

-- ===== re-review residual (R6): a DELETED booking must free its slot =====
-- The sync trigger was INSERT,UPDATE only, so deleting a booking left the slot 'booked' forever
-- and that time could never be offered again.
INSERT INTO MentorSlots (MentorId,StartAt,EndAt)
  VALUES (@m,'2026-12-01T10:00:00+07:00','2026-12-01T11:00:00+07:00');
DECLARE @slotDel INT=SCOPE_IDENTITY();
INSERT INTO Bookings (UserId,MentorId,SlotId,MentorType,Status)
  VALUES (@u,@m,@slotDel,'human','confirmed');
DECLARE @bkDel INT=SCOPE_IDENTITY();
IF (SELECT Status FROM MentorSlots WHERE SlotId=@slotDel) <> 'booked'
  THROW 50000,'setup: slot did not sync to booked',1;
DELETE FROM Bookings WHERE BookingId=@bkDel;
IF (SELECT Status FROM MentorSlots WHERE SlotId=@slotDel) <> 'open'
  THROW 50000,'EXPECT FAILED: deleting a booking left the slot booked',1;
PRINT 'slot freed after booking delete as expected';
