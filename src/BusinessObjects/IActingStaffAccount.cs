namespace BusinessObjects;

/// <summary>
/// #201 / ADM-03: who is writing right now, as an account id, or NULL when nobody is (a seed, a
/// fixture, a migration, a background worker).
///
/// The database cannot answer this question on its own: every request reaches one SQL principal, so
/// "which staff account is this" is not recoverable from the connection. The acting account therefore
/// travels as a column on the write - never `sp_set_session_context`, which survives commit and
/// rollback on a pooled connection and would vouch for whatever transaction ran next (PRN-01).
///
/// A NULL answer is not an error: it means "not a staff act", and the triggers treat it as naming
/// nobody, which no invariant can be violated by.
/// </summary>
public interface IActingStaffAccount
{
    int? UserId { get; }
}
