using Contracts;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Extensions;

public static class ProblemDetailsExtensions
{
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["code"] = context.ProblemDetails.Status switch
                {
                    StatusCodes.Status404NotFound => ApiError.Codes.NotFound,
                    StatusCodes.Status429TooManyRequests => ApiError.Codes.TooManyRequests,
                    _ => ApiError.Codes.Unexpected,
                };
            });

        return services;
    }

    public static IApplicationBuilder UseApiProblemDetails(this IApplicationBuilder app)
    {
        // The middleware only recognises a disconnect when the exception is OperationCanceledException
        // or IOException. A query cancelled by RequestAborted surfaces from SqlClient as SqlException
        // ("Operation cancelled by user"), which would otherwise be logged as an unhandled error and
        // answered 500 to a client that has already gone.
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            SuppressDiagnosticsCallback = context => context.HttpContext.RequestAborted.IsCancellationRequested,
            ExceptionHandler = async context =>
            {
                if (context.RequestAborted.IsCancellationRequested)
                {
                    context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                var problem = new ProblemDetails
                {
                    Title = "An unexpected error occurred.",
                    Status = StatusCodes.Status500InternalServerError,
                };
                problem.Extensions["code"] = ApiError.Codes.Unexpected;
                await context.Response.WriteAsJsonAsync(problem);
            },
        });

        app.UseStatusCodePages(async context =>
        {
            var status = context.HttpContext.Response.StatusCode;
            if (status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
            {
                return;
            }

            var response = context.HttpContext.Response;
            var problem = new ProblemDetails
            {
                Title = status == StatusCodes.Status404NotFound ? "Not Found" : "Error",
                Status = status,
            };
            problem.Extensions["code"] = status == StatusCodes.Status404NotFound
                ? ApiError.Codes.NotFound
                : ApiError.Codes.Unexpected;
            await response.WriteAsJsonAsync(problem);
        });

        return app;
    }
}
