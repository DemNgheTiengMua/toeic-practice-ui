using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public class MentorBookingException(string code, string message, int status = 400) : Exception(message)
{
    public const string InsufficientCredits = ApiError.Codes.InsufficientCredits;
    public const string SlotNotAvailable = ApiError.Codes.SlotNotAvailable;
    public const string BookingNotFound = ApiError.Codes.BookingNotFound;
    public const string BookingCannotCancel = ApiError.Codes.BookingCannotCancel;
    public const string BookingAlreadyRefunded = ApiError.Codes.BookingAlreadyRefunded;
    public const string BookingNotFinished = ApiError.Codes.BookingNotFinished;
    public const string BookingAlreadyRated = ApiError.Codes.BookingAlreadyRated;
    public const string MentorNotFound = ApiError.Codes.MentorNotFound;
    public const string ExamNotFound = ApiError.Codes.ExamNotFound;
    public const string Validation = ApiError.Codes.Validation;

    public string Code { get; } = code;
    public int Status { get; } = status;
}
