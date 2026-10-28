using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public async Task<AdminUserDto> LockUserAsync(int userId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/users/{userId}/lock");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminUserDto>(Json, cancellationToken))!;
    }

    public async Task<AdminUserDto> UnlockUserAsync(int userId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/users/{userId}/unlock");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminUserDto>(Json, cancellationToken))!;
    }

    public async Task ResendUserActivationAsync(int userId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/users/{userId}/resend-activation");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<AdjustCreditsResultDto> AdjustUserCreditsAsync(int userId, AdjustCreditsRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/users/{userId}/adjust-credits")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdjustCreditsResultDto>(Json, cancellationToken))!;
    }

    public Task<AdminUserDto> LockUserAsync(int userId, CancellationToken cancellationToken = default) =>
        LockUserAsync(userId, string.Empty, cancellationToken);

    public Task<AdminUserDto> UnlockUserAsync(int userId, CancellationToken cancellationToken = default) =>
        UnlockUserAsync(userId, string.Empty, cancellationToken);

    public Task ResendUserActivationAsync(int userId, CancellationToken cancellationToken = default) =>
        ResendUserActivationAsync(userId, string.Empty, cancellationToken);

    public Task<AdjustCreditsResultDto> AdjustUserCreditsAsync(int userId, AdjustCreditsRequest request, CancellationToken cancellationToken = default) =>
        AdjustUserCreditsAsync(userId, request, string.Empty, cancellationToken);


    public Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        var path = $"api/admin/users?{string.Join("&", query)}";
        return GetAuthorizedAsync<AdminUsersSummaryDto>(path, bearerToken, cancellationToken);
    }

    public Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAdminUsersAsync(q, status, page, pageSize, string.Empty, cancellationToken);
}
