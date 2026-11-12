using Contracts;

namespace Services;

/// <summary>
/// #281: a refund refused at the service boundary. It carries an <see cref="ApiError.Codes"/> value
/// rather than a message, because the screen decides what to show and two ceilings must not read the
/// same. The shape mirrors <see cref="ComplaintException"/>, the resolution flow this models on.
/// </summary>
public class RefundException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;

    public int Status { get; } = status;
}

/// <summary>#281: the refund named does not exist. Its own type so the endpoint can answer 404
/// without matching on a code string.</summary>
public sealed class RefundNotFoundException(int refundId)
    : RefundException(ApiError.Codes.RefundNotFound, $"Refund {refundId} was not found.", 404);
