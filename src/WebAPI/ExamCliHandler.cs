using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;

namespace WebAPI;

public static class ExamCliHandler
{
    public static async Task<bool> TryHandleAsync(WebApplication app, string[] args)
    {
        if (args.Length == 0 || (args[0] != "generate-exam" && args[0] != "--generate-exam"))
        {
            return false;
        }

        var filePath = args.Length > 1 ? args[1] : null;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            Console.Error.WriteLine("Error: Please provide a valid path to an exam blueprint JSON file.");
            Console.Error.WriteLine("Usage: dotnet run --project src/WebAPI -- generate-exam <path-to-blueprint.json> [--admin-id <id>]");
            Environment.ExitCode = 1;
            return true;
        }

        try
        {
            using var scope = app.Services.CreateScope();
            var generator = scope.ServiceProvider.GetRequiredService<IExamGeneratorService>();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();

            var jsonText = await File.ReadAllTextAsync(filePath);
            var blueprint = System.Text.Json.JsonSerializer.Deserialize<ExamBlueprintDto>(jsonText, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Failed to parse blueprint JSON.");

            int adminId = 1;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] is "--admin-id" or "-a" && int.TryParse(args[i + 1], out var parsedId))
                {
                    adminId = parsedId;
                }
            }
            if (adminId == 1)
            {
                var adminUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role.Code == "admin");
                if (adminUser != null) adminId = adminUser.UserId;
            }

            Console.WriteLine($"[ExamGenerator] Ingesting exam blueprint for cert '{blueprint.CertCode}'...");
            var result = await generator.GenerateAndIngestExamAsync(blueprint, adminId);
            Console.WriteLine($"[ExamGenerator] SUCCESS! Created exam ID {result.ExamId} ('{result.ExamName}') with {result.QuestionCount} questions (Status: {result.Status}).");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = 0;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ExamGenerator] ERROR: {ex.Message}");
            Environment.ExitCode = 1;
            return true;
        }
    }
}
