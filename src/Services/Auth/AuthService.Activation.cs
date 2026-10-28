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

public sealed partial class AuthService
{

    public async Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (user, now) = await ConsumeAsync(db.EmailVerificationTokens, token, cancellationToken);

        user.EmailVerifiedAt ??= now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResendActivationAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .SingleOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted && !u.IsLocked, cancellationToken);
        if (user is null || user.EmailVerifiedAt is not null)
        {
            return;
        }

        await SendActivationAsync(user, cancellationToken);
    }
}
