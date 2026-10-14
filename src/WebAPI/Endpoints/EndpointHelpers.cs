using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Endpoints;

/// <summary>Shared result and mapping helpers for the endpoint files.</summary>
public static class EndpointHelpers
{
    public static CertificateOptionDto ToCertificateDto(CertificateOption option) => new(option.Code, option.Name);

    public static AdminScoreConversionDto ToAdminScoreConversionDto(AdminScoreConversionView view) =>
        new(
            view.CertId,
            view.CertCode,
            view.CertName,
            view.Certificates.Select(ToCertificateDto).ToList(),
            view.ExamId,
            view.ExamName,
            view.SkillScales.Select(s => new SkillScoreScaleGroupDto(
                s.SkillId,
                s.SkillCode,
                s.SkillName,
                s.MaxQuestions,
                s.Scales.Select(sc => new ScoreScaleRowDto(
                    sc.ScaleId, sc.SkillId, sc.SkillCode, sc.SkillName, sc.RawScore, sc.ScaledScore)).ToList())).ToList(),
            view.Bands.Select(b => new ScoreBandConfigDto(
                b.BandId, b.Code, b.Name, b.MinTotal, b.MaxTotal, b.DisplayOrder)).ToList());

    public static SavedQuestionItemDto ToSavedQuestionDto(SavedQuestionItem q) => new(
        q.QuestionId,
        q.Source,
        q.SavedAt,
        q.SectionId,
        q.SectionCode,
        q.SectionName,
        q.CategoryId,
        q.CategoryName,
        q.Stem,
        q.Passage,
        q.ImagePath,
        q.AudioPath,
        q.Hint,
        q.Explanation,
        q.Transcript,
        q.Options.Select(o => new SavedQuestionOptionDto(o.OptionId, o.Label, o.Text, o.IsCorrect)).ToList());

    public static int CurrentUserId(HttpContext http) =>
        int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);

    public static IResult ValidationProblem(ValidationException ex) =>
        Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);

    public static IResult ToTokenResponse(AuthUser user, IAccessTokenFactory tokens)
    {
        var token = tokens.Create(user.UserId, user.Email, user.Role, user.FullName, user.SessionStamp);
        return Results.Ok(new AuthTokenResponse(token.Token, token.ExpiresAt, ToDto(user)));
    }

    public static AuthUserDto ToDto(AuthUser user) =>
        new(user.UserId, user.Email, user.FullName, user.Role, user.EmailVerified);

    public static IResult Problem(int status, string title, string code, string? detail = null)
    {
        var problem = new ProblemDetails { Title = title, Status = status, Detail = detail };
        problem.Extensions["code"] = code;
        return Results.Json(problem, statusCode: status);
    }
}
