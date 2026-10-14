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

        public const string SavedQuestionNotFound = "SAVED_QUESTION_NOT_FOUND";
        public const string QuestionNotFound = "QUESTION_NOT_FOUND";
        public const string RecommendationNotFound = "RECOMMENDATION_NOT_FOUND";

        public const string OrderAlreadyOpen = "ORDER_ALREADY_OPEN";
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

        public const string UserNotFound = "USER_NOT_FOUND";
    }
}
