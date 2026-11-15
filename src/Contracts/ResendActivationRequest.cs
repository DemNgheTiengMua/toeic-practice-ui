using System.ComponentModel.DataAnnotations;

namespace Contracts;

/// <summary>ADR-0018: the address is the whole input, because the caller has no session to identify
/// the account with — an unverified account cannot sign in.</summary>
public sealed record ResendActivationRequest(
    [property: Required, EmailAddress] string Email);
