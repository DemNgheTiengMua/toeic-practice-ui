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

[Authorize(Policy = Roles.Admin)]
[Route("admin/exams")]
public sealed class AdminExamsController(
    IAdminExamsClient api,
    CurrentUser currentUser,
    ILogger<AdminExamsController> logger,
    IAdminImportsClient? importsClient = null) : AdminControllerBase(currentUser, logger)
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

    [HttpPost("generate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(
        [FromForm] string? certCode,
        [FromForm] string? title,
        [FromForm] string? purpose,
        [FromForm] string? status,
        [FromForm] string? targetBand,
        [FromForm] int? difficultyLevel,
        [FromForm] string? blueprintJson,
        [FromForm] int? certId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ExamBlueprintDto blueprint;
            if (!string.IsNullOrWhiteSpace(blueprintJson))
            {
                blueprint = System.Text.Json.JsonSerializer.Deserialize<ExamBlueprintDto>(
                    blueprintJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Invalid blueprint JSON format.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(certCode))
                {
                    TempData["Error"] = "Choose the target certificate.";
                    return RedirectToAction(nameof(Index), new { certId });
                }

                var defaultSectionCode = certCode.Trim().ToUpperInvariant() switch
                {
                    "IELTS" => "PAS",
                    "VSTEP" => "R4",
                    _ => "P5"
                };

                blueprint = new ExamBlueprintDto(
                    CertCode: certCode,
                    Title: title,
                    Purpose: purpose ?? "mock",
                    Status: status ?? "draft",
                    TargetBand: targetBand,
                    DifficultyLevel: difficultyLevel ?? 3,
                    Sections: [new ExamBlueprintSectionDto(defaultSectionCode, 2)]);
            }

            var result = await api.GenerateAdminExamAsync(blueprint, cancellationToken);
            TempData["Success"] = $"Generated exam '{result.ExamName}' with {result.QuestionCount} questions (Status: {result.Status}).";
            return RedirectToAction(nameof(Studio), new { id = result.ExamId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Generative exam authoring refused");
            TempData["Error"] = ex.Detail ?? ex.Title ?? "Exam generation refused.";
            return RedirectToAction(nameof(Index), new { certId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during generative exam authoring");
            TempData["Error"] = $"Generation failed: {ex.Message}";
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
            Status = ExamStatusDto.Draft,
            Step = 1,
            Certificates = certs,
            ErrorMessage = TempData["Error"] as string,
            SuccessMessage = TempData["Success"] as string
        };

        return View("~/Views/Admin/ExamEditor.cshtml", vm);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] ExamFormViewModel model, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            model.Certificates = await LoadCertificatesAsync(cancellationToken);
            return View("~/Views/Admin/ExamEditor.cshtml", model);
        }

        try
        {
            var request = new CreateAdminExamRequest(
                model.CertId,
                model.Name,
                model.DurationMinutes,
                model.Purpose,
                model.Status,
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
            return View("~/Views/Admin/ExamEditor.cshtml", model);
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
                Status = detail.Status,
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

            return View("~/Views/Admin/ExamEditor.cshtml", vm);
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
            return View("~/Views/Admin/ExamEditor.cshtml", model);
        }

        try
        {
            var request = new UpdateAdminExamRequest(
                model.CertId,
                model.Name,
                model.DurationMinutes,
                model.Purpose,
                model.Status,
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

    [HttpGet("{id:int}/questions")]
    public IActionResult Questions(int id, [FromQuery] int? sectionId = null, [FromQuery] int? questionId = null)
    {
        var routeValues = new RouteValueDictionary { { "id", id } };
        if (sectionId.HasValue)
        {
            routeValues["sectionId"] = sectionId.Value;
        }
        if (questionId.HasValue)
        {
            routeValues["questionId"] = questionId.Value;
        }

        return RedirectToAction(nameof(Studio), routeValues);
    }

    [HttpPost("{id:int}/groups")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGroup(int id, [FromForm] QuestionGroupSaveModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            if (model.GroupId.HasValue && model.GroupId.Value > 0)
            {
                var updateReq = new UpdateQuestionGroupRequest(
                    model.SectionId,
                    model.Passage,
                    model.AudioPath,
                    model.AudioStartMs,
                    model.AudioEndMs,
                    model.ImagePath,
                    model.ImageAlt,
                    model.Transcript,
                    model.PrepSeconds,
                    model.ResponseSeconds,
                    model.DisplayOrder,
                    model.UnitNumber);
                await api.UpdateAdminQuestionGroupAsync(id, model.GroupId.Value, updateReq, cancellationToken);
                TempData["Success"] = "Question group updated successfully.";
            }
            else
            {
                var createReq = new CreateQuestionGroupRequest(
                    model.SectionId,
                    model.Passage,
                    model.AudioPath,
                    model.AudioStartMs,
                    model.AudioEndMs,
                    model.ImagePath,
                    model.ImageAlt,
                    model.Transcript,
                    model.PrepSeconds,
                    model.ResponseSeconds,
                    model.DisplayOrder,
                    model.UnitNumber);
                await api.CreateAdminQuestionGroupAsync(id, createReq, cancellationToken);
                TempData["Success"] = "Question group created successfully.";
            }

            return RedirectToAction(nameof(Studio), new { id, sectionId = model.SectionId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to save group for exam {ExamId}", id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Studio), new { id, sectionId = model.SectionId });
        }
    }

    [HttpPost("{id:int}/groups/{groupId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGroup(int id, int groupId, [FromForm] int? sectionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.DeleteAdminQuestionGroupAsync(id, groupId, cancellationToken);
            TempData["Success"] = "Question group deleted.";
            return RedirectToAction(nameof(Studio), new { id, sectionId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to delete group {GroupId} for exam {ExamId}", groupId, id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Studio), new { id, sectionId });
        }
    }

    [HttpPost("{id:int}/questions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveQuestion(int id, [FromForm] QuestionSaveModel model, CancellationToken cancellationToken = default)
    {
        try
        {

            var optionInputs = new List<AdminQuestionOptionInput>();
            if (model.QuestionType != QuestionTypeDto.FreeResponse && model.Options.Count > 0)
            {
                foreach (var opt in model.Options)
                {
                    var isCorrect = (model.QuestionType == QuestionTypeDto.Mcq || model.QuestionType == QuestionTypeDto.Mcq_3)
                        && !string.IsNullOrWhiteSpace(model.CorrectOptionLabel)
                        ? string.Equals(opt.Label?.Trim(), model.CorrectOptionLabel.Trim(), StringComparison.OrdinalIgnoreCase)
                        : opt.IsCorrect;

                    optionInputs.Add(new AdminQuestionOptionInput(
                        opt.OptionId,
                        opt.Label ?? string.Empty,
                        opt.Text ?? string.Empty,
                        isCorrect,
                        opt.Rationale));
                }
            }

            if (model.QuestionId.HasValue && model.QuestionId.Value > 0)
            {
                var updateReq = new UpdateQuestionRequest(
                    model.GroupId,
                    model.Stem,
                    model.DifficultyLevel,
                    model.QuestionType,
                    model.Explanation,
                    model.CategoryId,
                    model.PrepSeconds,
                    model.ResponseSeconds,
                    model.MinWords,
                    model.MaxWords,
                    model.Hint,
                    model.DisplayOrder,
                    optionInputs,
                    null,
                    model.MaxSelections,
                    model.AllowNumber,
                    model.AcceptedAnswersJson,
                    model.RequiredKeywords);

                await api.UpdateAdminQuestionAsync(model.QuestionId.Value, updateReq, cancellationToken);
                TempData["Success"] = $"Question {model.DisplayOrder} saved successfully.";
                return RedirectToAction(nameof(Studio), new { id, sectionId = model.SectionId, questionId = model.QuestionId });
            }
            else
            {
                var createReq = new CreateQuestionRequest(
                    model.GroupId,
                    model.Stem,
                    model.DifficultyLevel,
                    model.QuestionType,
                    model.Explanation,
                    model.CategoryId,
                    model.PrepSeconds,
                    model.ResponseSeconds,
                    model.MinWords,
                    model.MaxWords,
                    model.Hint,
                    model.DisplayOrder,
                    optionInputs,
                    null,
                    model.MaxSelections,
                    model.AllowNumber,
                    model.AcceptedAnswersJson,
                    model.RequiredKeywords);

                var created = await api.CreateAdminQuestionAsync(id, createReq, cancellationToken);
                TempData["Success"] = $"Question {created.DisplayOrder} created successfully.";
                return RedirectToAction(nameof(Studio), new { id, sectionId = model.SectionId, questionId = created.QuestionId });
            }
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to save question for exam {ExamId}", id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Studio), new { id, sectionId = model.SectionId, questionId = model.QuestionId });
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
        try
        {
            var exams = await api.GetAdminExamsAsync(null, null, null, 1, 1, cancellationToken);
            if (exams.Certificates.Count > 0)
            {
                return exams.Certificates;
            }
        }
        catch
        {
            // fallback
        }

        return
        [
            new AdminCertificateOptionDto(1, "toeic", "TOEIC"),
            new AdminCertificateOptionDto(2, "ielts", "IELTS"),
            new AdminCertificateOptionDto(3, "vstep", "VSTEP")
        ];
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

    [HttpGet("{id:int}/studio")]
    public async Task<IActionResult> Studio(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var studio = await api.GetAdminExamStudioAsync(id, cancellationToken);
            return View("~/Views/Admin/ExamStudio.cshtml", studio);
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Could not load Exam Studio for exam {ExamId}", id);
            TempData["Error"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Index));
        }
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

    [HttpPut("{id:int}/studio/group/{groupId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutosaveGroup(int id, int groupId, [FromBody] UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await api.UpdateAdminQuestionGroupAsync(id, groupId, request, cancellationToken);
            return Json(updated);
        }
        catch (ApiException ex)
        {
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

    [HttpDelete("{id:int}/studio/group/{groupId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStudioGroup(int id, int groupId, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.DeleteAdminQuestionGroupAsync(id, groupId, cancellationToken);
            return Json(new { success = true });
        }
        catch (ApiException ex)
        {
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
