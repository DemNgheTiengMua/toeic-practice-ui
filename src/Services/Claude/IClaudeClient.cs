using System.Threading;
using System.Threading.Tasks;

namespace Services;

public interface IClaudeClient
{
    Task<string> CreateMessageAsync(
        string userPrompt,
        string? systemPrompt = null,
        int maxTokens = 1500,
        CancellationToken cancellationToken = default);
}

