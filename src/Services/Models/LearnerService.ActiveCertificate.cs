using BusinessObjects;
namespace Services;

public sealed partial class LearnerService
{
    private sealed record ActiveCertificate(int CertId, CertificateOption Option);
}
