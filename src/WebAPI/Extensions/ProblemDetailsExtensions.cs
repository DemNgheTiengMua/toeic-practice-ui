using Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

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
        app.UseExceptionHandler(errorApp =>
            errorApp.Run(async context =>
            {
                // A binding failure is the CLIENT's error, not ours: minimal APIs throw
                // BadHttpRequestException for a missing or unparseable parameter, and on .NET 10 and
                // earlier that arrives here looking like any other crash. Without this line a caller
                // who forgets a query parameter is told the server is broken (500 UNEXPECTED) and the
                // detail page logs a defect that never happened.
                var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
                var badRequest = error as BadHttpRequestException;

                context.Response.StatusCode = badRequest?.StatusCode ?? StatusCodes.Status500InternalServerError;
                var problem = new ProblemDetails
                {
                    Title = badRequest is null ? "An unexpected error occurred." : "The request is not valid.",
                    Status = context.Response.StatusCode,
                };
                problem.Extensions["code"] = badRequest is null ? ApiError.Codes.Unexpected : ApiError.Codes.Validation;
                await context.Response.WriteAsJsonAsync(problem);
            }));

        // F4: a client that disconnects mid-query fires RequestAborted, and SqlClient reports a
        // command cancelled while it runs as SqlException "Operation cancelled by user", not as
        // OperationCanceledException. The handler above treats only the latter as a disconnect, so
        // every aborted dashboard load was logged as a failure. Answer 499 here instead, as it does
        // for a cancellation. Only the client-side error number 0 qualifies: anything the server
        // raises keeps its own number, so a genuine failure that coincides with the abort is still
        // reported.
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex) when (context.RequestAborted.IsCancellationRequested && IsCancelledCommand(ex))
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                }
            }
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
                Title = status switch
                {
                    StatusCodes.Status404NotFound => "Not Found",
                    StatusCodes.Status400BadRequest => "The request is not valid.",
                    _ => "Error",
                },
                Status = status,
            };
            problem.Extensions["code"] = status switch
            {
                StatusCodes.Status404NotFound => ApiError.Codes.NotFound,
                StatusCodes.Status400BadRequest => ApiError.Codes.Validation,
                _ => ApiError.Codes.Unexpected,
            };
            await response.WriteAsJsonAsync(problem);
        });

        return app;
    }

    /// <summary>A command SqlClient cancelled (error number 0), bare from a query or wrapped by
    /// SaveChanges.</summary>
    private static bool IsCancelledCommand(Exception exception) =>
        exception is SqlException { Number: 0 }
        || exception is DbUpdateException { InnerException: SqlException { Number: 0 } };
}
