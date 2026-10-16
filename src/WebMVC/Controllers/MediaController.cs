using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebMVC.Controllers;

/// <summary>Serves imported question media. Question rows store "/media/imports/{batch}/{file}"
/// (ADR-0007); the browser fetches it here and the site forwards to the API with the cookie's
/// token, so the media store is never exposed directly (ADR-0002).</summary>
[Authorize]
public sealed class MediaController(IMediaClient api, CurrentUser currentUser) : Controller
{
    [HttpGet("/media/imports/{batchId:int}/{fileName}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> Import(int batchId, string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var (stream, contentType) = await api.GetImportMediaAsync(batchId, fileName, cancellationToken);
            return File(stream, contentType, enableRangeProcessing: true);
        }
        catch (ApiException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return NotFound();
        }
    }
}
