using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebMVC.Controllers;

/// <summary>Serves imported question media. Question rows store "/media/imports/{batch}/{file}"
/// (ADR-0007); the browser fetches it here and the site forwards to the API with the cookie's
/// token, so the media store is never exposed directly (ADR-0002). The browser's Range header is
/// forwarded and the API's 206 relayed, so a player can start an audio slice mid-track.</summary>
[Authorize]
public sealed class MediaController(IMediaClient api, CurrentUser currentUser) : Controller
{
    [HttpGet("/media/imports/{batchId:int}/{fileName}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    public Task<IActionResult> Import(int batchId, string fileName, CancellationToken cancellationToken) =>
        RelayAsync(MediaArea.Imports, batchId, fileName, cancellationToken);

    [HttpGet("/media/exams/{examId:int}/{fileName}")]
    [HttpGet("/exams/{examId:int}/{fileName}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    public Task<IActionResult> Exam(int examId, string fileName, CancellationToken cancellationToken) =>
        RelayAsync(MediaArea.Exams, examId, fileName, cancellationToken);

    private async Task<IActionResult> RelayAsync(MediaArea area, int ownerId, string fileName, CancellationToken cancellationToken)
    {
        MediaStreamResponse media;
        try
        {
            var token = await currentUser.GetBearerTokenAsync() ?? string.Empty;
            media = await api.GetMediaAsync(area, ownerId, fileName, Request.Headers.Range.ToString(), token, cancellationToken);
        }
        catch (ApiException ex) when (ex.Status is StatusCodes.Status404NotFound or StatusCodes.Status416RangeNotSatisfiable)
        {
            return StatusCode(ex.Status);
        }

        await using (media.Stream)
        {
            Response.StatusCode = media.StatusCode;
            Response.ContentType = media.ContentType;
            Response.Headers.AcceptRanges = "bytes";
            if (media.ContentLength.HasValue)
            {
                Response.ContentLength = media.ContentLength;
            }
            if (media.ContentRange is not null)
            {
                Response.Headers.ContentRange = media.ContentRange;
            }

            await media.Stream.CopyToAsync(Response.Body, cancellationToken);
        }

        return new EmptyResult();
    }
}
