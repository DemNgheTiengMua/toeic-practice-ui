using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapQuestions(RouteGroupBuilder admin)
    {

        admin.MapGet("/exams/{examId:int}/questions", async (
            int examId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var questions = await exams.GetExamQuestionsAsync(examId, cancellationToken);
                return Results.Ok(questions);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/groups", async (
            int examId,
            CreateQuestionGroupRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await exams.CreateQuestionGroupAsync(examId, request, cancellationToken);
                return Results.Created($"/api/admin/exams/{examId}/groups/{created.GroupId}", created);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPut("/exams/{examId:int}/groups/{groupId:int}", async (
            int examId,
            int groupId,
            UpdateQuestionGroupRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await exams.UpdateQuestionGroupAsync(examId, groupId, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapDelete("/exams/{examId:int}/groups/{groupId:int}", async (
            int examId,
            int groupId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await exams.DeleteQuestionGroupAsync(examId, groupId, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/questions", async (
            int examId,
            CreateQuestionRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await exams.CreateQuestionAsync(examId, request, cancellationToken);
                return Results.Created($"/api/admin/questions/{created.QuestionId}", created);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPut("/questions/{questionId:int}", async (
            int questionId,
            UpdateQuestionRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await exams.UpdateQuestionAsync(questionId, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapDelete("/questions/{questionId:int}", async (
            int questionId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await exams.DeleteQuestionAsync(questionId, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
