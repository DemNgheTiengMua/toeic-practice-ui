using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace WebMVC.Controllers;

/// <summary>
/// #188: the authoring surface for the pool. Its routes carry no exam id, because an item has none -
/// which is exactly why it is not a set of actions on <see cref="AdminExamsController"/>. The studio
/// screen that renders these calls is #203; this is the door it will post to.
/// </summary>
[Authorize(Policy = Permissions.ContentAuthor)]
[Route("admin/items")]
public sealed class AdminItemsController(
    IAdminItemsClient api,
    CurrentUser currentUser,
    ILogger<AdminItemsController> logger) : AdminControllerBase(currentUser, logger)
{
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreatePoolItemRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await api.CreateAdminItemAsync(request, cancellationToken);
            return Json(created);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Could not author a pool item");
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpPut("{itemId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int itemId, [FromBody] UpdatePoolItemRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await api.UpdateAdminItemAsync(itemId, request, cancellationToken);
            return Json(updated);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Could not edit pool item {ItemId}", itemId);
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }
}
