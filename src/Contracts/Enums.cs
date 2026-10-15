using System.Text.Json.Serialization;

namespace Contracts;

// Wire mirrors of the BusinessObjects enums (issue #2: enums for status strings). Each member
// serialises as the database code it stands for, so the JSON on the wire is unchanged
// ("in_progress", not "InProgress"). Named <Domain>Dto like every Contracts type, so files importing both namespaces stay unambiguous.

/// <summary>CK_Skills_Modality: listening, reading, speaking, writing.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ModalityDto>))]
public enum ModalityDto
{
    [JsonStringEnumMemberName("listening")] Listening,
    [JsonStringEnumMemberName("reading")] Reading,
    [JsonStringEnumMemberName("speaking")] Speaking,
    [JsonStringEnumMemberName("writing")] Writing,
}

/// <summary>CK_Questions_Type: mcq, free_response.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuestionTypeDto>))]
public enum QuestionTypeDto
{
    [JsonStringEnumMemberName("mcq")] Mcq,
    [JsonStringEnumMemberName("free_response")] FreeResponse,
}

/// <summary>CK_Exams_Status: draft, published, hidden.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExamStatusDto>))]
public enum ExamStatusDto
{
    [JsonStringEnumMemberName("draft")] Draft,
    [JsonStringEnumMemberName("published")] Published,
    [JsonStringEnumMemberName("hidden")] Hidden,
}

/// <summary>CK_Exams_Purpose: mock, placement, speaking_practice, writing_practice.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExamPurposeDto>))]
public enum ExamPurposeDto
{
    [JsonStringEnumMemberName("mock")] Mock,
    [JsonStringEnumMemberName("placement")] Placement,
    [JsonStringEnumMemberName("speaking_practice")] SpeakingPractice,
    [JsonStringEnumMemberName("writing_practice")] WritingPractice,
}

/// <summary>CK_Attempts_Status: in_progress, submitted, graded, abandoned.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AttemptStatusDto>))]
public enum AttemptStatusDto
{
    [JsonStringEnumMemberName("in_progress")] InProgress,
    [JsonStringEnumMemberName("submitted")] Submitted,
    [JsonStringEnumMemberName("graded")] Graded,
    [JsonStringEnumMemberName("abandoned")] Abandoned,
}

/// <summary>CK_FreeResp_Status: draft, pending_ai, graded.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FreeResponseStatusDto>))]
public enum FreeResponseStatusDto
{
    [JsonStringEnumMemberName("draft")] Draft,
    [JsonStringEnumMemberName("pending_ai")] PendingAi,
    [JsonStringEnumMemberName("graded")] Graded,
}

/// <summary>CK_AiGradings_Status: pending, done, failed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AiGradingStatusDto>))]
public enum AiGradingStatusDto
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("failed")] Failed,
}

/// <summary>CK_Practice_Status: in_progress, finished, abandoned.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PracticeStatusDto>))]
public enum PracticeStatusDto
{
    [JsonStringEnumMemberName("in_progress")] InProgress,
    [JsonStringEnumMemberName("finished")] Finished,
    [JsonStringEnumMemberName("abandoned")] Abandoned,
}

/// <summary>CK_Practice_Source: all, wrong_before, unseen.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuestionSourceDto>))]
public enum QuestionSourceDto
{
    [JsonStringEnumMemberName("all")] All,
    [JsonStringEnumMemberName("wrong_before")] WrongBefore,
    [JsonStringEnumMemberName("unseen")] Unseen,
}

/// <summary>CK_Practice_Feedback: immediate, end.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FeedbackModeDto>))]
public enum FeedbackModeDto
{
    [JsonStringEnumMemberName("immediate")] Immediate,
    [JsonStringEnumMemberName("end")] End,
}

/// <summary>CK_SavedQ_Source: manual, incorrect.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SavedQuestionSourceDto>))]
public enum SavedQuestionSourceDto
{
    [JsonStringEnumMemberName("manual")] Manual,
    [JsonStringEnumMemberName("incorrect")] Incorrect,
}

/// <summary>CK_Reco_Severity: info, minor, major.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecommendationSeverityDto>))]
public enum RecommendationSeverityDto
{
    [JsonStringEnumMemberName("info")] Info,
    [JsonStringEnumMemberName("minor")] Minor,
    [JsonStringEnumMemberName("major")] Major,
}

/// <summary>CK_Orders_Status: pending, reconciling, paid, failed, expired.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrderStatusDto>))]
public enum OrderStatusDto
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("reconciling")] Reconciling,
    [JsonStringEnumMemberName("paid")] Paid,
    [JsonStringEnumMemberName("failed")] Failed,
    [JsonStringEnumMemberName("expired")] Expired,
}

/// <summary>CK_Orders_Gateway: payos, mock.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentGatewayDto>))]
public enum PaymentGatewayDto
{
    [JsonStringEnumMemberName("payos")] Payos,
    [JsonStringEnumMemberName("mock")] Mock,
}

/// <summary>CK_PaymentEvents_Source: webhook, status_query, return_redirect, cancel.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentEventSourceDto>))]
public enum PaymentEventSourceDto
{
    [JsonStringEnumMemberName("webhook")] Webhook,
    [JsonStringEnumMemberName("status_query")] StatusQuery,
    [JsonStringEnumMemberName("return_redirect")] ReturnRedirect,
    [JsonStringEnumMemberName("cancel")] Cancel,
}

/// <summary>CK_PaymentEvents_ReportedStatus: pending, paid, failed, expired, cancelled.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GatewayStatusDto>))]
public enum GatewayStatusDto
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("paid")] Paid,
    [JsonStringEnumMemberName("failed")] Failed,
    [JsonStringEnumMemberName("expired")] Expired,
    [JsonStringEnumMemberName("cancelled")] Cancelled,
}

/// <summary>CK_CreditTxn_Reason: purchase, admin_grant, mock_exam_start, ai_grading, mentor_booking, premium_path, admin_revoke, booking_cancel.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CreditReasonDto>))]
public enum CreditReasonDto
{
    [JsonStringEnumMemberName("purchase")] Purchase,
    [JsonStringEnumMemberName("admin_grant")] AdminGrant,
    [JsonStringEnumMemberName("mock_exam_start")] MockExamStart,
    [JsonStringEnumMemberName("ai_grading")] AiGrading,
    [JsonStringEnumMemberName("mentor_booking")] MentorBooking,
    [JsonStringEnumMemberName("premium_path")] PremiumPath,
    [JsonStringEnumMemberName("admin_revoke")] AdminRevoke,
    [JsonStringEnumMemberName("booking_cancel")] BookingCancel,
}
