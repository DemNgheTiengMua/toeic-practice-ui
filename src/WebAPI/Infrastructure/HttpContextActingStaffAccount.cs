using System.Security.Claims;
using BusinessObjects;
using Contracts;

namespace WebAPI.Infrastructure;

/// <summary>
/// #201: the acting staff account, read from the JWT the request already carries.
///
/// It returns the claim for every authenticated caller, including learners. That is deliberate: the
/// triggers read it as "who declared this write", and a learner writing to a guarded table is a
/// separate defect the value makes visible rather than hides. Deciding "is this staff" is the
/// trigger's job, from the permission tables, not this class's.
/// </summary>
public sealed class HttpContextActingStaffAccount(IHttpContextAccessor accessor) : IActingStaffAccount
{
    public int? UserId =>
        int.TryParse(accessor.HttpContext?.User.FindFirst(IAccessTokenFactory.ClaimUserId)?.Value, out var id)
            ? id
            : null;
}
