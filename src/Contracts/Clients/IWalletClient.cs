
namespace Contracts;

public interface IWalletClient
{
    Task<WalletDto> GetWalletAsync(CancellationToken cancellationToken = default);
    Task<WalletDto> GetWalletAsync(string bearerToken, CancellationToken cancellationToken = default) => GetWalletAsync(cancellationToken);
}
