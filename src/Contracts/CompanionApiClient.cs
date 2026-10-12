using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>What the MVC site knows about the API: typed calls, errors surfaced with their stable code.</summary>
public interface ICompanionApiClient
{
    Task<IReadOnlyList<CertificateDto>> GetCertificatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Raised when the API answers with a non-success status; carries the ProblemDetails code.</summary>
public sealed class ApiException : HttpRequestException
{
    public ApiException(string code, string? title, int status)
        : base($"API error {status} ({code}): {title}", null, (System.Net.HttpStatusCode)status)
    {
        Code = code;
        Title = title;
        Status = status;
    }

    public string Code { get; }

    public new string? Title { get; }

    public int Status { get; }
}

public sealed class CompanionApiClient(HttpClient http) : ICompanionApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CertificateDto>> GetCertificatesAsync(CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync("api/certificates", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var certificates = await response.Content.ReadFromJsonAsync<List<CertificateDto>>(Json, cancellationToken);
        return certificates ?? [];
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // The API answers failures with RFC 9457 ProblemDetails plus a stable "code" extension.
        // Fall back to the generic code when the body is missing or not the expected shape.
        var code = ApiError.Codes.Unexpected;
        string? title = response.ReasonPhrase;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>(Json, cancellationToken);
            if (problem is not null)
            {
                if (!string.IsNullOrEmpty(problem.Code))
                {
                    code = problem.Code;
                }

                title = problem.Title ?? title;
            }
        }
        catch (JsonException)
        {
            // Body wasn't ProblemDetails JSON; keep the fallback code.
        }

        throw new ApiException(code, title, (int)response.StatusCode);
    }

    private sealed class ProblemDetailsBody
    {
        public string? Title { get; set; }

        public int? Status { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }
}
