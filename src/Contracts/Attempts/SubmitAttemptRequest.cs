using System.Collections.Generic;

namespace Contracts;

public sealed record SubmitAttemptRequest(
    IReadOnlyList<SubmitAttemptDraftRequest> Drafts);
