using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<WalletDto> GetWalletAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<WalletDto>("api/learner/wallet", bearerToken, cancellationToken);

    public Task<WalletDto> GetWalletAsync(CancellationToken cancellationToken = default) =>
        GetWalletAsync(string.Empty, cancellationToken);
}
