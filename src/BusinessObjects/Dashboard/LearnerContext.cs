
namespace BusinessObjects;

/// <summary>What every learner screen needs before it renders: the certificate it is scoped to,
/// the others the learner can switch to, and the credit balance (the ledger's sum — credits are
/// one wallet across certificates, so the balance is never scoped).
/// <para>
/// #242: <see cref="Skills"/> is the modalities the active certificate examines, which is what the
/// shell's six doors read to explain a door this certificate cannot open. <see cref="ExamDate"/> is
/// the learner's goal date for this certificate, or null when they have not set one — the shell
/// prints nothing then rather than a placeholder date.
/// </para></summary>
public sealed record LearnerContext(
    CertificateOption Active,
    IReadOnlyList<CertificateOption> Certificates,
    int CreditBalance,
    IReadOnlyList<Modality> Skills,
    DateOnly? ExamDate);
