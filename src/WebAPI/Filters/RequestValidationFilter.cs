using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Contracts;

namespace WebAPI.Filters;

/// <summary>
/// Tier 2 Minimal API endpoint filter that validates <typeparamref name="TRequest"/> against
/// <see cref="ValidationAttribute"/> and <see cref="IValidatableObject"/> rules, short-circuiting
/// invalid requests with an RFC 7807 <c>400 Bad Request</c> ValidationProblem payload.
/// </summary>
public sealed class RequestValidationFilter<TRequest> : IEndpointFilter where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [""] = ["Request body is required."] },
                title: "Validation failed",
                extensions: new Dictionary<string, object?> { ["code"] = ApiError.Codes.Validation });
        }

        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(request);
        if (!Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true))
        {
            var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var result in validationResults)
            {
                var message = result.ErrorMessage ?? "Invalid value.";
                var members = result.MemberNames.Any() ? result.MemberNames : [""];
                foreach (var member in members)
                {
                    var key = ToCamelCaseKey(member);
                    if (!errors.TryGetValue(key, out var list))
                    {
                        list = [];
                        errors[key] = list;
                    }
                    list.Add(message);
                }
            }

            var errorDictionary = errors.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToArray(),
                StringComparer.Ordinal);

            return Results.ValidationProblem(
                errorDictionary,
                title: "Validation failed",
                extensions: new Dictionary<string, object?> { ["code"] = ApiError.Codes.Validation });
        }

        return await next(context);
    }

    private static string ToCamelCaseKey(string memberName)
    {
        if (string.IsNullOrEmpty(memberName))
        {
            return string.Empty;
        }

        if (!memberName.Contains('.', StringComparison.Ordinal))
        {
            return JsonNamingPolicy.CamelCase.ConvertName(memberName);
        }

        return string.Join('.', memberName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
    }
}

public static class RequestValidationExtensions
{
    /// <summary>
    /// Attaches <see cref="RequestValidationFilter{TRequest}"/> to validate <typeparamref name="TRequest"/>
    /// before the endpoint handler runs.
    /// </summary>
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class
    {
        return builder.AddEndpointFilter<RequestValidationFilter<TRequest>>();
    }
}
