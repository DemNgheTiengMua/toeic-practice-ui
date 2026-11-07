using System;
using System.Collections.Generic;

namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<RubricStudioDto> GetRubricStudioAsync(int certId, int skillId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<RubricStudioDto>(
            $"api/admin/studio/rubric?certId={certId}&skillId={skillId}", bearerToken, cancellationToken);

    public Task<RubricStudioDto> SaveRubricAsync(SaveRubricRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<SaveRubricRequest, RubricStudioDto>(
            "api/admin/studio/rubric", request, bearerToken, cancellationToken);

    public Task<RubricLadderDto> RunRubricLadderAsync(RunRubricLadderRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<RunRubricLadderRequest, RubricLadderDto>(
            "api/admin/studio/rubric/ladder", request, bearerToken, cancellationToken);

    public Task<RubricLadderDto> LockRubricAsync(RunRubricLadderRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<RunRubricLadderRequest, RubricLadderDto>(
            "api/admin/studio/rubric/lock", request, bearerToken, cancellationToken);

    public Task<RubricStudioDto> GetRubricStudioAsync(int certId, int skillId, CancellationToken cancellationToken = default) =>
        GetRubricStudioAsync(certId, skillId, string.Empty, cancellationToken);

    public Task<RubricStudioDto> SaveRubricAsync(SaveRubricRequest request, CancellationToken cancellationToken = default) =>
        SaveRubricAsync(request, string.Empty, cancellationToken);

    public Task<RubricLadderDto> RunRubricLadderAsync(RunRubricLadderRequest request, CancellationToken cancellationToken = default) =>
        RunRubricLadderAsync(request, string.Empty, cancellationToken);

    public Task<RubricLadderDto> LockRubricAsync(RunRubricLadderRequest request, CancellationToken cancellationToken = default) =>
        LockRubricAsync(request, string.Empty, cancellationToken);
}
