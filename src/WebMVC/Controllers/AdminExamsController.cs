using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize(Policy = Permissions.ContentAuthor)]
[Route("admin/exams")]
public sealed class AdminExamsController(
    IAdminExamsClient api,
    CurrentUser currentUser,
    ILogger<AdminExamsController> logger,
    IAdminImportsClient? importsClient = null,
    IAdminItemsClient? itemsClient = null) : AdminControllerBase(currentUser, logger)
{
    private const long MaxUploadBytes = 101L * 1024 * 1024;

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] int? certId,
        [FromQuery] string? purpose,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var summary = await api.GetAdminExamsAsync(q, status, certId, purpose, page, 20, cancellationToken);

            var vm = new AdminExamListViewModel(
                summary,
                q,
                status,
                certId,
                page,
                purpose,
                ErrorMessage: TempData["Error"] as string,
                SuccessMessage: TempData["Success"] as string);
            return View("~/Views/Admin/Exams.cshtml", vm);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not load admin exams list");
            TempData["Error"] = "Could not load the exam list. Please try again.";
            var emptySummary = new AdminExamListSummaryDto([], 0, 1, 20, []);
            return View("~/Views/Admin/Exams.cshtml", new AdminExamListViewModel(emptySummary, q, status, certId, page, purpose, ErrorMessage: TempData["Error"] as string));
        }
    }

    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Import(
        [FromForm] int? certId,
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (importsClient is null)
        {
            throw new InvalidOperationException("IAdminImportsClient is not configured.");
        }

        if (certId is null)
        {
            TempData["Error"] = "Choose the certificate the questions belong to.";
            return RedirectToAction(nameof(Index));
        }

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a .json manifest or a .zip package to import.";
            return RedirectToAction(nameof(Index), new { certId });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await importsClient.UploadAdminImportAsync(certId.Value, stream, file.FileName, cancellationToken);

            if (result.Status == ImportBatchStatusDto.Done)
            {
                TempData["Success"] = $"Imported '{result.ExamName}' as a draft exam with {result.QuestionCount} questions.";
            }
            else
            {
                var errorMsg = $"Nothing was imported: {result.ErrorCount} {(result.ErrorCount == 1 ? "problem" : "problems")} found in manifest.";
                try
                {
                    var detail = await importsClient.GetAdminImportBatchAsync(result.BatchId, cancellationToken);
                    if (detail?.Logs?.Count > 0)
                    {
                        var issues = detail.Logs
                            .Where(l => l.Level == ImportLogLevelDto.Error)
                            .Take(3)
                            .Select(l => l.RowNo.HasValue ? $"Q{l.RowNo}: {l.Message}" : l.Message);
                        var issueList = string.Join("; ", issues);
                        if (!string.IsNullOrWhiteSpace(issueList))
                        {
                            errorMsg += $" Issues: {issueList}";
                        }
                    }
                }
                catch (Exception logEx)
                {
                    logger.LogDebug(logEx, "Could not fetch batch {BatchId} detail logs", result.BatchId);
                }
                TempData["Error"] = errorMsg;
            }

            return RedirectToAction(nameof(Index), new { certId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Import upload refused");
            TempData["Error"] = ex.Detail ?? ex.Title ?? "The import was refused.";
            return RedirectToAction(nameof(Index), new { certId });
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during exam import upload");
            TempData["Error"] = "Unable to reach import service. Please try again.";
            return RedirectToAction(nameof(Index), new { certId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during exam import upload");
            TempData["Error"] = "An error occurred while importing the exam. Please try again.";
            return RedirectToAction(nameof(Index), new { certId });
        }
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create([FromQuery] int? certId, CancellationToken cancellationToken = default)
    {
        var certs = await LoadCertificatesAsync(cancellationToken);
        var selectedCert = certId ?? certs.FirstOrDefault()?.CertId ?? 1;

        var vm = new ExamFormViewModel
        {
            CertId = selectedCert,
            DurationMinutes = 120,
            Purpose = ExamPurposeDto.Mock,
            Step = 1,
            Certificates = certs,
            ErrorMessage = TempData["Error"] as string,
            SuccessMessage = TempData["Success"] as string
        };

        return View("~/Views/Admin/ExamEditor.cshtml", new ExamEditorViewModel(vm));
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] ExamFormViewModel model, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            model.Certificates = await LoadCertificatesAsync(cancellationToken);
            return View("~/Views/Admin/ExamEditor.cshtml", new ExamEditorViewModel(model));
        }

        try
        {
            var request = new CreateAdminExamRequest(
                model.CertId,
                model.Name,
                model.DurationMinutes,
                model.Purpose,
                model.Description,
                model.SourceName,
                model.InternalNote,
                model.QuestionCount);

            var created = await api.CreateAdminExamAsync(request, cancellationToken);
            TempData["Success"] = $"Exam '{created.Name}' created successfully.";
            return RedirectToAction(nameof(Edit), new { id = created.ExamId, step = 2 });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to create exam");
            model.Certificates = await LoadCertificatesAsync(cancellationToken);
            model.ErrorMessage = ex.Detail ?? ex.Message;
            return View("~/Views/Admin/ExamEditor.cshtml", new ExamEditorViewModel(model));
        }
    }

    [HttpGet("{id:int}")]
    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, [FromQuery] int step = 1, CancellationToken cancellationToken = default)
    {
        try
        {
            var detail = await api.GetAdminExamByIdAsync(id, cancellationToken);
            var certs = await LoadCertificatesAsync(cancellationToken);

            var vm = new ExamFormViewModel
            {
                ExamId = detail.ExamId,
                CertId = detail.CertId,
                Name = detail.Name,
                DurationMinutes = detail.DurationMinutes,
                Purpose = detail.Purpose,
                Description = detail.Description,
                SourceName = detail.SourceName,
                InternalNote = detail.InternalNote,
                QuestionCount = detail.QuestionCount,
                Step = Math.Clamp(step, 1, 3),
                Detail = detail,
                Certificates = certs,
                ErrorMessage = TempData["Error"] as string,
                SuccessMessage = TempData["Success"] as string
            };

            return View("~/Views/Admin/ExamEditor.cshtml", new ExamEditorViewModel(vm));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.ExamNotFound)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not load exam {ExamId}", id);
            TempData["Error"] = "Could not load exam details.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [FromForm] ExamFormViewModel model, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            model.Certificates = await LoadCertificatesAsync(cancellationToken);
            return View("~/Views/Admin/ExamEditor.cshtml", new ExamEditorViewModel(model));
        }

        try
        {
            var request = new UpdateAdminExamRequest(
                model.CertId,
                model.Name,
                model.DurationMinutes,
                model.Purpose,
                model.Description,
                model.SourceName,
                model.InternalNote,
                model.QuestionCount);

            var updated = await api.UpdateAdminExamAsync(id, request, cancellationToken);
            TempData["Success"] = $"Exam '{updated.Name}' updated successfully.";
            var nextStep = model.Step == 1 ? 2 : model.Step;
            return RedirectToAction(nameof(Edit), new { id, step = nextStep });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to update exam {ExamId}", id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Edit), new { id, step = model.Step });
        }
    }

    [HttpPost("{id:int}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id, CancellationToken cancellationToken = default)
    {
        bool isAjax = Request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                      || string.Equals(Request.ContentType, "application/json", StringComparison.OrdinalIgnoreCase)
                      || Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        try
        {
            var result = await api.PublishAdminExamAsync(id, cancellationToken);
            if (isAjax)
            {
                return Json(new { success = true, message = result.Message });
            }
            TempData["Success"] = result.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to publish exam {ExamId}", id);
            var errorMsg = ex.Detail ?? ex.Message;
            if (isAjax)
            {
                return StatusCode(ex.Status > 0 ? ex.Status : StatusCodes.Status400BadRequest, new { success = false, message = errorMsg });
            }
            TempData["Error"] = errorMsg;
            return RedirectToAction(nameof(Edit), new { id, step = 3 });
        }
    }

    [HttpPost("{id:int}/withdraw")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await api.WithdrawAdminExamAsync(id, cancellationToken);
            TempData["Success"] = result.Message;
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to withdraw exam {ExamId}", id);
            TempData["Error"] = ex.Detail ?? ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.DeleteAdminExamAsync(id, cancellationToken);
            TempData["Success"] = "Exam deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to delete exam {ExamId}", id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("{id:int}/audio")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(63L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 63L * 1024 * 1024)]
    public async Task<IActionResult> SaveAudio(int id, [FromForm] UploadAudioModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            string audioPath;
            long sizeBytes;
            int durationSec;

            if (model.AudioFile != null && model.AudioFile.Length > 0)
            {
                await using var stream = model.AudioFile.OpenReadStream();
                var result = await api.UploadAdminExamMediaAsync(id, stream, model.AudioFile.FileName, cancellationToken);
                audioPath = MediaUrlHelper.NormalizeMediaUrl(!string.IsNullOrWhiteSpace(result.Url) ? result.Url : result.Path)!;
                sizeBytes = model.AudioFile.Length;
                durationSec = model.DurationSec > 0 ? model.DurationSec : 300;
            }
            else if (!string.IsNullOrWhiteSpace(model.AudioPath))
            {
                audioPath = MediaUrlHelper.NormalizeMediaUrl(model.AudioPath)!;
                sizeBytes = model.SizeBytes > 0 ? model.SizeBytes : 0;
                durationSec = model.DurationSec > 0 ? model.DurationSec : 300;
            }
            else
            {
                if (Request.Headers.Accept.ToString().Contains("application/json") || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return BadRequest(new { message = "Please select an audio file to upload." });
                }
                TempData["Error"] = "Please select an audio file to upload.";
                return RedirectToAction(nameof(Edit), new { id, step = 2 });
            }

            var request = new SaveExamSectionAudioRequest(model.SectionId, audioPath, durationSec, sizeBytes);
            await api.SaveAdminExamSectionAudioAsync(id, request, cancellationToken);

            if (Request.Headers.Accept.ToString().Contains("application/json") || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, audioPath, sectionId = model.SectionId, durationSec, sizeBytes });
            }

            TempData["Success"] = "Audio file saved successfully.";
            return RedirectToAction(nameof(Edit), new { id, step = 2 });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to save audio for exam {ExamId}", id);
            if (Request.Headers.Accept.ToString().Contains("application/json") || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
            }
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Edit), new { id, step = 2 });
        }
    }

    [HttpPost("{id:int}/groups/{groupId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGroup(int id, int groupId, [FromForm] int? sectionId, CancellationToken cancellationToken = default)
    {
        try
        {
            // #273: one removal path. A pool paper's group is a position set on the paper; a legacy
            // paper's group is its own row. The API decides by which table holds the id.
            await api.DeleteExamStudioGroupAsync(id, groupId, cancellationToken);
            TempData["Success"] = "Question group removed.";
            return RedirectToAction(nameof(Studio), new { id, sectionId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to delete group {GroupId} for exam {ExamId}", groupId, id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Studio), new { id, sectionId });
        }
    }

    [HttpPost("{id:int}/questions/{questionId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(int id, int questionId, [FromForm] int? sectionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.DeleteAdminQuestionAsync(questionId, cancellationToken);
            TempData["Success"] = "Question deleted successfully.";
            return RedirectToAction(nameof(Studio), new { id, sectionId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to delete question {QuestionId} for exam {ExamId}", questionId, id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Studio), new { id, sectionId });
        }
    }

    private async Task<IReadOnlyList<AdminCertificateOptionDto>> LoadCertificatesAsync(CancellationToken cancellationToken)
    {
        // #149: the exam list already carries the real certificates. An empty list is surfaced, never
        // replaced with a made-up TOEIC/IELTS/VSTEP trio that points at certificate ids nobody created.
        var page = await api.GetAdminExamsAsync(null, null, null, 1, 20, cancellationToken);
        return page.Certificates;
    }

    [HttpPost("{id:int}/quick-ingest/parse")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ParseQuickIngest(
        int id,
        [FromBody] QuickIngestParseRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await api.ParseQuickIngestAsync(id, request, cancellationToken);
            return Json(result);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to parse quick ingest for exam {ExamId}", id);
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpPost("{id:int}/quick-ingest/commit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CommitQuickIngest(
        int id,
        [FromBody] QuickIngestCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await api.CommitQuickIngestAsync(id, request, cancellationToken);
            return Json(result);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to commit quick ingest for exam {ExamId}", id);
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpGet("sections/{sectionId:int}/quick-ingest/sample")]
    public async Task<IActionResult> GetQuickIngestSample(
        int sectionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await api.GetQuickIngestSampleTemplateAsync(sectionId, cancellationToken);
            return Json(result);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to get quick ingest sample template for section {SectionId}", sectionId);
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    /// <summary>
    /// #158: the exam as a learner sits it, in the real sitting view, for a draft or published exam.
    /// Admin-only by the controller's policy, so the same URL a learner can type reaches nothing:
    /// this action lives on the admin controller and the learner's sitting action never reads a
    /// preview flag. Nothing is written — no attempt, no answer, no ledger row.
    /// </summary>
    [HttpGet("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id, [FromQuery] int? q, CancellationToken cancellationToken = default)
    {
        try
        {
            var preview = await api.GetAdminExamPreviewAsync(id, q, cancellationToken);
            return View("~/Views/Admin/ExamPreview.cshtml", new ExamTakeViewModel(preview.Take, Preview: preview));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Could not load preview for exam {ExamId}", id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("{id:int}/studio")]
    public async Task<IActionResult> Studio(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var studio = await api.GetAdminExamStudioAsync(id, cancellationToken);
            return View("~/Views/Admin/ExamStudio.cshtml", new ExamStudioViewModel(studio));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Could not load Exam Studio for exam {ExamId}", id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// #225 / S-09: the assembly workbench — the certificate's sections, what each declares and what
    /// the pool can supply, with the positions the paper already holds.
    /// <para>
    /// Everything the page prints is a column of <see cref="ExamAssemblyDto"/>; this action groups and
    /// forwards, as the other studios do. A screen that subtracted declared from supply would be a
    /// second arithmetic path over the same stock.
    /// </para>
    /// </summary>
    [HttpGet("{id:int}/assembly")]
    public async Task<IActionResult> Assembly(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var assembly = await api.GetAdminExamAssemblyAsync(id, cancellationToken);
            return View("~/Views/Admin/ExamAssembly.cshtml",
                ExamAssemblyPresentation.From(
                    assembly,
                    code => CefrLevelLabels.LabelForCode(code, logger),
                    code => ModalityLabels.LabelForCode(code, logger)));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Could not load the assembly workbench for exam {ExamId}", id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// The bind. One position at a time, because a paper's position is what the binding decides — a
    /// bulk endpoint would have to invent the positions it was not told.
    /// <para>
    /// <b>This forwards; it does not judge.</b> The demo-into-production refusal is
    /// <c>trg_ExamItems_ProductionBindsProduction</c> (INV-6) and the skill refusal is
    /// <c>FK_ExamItems_ItemSkill</c>, both enforced in the database and both surfaced by
    /// <c>BindItemAsync</c> as a sentence. Adding a second check here would give one rule two
    /// authorities, and the two eventually disagree.
    /// </para>
    /// </summary>
    [HttpPost("{id:int}/assembly/bind")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bind(
        int id,
        [FromForm] BindItemFormModel model,
        CancellationToken cancellationToken = default)
    {
        if (itemsClient is null)
        {
            throw new InvalidOperationException("IAdminItemsClient is not configured.");
        }

        try
        {
            var binding = await itemsClient.BindAdminItemAsync(
                id,
                new BindPoolItemRequest(model.SectionId, model.ItemVersionId, model.DisplayOrder, model.UnitNumber),
                cancellationToken);

            TempData["Success"] =
                $"Bound version {binding.ItemVersionId} at position {binding.DisplayOrder} of section {model.SectionCode}, unit {binding.UnitNumber}.";
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Could not bind an item to exam {ExamId}", id);
            TempData["Error"] = ex.Detail ?? ex.Message;
        }

        return RedirectToAction(nameof(Assembly), new { id });
    }

    [HttpPost("{id:int}/reorder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder(int id, [FromBody] ReorderSectionItemsRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.ReorderAdminSectionItemsAsync(id, request, cancellationToken);
            return Json(new { success = true });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to reorder items for exam {ExamId}", id);
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpGet("{id:int}/audit")]
    public async Task<IActionResult> Audit(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var audit = await api.GetAdminExamAuditAsync(id, cancellationToken);
            return Json(audit);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to audit exam {ExamId}", id);
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    /// <summary>
    /// #273: the group card's autosave. The name changed with the write: this no longer posts a
    /// <c>QuestionGroup</c> (#265 deleted that route), it writes the stimulus a pool group IS, plus the
    /// unit its positions sit in.
    /// </summary>
    [HttpPut("{id:int}/studio/group/{groupId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStudioGroup(int id, int groupId, [FromBody] UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.UpdateExamStudioGroupAsync(id, groupId, request, cancellationToken);
            return Json(new { success = true, groupId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to update group {GroupId} for exam {ExamId}", groupId, id);
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpPut("{id:int}/studio/question/{questionId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutosaveQuestion(int id, int questionId, [FromBody] UpdateQuestionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await api.UpdateAdminQuestionAsync(questionId, request, cancellationToken);
            return Json(updated);
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    /// <summary>
    /// #273: "Remove Group". Removes the paper's positions; the pool's items and their stimulus stay,
    /// because another paper may bind them and a pool edit is not this screen's to make.
    /// </summary>
    [HttpDelete("{id:int}/studio/group/{groupId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveStudioGroup(int id, int groupId, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.DeleteExamStudioGroupAsync(id, groupId, cancellationToken);
            return Json(new { success = true });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to remove group {GroupId} from exam {ExamId}", groupId, id);
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpDelete("{id:int}/studio/question/{questionId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStudioQuestion(int id, int questionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.DeleteAdminQuestionAsync(questionId, cancellationToken);
            return Json(new { success = true });
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpPut("{id:int}/studio/meta")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutosaveMeta(int id, [FromBody] UpdateAdminExamRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await api.UpdateAdminExamAsync(id, request, cancellationToken);
            return Json(updated);
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpPost("{id:int}/media")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadMedia(int id, IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "Choose a file to upload." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await api.UploadAdminExamMediaAsync(id, stream, file.FileName, cancellationToken);
            return Json(new { success = true, url = result.Url, path = result.Path, fileName = result.FileName });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to upload media for exam {ExamId}", id);
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpPost("{id:int}/clusters")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCluster(int id, [FromBody] CreateExamClusterRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var cluster = await api.CreateAdminExamClusterAsync(id, request, cancellationToken);
            return Json(new { success = true, cluster });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to create cluster for exam {ExamId}", id);
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> Export(int id, [FromQuery] string? format = "zip", CancellationToken cancellationToken = default)
    {
        try
        {
            var (stream, contentType, fileName) = await api.ExportAdminExamAsync(id, format, cancellationToken);
            return File(stream, contentType, fileName);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to export exam {ExamId}", id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Studio), new { id });
        }
    }

    [HttpGet("manifest-template")]
    public async Task<IActionResult> ManifestTemplate([FromQuery] int certId, CancellationToken cancellationToken = default)
    {
        if (certId <= 0)
        {
            TempData["Error"] = "Select a certificate to download its manifest template.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var (stream, contentType, fileName) = await api.GetManifestTemplateAsync(certId, cancellationToken);
            return File(stream, contentType, fileName);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to download manifest template for cert {CertId}", certId);
            TempData["Error"] = ex.Detail ?? ex.Message ?? "Failed to download manifest template.";
            return RedirectToAction(nameof(Index), new { certId });
        }
    }
}
