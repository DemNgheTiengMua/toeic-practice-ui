using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public async Task<IReadOnlyList<CertificateDto>> GetCertificatesAsync(CancellationToken cancellationToken = default)
    {
        // The certificate catalog is anonymous — no bearer to forward here.
        var certificates = await http.GetFromJsonAsync<List<CertificateDto>>("api/certificates", Json, cancellationToken);
        return certificates ?? [];
    }
}
