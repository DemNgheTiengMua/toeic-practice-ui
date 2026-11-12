namespace Contracts;

/// <summary>
/// The error body every API failure carries. <see cref="Code"/> is the stable machine-readable
/// key the MVC site switches on; the rest follows RFC 9457 ProblemDetails.
/// </summary>
public sealed record ApiError(string Code, string Title, int Status, string? Detail = null)
{
    public static class Codes
    {
        public const string Unexpected = "UNEXPECTED";

        public const string NotFound = "NOT_FOUND";

        public const string Validation = "VALIDATION";

        /// <summary>Registration with an email that already has a live account.</summary>
        public const string EmailTaken = AuthErrorCodes.EmailTaken;

        /// <summary>Unknown email or wrong password at sign-in (deliberately indistinguishable).</summary>
        public const string InvalidCredentials = AuthErrorCodes.InvalidCredentials;

        /// <summary>A locked account, at sign-in or on any later request.</summary>
        public const string Locked = AuthErrorCodes.Locked;

        /// <summary>Too many requests / rate limit exceeded (HTTP 429).</summary>
        public const string TooManyRequests = "TOO_MANY_REQUESTS";

        /// <summary>No valid bearer token on a request that needs one.</summary>
        public const string Unauthenticated = "AUTH_REQUIRED";

        /// <summary>Signed in, but the role doesn't allow this (e.g. a learner on an admin endpoint).</summary>
        public const string Forbidden = "AUTH_FORBIDDEN";

        /// <summary>An activation or reset link that is unknown, already used, or expired.</summary>
        public const string TokenInvalid = "AUTH_TOKEN_INVALID";

        /// <summary>The mail could not be handed to the sender; the learner should retry later.</summary>
        public const string MailUnavailable = "MAIL_UNAVAILABLE";

        /// <summary>Switching to a certificate that doesn't exist or has been retired.</summary>
        public const string CertificateUnavailable = "CERT_UNAVAILABLE";

        public const string PracticeSectionUnavailable = "PRACTICE_SECTION_UNAVAILABLE";
        public const string PracticeModalityNotAllowed = "PRACTICE_MODALITY_NOT_ALLOWED";
        public const string PracticeNoQuestionsAvailable = "PRACTICE_NO_QUESTIONS_AVAILABLE";
        public const string PracticeSessionNotFound = "PRACTICE_SESSION_NOT_FOUND";
        public const string PracticeQuestionNotFound = "PRACTICE_QUESTION_NOT_FOUND";
        public const string PracticeInvalidOption = "PRACTICE_INVALID_OPTION";
        public const string PracticeSessionAlreadyFinished = "PRACTICE_SESSION_FINISHED";

        /// <summary>#166: one question is answered once; a second submission is refused.</summary>
        public const string PracticeAlreadyAnswered = "PRACTICE_ALREADY_ANSWERED";

        public const string SavedQuestionNotFound = "SAVED_QUESTION_NOT_FOUND";
        public const string QuestionNotFound = "QUESTION_NOT_FOUND";
        public const string RecommendationNotFound = "RECOMMENDATION_NOT_FOUND";

        public const string OrderAlreadyOpen = "order_already_open";
        public const string EmailNotVerified = "AUTH_EMAIL_UNVERIFIED";
        public const string OrderNotFound = "ORDER_NOT_FOUND";
        public const string PackageNotFound = "PACKAGE_NOT_FOUND";
        public const string PaymentFailed = "PAYMENT_FAILED";
        public const string PaymentExpired = "PAYMENT_EXPIRED";

        public const string InsufficientCredits = "insufficient_credits";
        public const string AttemptExpired = "attempt_expired";
        public const string ExamNotFound = "EXAM_NOT_FOUND";
        public const string AttemptNotFound = "ATTEMPT_NOT_FOUND";
        public const string AttemptNotActive = "ATTEMPT_NOT_ACTIVE";
        public const string AttemptNotFinished = "ATTEMPT_NOT_FINISHED";

        /// <summary>#222: the mock projection sync had nothing legitimate to write.</summary>
        public const string NothingToSync = "NOTHING_TO_SYNC";
        public const string PreflightRequired = "preflight_required";

        public const string ScoreConversionInvalid = "SCORE_CONVERSION_INVALID";

        public const string UserNotFound = "USER_NOT_FOUND";
        public const string InsightNotFound = "INSIGHT_NOT_FOUND";
        public const string InsightNotFailed = "INSIGHT_NOT_FAILED";

        public const string PlacementNotFound = "placement_not_found";
        public const string PlacementNotActive = "placement_not_active";
        public const string PlacementExpired = "placement_expired";
        public const string PlacementNotFinished = "placement_not_finished";

        /// <summary>#217 / PLC-19: nothing was measurable, so the sitting is abandoned and has no result.</summary>
        public const string PlacementAbandoned = "placement_abandoned";
        public const string TargetBandInvalid = "target_band_invalid";

        /// <summary>#213 / PLC-07: a retake inside the 24-hour cooldown. Never a lifetime cap.</summary>
        public const string PlacementCooldown = "placement_cooldown";

        /// <summary>#217 / PLC-35: nothing on this placement failed to grade, so there is nothing to retry.</summary>
        public const string PlacementRetryNotAllowed = "placement_retry_not_allowed";

        /// <summary>#153: a declared pace above the 25 h/week ceiling.</summary>
        public const string WeeklyHoursTooHigh = "weekly_hours_too_high";

        /// <summary>#153: an exam date that has already passed.</summary>
        public const string TargetDateInvalid = "target_date_invalid";

        public const string LearningPathNotFound = "LEARNING_PATH_NOT_FOUND";
        public const string LearningPathAlreadyActive = "LEARNING_PATH_ALREADY_ACTIVE";
        public const string LearningPathStepNotFound = "LEARNING_PATH_STEP_NOT_FOUND";
        public const string LearningPathStepLocked = "LEARNING_PATH_STEP_LOCKED";

        /// <summary>#153: the goal is Unrealistic. The detail carries hours needed and hours available.</summary>
        public const string LearningPathGoalUnrealistic = "LEARNING_PATH_GOAL_UNREALISTIC";

        /// <summary>#153: no starting band and no target band were ever established for this learner.</summary>
        public const string LearningPathBandUnknown = "LEARNING_PATH_BAND_UNKNOWN";
        public const string LessonNotFound = "LESSON_NOT_FOUND";

        public const string SlotNotAvailable = "slot_not_available";
        public const string BookingNotFound = "booking_not_found";
        public const string BookingCannotCancel = "booking_cannot_cancel";
        public const string BookingAlreadyRefunded = "booking_already_refunded";
        public const string BookingNotFinished = "booking_not_finished";
        public const string BookingAlreadyRated = "booking_already_rated";
        public const string MentorNotFound = "mentor_not_found";

        public const string ComplaintNotFound = "COMPLAINT_NOT_FOUND";
        public const string ComplaintInvalidSubject = "COMPLAINT_INVALID_SUBJECT";
        public const string ComplaintAlreadyResolved = "COMPLAINT_ALREADY_RESOLVED";

        /// <summary>#281: a refund that is not there.</summary>
        public const string RefundNotFound = "REFUND_NOT_FOUND";

        /// <summary>#281: a refund with no recorded justification is refused; no value leaves the
        /// company without one.</summary>
        public const string RefundReasonRequired = "REFUND_REASON_REQUIRED";

        /// <summary>#281: a credit refund of zero or fewer credits.</summary>
        public const string RefundCreditCountInvalid = "REFUND_CREDIT_COUNT_INVALID";

        /// <summary>#281: cumulative credits refunded against one order would exceed what that order
        /// granted. Distinct from <see cref="RefundExceedsCeiling"/> so the screen can name the order
        /// rather than the policy.</summary>
        public const string RefundExceedsOrderCeiling = "REFUND_EXCEEDS_ORDER_CEILING";

        /// <summary>#281: a refund with no order behind it exceeds the configured ceiling.</summary>
        public const string RefundExceedsCeiling = "REFUND_EXCEEDS_CEILING";

        /// <summary>#281: requested, approved or rejected already - a decided refund is final.</summary>
        public const string RefundAlreadyDecided = "REFUND_ALREADY_DECIDED";

        /// <summary>#282: a money refund of zero, or one that names an amount in the wrong column.
        /// <c>CK_Refunds_MethodOwnsAmount</c> would refuse the row; this refusal is readable.</summary>
        public const string RefundAmountInvalid = "REFUND_AMOUNT_INVALID";

        /// <summary>#282: a credit refund cannot be settled. The ledger is where its value landed, so
        /// there is no transfer for an accountant to confirm.</summary>
        public const string RefundCreditCannotSettle = "REFUND_CREDIT_CANNOT_SETTLE";

        /// <summary>#282: only an approved refund can be settled. Approval is the decision; settlement
        /// is the accountant saying the transfer happened.</summary>
        public const string RefundNotApproved = "REFUND_NOT_APPROVED";

        /// <summary>#281: the order or complaint a refund names is not the learner's.</summary>
        public const string RefundInvalidSubject = "REFUND_INVALID_SUBJECT";

        public const string ExamIncomplete = "EXAM_INCOMPLETE";
        public const string ExamHasFinishedAttempts = "EXAM_HAS_FINISHED_ATTEMPTS";
        public const string QuestionHasAttemptAnswers = "QUESTION_HAS_ATTEMPT_ANSWERS";
        public const string QuestionGroupNotFound = "QUESTION_GROUP_NOT_FOUND";

        public const string ImportBatchNotFound = "IMPORT_BATCH_NOT_FOUND";
    }

}
