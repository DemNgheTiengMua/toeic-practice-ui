namespace BusinessObjects;

// Domain enums for the schema's CHECK-constrained codes (issue #2: enums for status strings).
// Entities stay string, as scaffolded; Services converts with EnumCodes, where each member's
// snake_case name is the database code. Keep members in step with docs/database-schema.sql
// and with the wire mirrors (<Name>Dto) in Contracts/Enums.cs.

/// <summary>CK_Skills_Modality: listening, reading, speaking, writing.</summary>
public enum Modality
{
    Listening,
    Reading,
    Speaking,
    Writing,
}

/// <summary>CK_Questions_Type: mcq, free_response.</summary>
public enum QuestionType
{
    Mcq,
    FreeResponse,
}

/// <summary>CK_Exams_Status: draft, published, hidden.</summary>
public enum ExamStatus
{
    Draft,
    Published,
    Hidden,
}

/// <summary>CK_Exams_Purpose: mock, placement, speaking_practice, writing_practice.</summary>
public enum ExamPurpose
{
    Mock,
    Placement,
    SpeakingPractice,
    WritingPractice,
}

/// <summary>CK_Attempts_Status: in_progress, submitted, graded, abandoned.</summary>
public enum AttemptStatus
{
    InProgress,
    Submitted,
    Graded,
    Abandoned,
}

/// <summary>CK_Placement_Status: in_progress, done, abandoned.</summary>
public enum PlacementStatus
{
    InProgress,
    Done,
    Abandoned,
}

/// <summary>CK_FreeResp_Status: draft, pending_ai, graded.</summary>
public enum FreeResponseStatus
{
    Draft,
    PendingAi,
    Graded,
}

/// <summary>CK_AiGradings_Status: pending, done, failed.</summary>
public enum AiGradingStatus
{
    Pending,
    Done,
    Failed,
}

/// <summary>CK_AiInsights_Scope: practice_session, attempt, path, dashboard.</summary>
public enum AiInsightScope
{
    PracticeSession,
    Attempt,
    Path,
    Dashboard,
}

/// <summary>CK_AiInsights_Status: pending, done, failed.</summary>
public enum AiInsightStatus
{
    Pending,
    Done,
    Failed,
}

/// <summary>CK_AiInsights_Trend: improving, flat, declining.</summary>
public enum AiInsightTrend
{
    Improving,
    Flat,
    Declining,
}

/// <summary>CK_Practice_Status: in_progress, finished, abandoned.</summary>
public enum PracticeStatus
{
    InProgress,
    Finished,
    Abandoned,
}

/// <summary>CK_Practice_Source: all, wrong_before, unseen.</summary>
public enum QuestionSource
{
    All,
    WrongBefore,
    Unseen,
}

/// <summary>CK_Practice_Feedback: immediate, end.</summary>
public enum FeedbackMode
{
    Immediate,
    End,
}

/// <summary>CK_SavedQ_Source: manual, incorrect.</summary>
public enum SavedQuestionSource
{
    Manual,
    Incorrect,
}

/// <summary>CK_Reco_Severity: info, minor, major.</summary>
public enum RecommendationSeverity
{
    Info,
    Minor,
    Major,
}

/// <summary>CK_Orders_Status: pending, reconciling, paid, failed, expired.</summary>
public enum OrderStatus
{
    Pending,
    Reconciling,
    Paid,
    Failed,
    Expired,
}

/// <summary>CK_Orders_Gateway: payos, mock.</summary>
public enum PaymentGateway
{
    Payos,
    Mock,
}

/// <summary>CK_PaymentEvents_Source: webhook, status_query, return_redirect, cancel.</summary>
public enum PaymentEventSource
{
    Webhook,
    StatusQuery,
    ReturnRedirect,
    Cancel,
}

/// <summary>CK_PaymentEvents_ReportedStatus: pending, paid, failed, expired, cancelled.</summary>
public enum GatewayStatus
{
    Pending,
    Paid,
    Failed,
    Expired,
    Cancelled,
}
/// <summary>CK_CreditTxn_Reason: purchase, admin_grant, mock_exam_start, ai_grading, mentor_booking, premium_path, admin_revoke, booking_cancel.</summary>
public enum CreditReason
{
    Purchase,
    AdminGrant,
    MockExamStart,
    AiGrading,
    MentorBooking,
    PremiumPath,
    AdminRevoke,
    BookingCancel,
}


/// <summary>CK_LearnerPaths_Status: active, completed, reset.</summary>
public enum LearnerPathStatus
{
    Active,
    Completed,
    Reset,
}

/// <summary>CK_Steps_Status: locked, available, in_progress, done.</summary>
public enum LearnerPathStepStatus
{
    Locked,
    Available,
    InProgress,
    Done,
}

/// <summary>CK_PMI_Type: practice_section, mock_exam, ai_task, mentor.</summary>
public enum PathModuleItemType
{
    PracticeSection,
    MockExam,
    AiTask,
    Mentor,
}

