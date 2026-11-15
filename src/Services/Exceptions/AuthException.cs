using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Services;

/// <summary>Thrown when a caller asks for something identity refuses: duplicate email at
/// registration, bad credentials at sign-in. <see cref="Code"/> is the API's stable error code.</summary>
public sealed class AuthException(string code, string message) : Exception(message)
{
    public const string DuplicateEmail = AuthErrorCodes.EmailTaken;
    public const string InvalidCredentials = AuthErrorCodes.InvalidCredentials;
    public const string Locked = AuthErrorCodes.Locked;
    public const string TokenInvalid = AuthErrorCodes.TokenInvalid;

    /// <summary>Correct credentials, but the mailbox was never proven. ADR-0018: verification gates
    /// sign-in itself, so this refuses the token rather than limiting what the token can do.</summary>
    public const string EmailUnverified = AuthErrorCodes.EmailUnverified;

    public string Code { get; } = code;
}
