using System.Text.Json;
using Contracts;

namespace WebMVC.Models;

public sealed record ExamPreflightViewModel(
    PreflightStatusDto Preflight,
    string? ErrorMessage = null,
    int? PlacementId = null)
{
    public string PageTitle { get; } = $"Pre-flight Check — {Preflight.ExamName}";
    public string PageDescription { get; } = $"Equipment readiness and asset verification for {Preflight.ExamName}.";
    public bool HasError { get; } = !string.IsNullOrEmpty(ErrorMessage);
    public string InstructionsUrl { get; } = PlacementId.HasValue ? "/placement" : $"/exams/{Preflight.ExamId}/instructions";
    public string VerifyUrl { get; } = $"/exams/preflight/{Preflight.AttemptId}/verify" + (PlacementId.HasValue ? $"?placementId={PlacementId.Value}" : "");
    public string VerificationBadgeClass { get; } = Preflight.IsVerified ? "badge--success" : "badge--warning";
    public string GateStatusLabel { get; } = Preflight.IsVerified ? "VERIFIED & READY" : "CHECKS PENDING";
    public string MicrophoneStatusLabel { get; } = Preflight.IsVerified ? "MIC VERIFIED" : "CHECK REQUIRED";
    public string BufferStatusLabel { get; } = Preflight.AudioUrls.Count > 0 ? "PRE-BUFFERING..." : "NO AUDIO REQUIRED";
    public int InitialBufferPercent { get; } = Preflight.AudioUrls.Count > 0 ? 0 : 100;
    public string BufferPercentageLabel { get; } = Preflight.AudioUrls.Count > 0 ? "0%" : "100%";
    public string BufferProgressClass { get; } = Preflight.AudioUrls.Count > 0 ? "" : "preflight-buffer-complete";
    public string BufferDetails { get; } = Preflight.AudioUrls.Count > 0
        ? $"0 of {Preflight.AudioUrls.Count} audio tracks pre-buffered"
        : "No listening audio tracks in this exam blueprint";
    // Keep the default HTML-safe encoder: this JSON is rendered as raw script data, never executable code.
    public string PageDataJson { get; } = JsonSerializer.Serialize(new
    {
        audioUrls = Preflight.AudioUrls,
        requiresMic = Preflight.RequiresMicrophone,
        isAlreadyVerified = Preflight.IsVerified
    });
}
