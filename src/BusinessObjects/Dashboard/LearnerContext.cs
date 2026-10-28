
namespace BusinessObjects;

/// <summary>What every learner screen needs before it renders: the certificate it is scoped to,
/// the others the learner can switch to, and the credit balance (the ledger's sum — credits are
/// one wallet across certificates, so the balance is never scoped).</summary>
public sealed record LearnerContext(
    CertificateOption Active,
    IReadOnlyList<CertificateOption> Certificates,
    int CreditBalance);
