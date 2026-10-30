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
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                var problem = new ProblemDetails
                {
                    Title = "An unexpected error occurred.",
                    Status = StatusCodes.Status500InternalServerError,
                };
                problem.Extensions["code"] = ApiError.Codes.Unexpected;
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

    /// <summary>A command SqlClient cancelled (error number 0), bare from a query or wrapped by
    /// SaveChanges.</summary>
    private static bool IsCancelledCommand(Exception exception) =>
        exception is SqlException { Number: 0 }
        || exception is DbUpdateException { InnerException: SqlException { Number: 0 } };
}
