using System;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

/// <summary>
/// #156: detection of a divergence between the placement result and observed practice, and the
/// learner's decision about what to do with it. It never corrects anything on its own.
/// </summary>
public interface ICalibrationService
{
    /// <summary>What the evidence currently supports, including the reason when it supports nothing.</summary>
    Task<CalibrationView> GetCalibrationAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the learner's answer to the current proposal: <paramref name="adjust"/> true lowers
    /// the path's starting band, false leaves it exactly as it was.
    /// </summary>
    Task<CalibrationDecisionResult> DecideAsync(int userId, bool adjust, CancellationToken cancellationToken = default);
}

/// <summary>#156: a calibration refusal that the screen can explain rather than log as a fault.</summary>
public sealed class CalibrationException : Exception
{
    public const string NoProposal = "CALIBRATION_NO_PROPOSAL";

    public CalibrationException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
