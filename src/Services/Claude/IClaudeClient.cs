using System.Threading;
using System.Threading.Tasks;

namespace Services;

public interface IClaudeClient
{
    /// <summary>
    /// Sends one message.
    /// <para>
    /// <b><paramref name="model"/> is what makes two verifiers two opinions.</b> Gate three refuses a
    /// rubric whose author, verifier one and verifier two are not three different families, and the
    /// gate can only compare names it is given. Before this parameter existed, every call used the one
    /// model configured on <see cref="ClaudeOptions"/>, so a deployment that configured
    /// <c>["opus", "sonnet"]</c> ran the same weights twice under two labels and the "agreement" was
    /// one opinion counted twice — the exact failure the distinct-family rule exists to catch, passing
    /// the rule while defeating it.
    /// </para>
    /// <para>
    /// Null means "whatever the deployment configured", which is what the grader, the insight generator
    /// and every caller that does not need a second family should pass.
    /// </para>
    /// </summary>
    Task<string> CreateMessageAsync(
        string userPrompt,
        string? systemPrompt = null,
        int maxTokens = 1500,
        string? model = null,
        CancellationToken cancellationToken = default);
}
