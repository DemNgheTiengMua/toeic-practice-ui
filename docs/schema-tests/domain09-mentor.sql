SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'S','speaking',1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO MentorProfiles (DisplayName,CertId,SkillId,Type,PricePerSlot,IsActive)
  VALUES (N'Coach',@c,@sk,'human',5,1); DECLARE @m INT=SCOPE_IDENTITY();
INSERT INTO MentorSlots (MentorId,StartAt,EndAt,Status)
  VALUES (@m,SYSDATETIMEOFFSET(),DATEADD(hour,1,SYSDATETIMEOFFSET()),'booked'); DECLARE @slot INT=SCOPE_IDENTITY();
INSERT INTO Bookings (UserId,MentorId,SlotId,Status,MeetLink) VALUES (@u,@m,@slot,'confirmed',N'https://meet');
PRINT 'valid booking accepted';
-- reject (Review Focus #5): a second booking on the same booked slot
BEGIN TRY
  INSERT INTO Bookings (UserId,MentorId,SlotId,Status) VALUES (@u,@m,@slot,'pending');
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
