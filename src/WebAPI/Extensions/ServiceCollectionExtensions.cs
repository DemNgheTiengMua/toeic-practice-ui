using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Services.Credits;

namespace WebAPI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCompanionDbContext(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CompanionDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("CompanionDb")));

        services.AddHealthChecks()
            .AddDbContextCheck<CompanionDbContext>();

        return services;
    }

    public static IServiceCollection AddDomainServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Core application services
        services.AddScoped<ICertificateService, CertificateService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton(
            configuration.GetSection(AuthLinkOptions.SectionName).Get<AuthLinkOptions>() ?? new AuthLinkOptions());

        var emailOptions = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
        services.AddSingleton<IEmailSender>(sp => new DropFolderEmailSender(
            Path.GetFullPath(emailOptions.DropFolder, environment.ContentRootPath),
            emailOptions.From,
            sp.GetRequiredService<ILogger<DropFolderEmailSender>>()));

        services.AddScoped<ILearnerService, LearnerService>();
        services.AddScoped<ICreditLedger, CreditLedger>();
        services.AddScoped<IPracticeService, PracticeService>();
        services.AddScoped<ISavedQuestionService, SavedQuestionService>();
        services.AddScoped<IWeaknessAnalysisService, WeaknessAnalysisService>();
        services.AddScoped<IPackageService, PackageService>();
        services.AddScoped<IAttemptEngine, AttemptEngine>();
        services.AddScoped<IMockExamService, MockExamService>();
        services.AddScoped<IAdminOrderService, AdminOrderService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IScoreConversionService, ScoreConversionService>();
        services.AddScoped<IAdminExamService, AdminExamService>();
        services.AddScoped<IExamQuickIngestService, ExamQuickIngestService>();
        services.AddScoped<IQuestionImportService, QuestionImportService>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IExamGeneratorService, ExamGeneratorService>();
        services.AddScoped<IPlacementService, PlacementService>();
        services.AddScoped<ILearningPathService, LearningPathService>();
        services.AddScoped<ILessonService, LessonService>();
        services.AddScoped<IAdminLessonService, AdminLessonService>();
        services.AddScoped<IPremiumPathService, PremiumPathService>();
        services.AddScoped<IMentorService, MentorService>();
        services.AddScoped<IComplaintService, ComplaintService>();

        // AI and background worker configuration
        services.AddAiServices(configuration, environment);

        // Payment gateway configuration
        services.AddPaymentServices(configuration);

        return services;
    }

    private static IServiceCollection AddAiServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var speechSuperOptions = configuration.GetSection(SpeechSuperOptions.SectionName).Get<SpeechSuperOptions>() ?? new SpeechSuperOptions();
        speechSuperOptions.AppKey = configuration["SPEECHSUPER_APP_KEY"] ?? speechSuperOptions.AppKey;
        speechSuperOptions.SecretKey = configuration["SPEECHSUPER_SECRET_KEY"] ?? speechSuperOptions.SecretKey;
        speechSuperOptions.BaseUrl = configuration["SPEECHSUPER_BASE_URL"] ?? speechSuperOptions.BaseUrl;
        speechSuperOptions.CoreType = configuration["SPEECHSUPER_CORE_TYPE"] ?? speechSuperOptions.CoreType;

        services.AddSingleton(speechSuperOptions);
        services.AddHttpClient<ISpeechSuperClient, SpeechSuperClient>();

        var claudeOptions = configuration.GetSection(ClaudeOptions.SectionName).Get<ClaudeOptions>() ?? new ClaudeOptions();
        claudeOptions.ApiKey = configuration["CLAUDE_API_KEY"] ?? claudeOptions.ApiKey;
        claudeOptions.BaseUrl = configuration["CLAUDE_BASE_URL"] ?? claudeOptions.BaseUrl;
        claudeOptions.Model = configuration["CLAUDE_MODEL"] ?? claudeOptions.Model;

        services.AddSingleton(claudeOptions);
        services.AddHttpClient<IClaudeClient, ClaudeClient>();
        services.AddSingleton<ClaudeAiGrader>();

        var audioStoragePath = configuration["FileStore:AudioDirectory"]
            ?? Path.Combine(environment.ContentRootPath, "..", "..", "App_Data", "audio");
        services.AddSingleton<IFileStore>(new LocalFileStore(audioStoragePath));

        var aiProvider = configuration["AI_PROVIDER"] ?? configuration["AiProviders:Provider"] ?? "real";
        if (aiProvider.Equals("real", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAiGrader>(sp => new SpeechSuperAiGrader(
                sp.GetRequiredService<ISpeechSuperClient>(),
                sp.GetRequiredService<IFileStore>(),
                sp.GetRequiredService<ClaudeAiGrader>()));
            services.AddSingleton<IAiInsightGenerator, ClaudeAiInsightGenerator>();
        }
        else
        {
            services.AddSingleton<IAiGrader, FakeAiGrader>();
            services.AddSingleton<IAiInsightGenerator, FakeAiInsightGenerator>();
        }

        services.AddScoped<IAiGradingProcessor, AiGradingProcessor>();
        services.AddScoped<IWritingPracticeService, WritingPracticeService>();
        services.AddScoped<ISpeakingPracticeService, SpeakingPracticeService>();
        services.AddHostedService<AiGradingBackgroundService>();

        services.AddScoped<IAiInsightService, AiInsightService>();
        services.AddHostedService<AiInsightBackgroundService>();

        return services;
    }

    private static IServiceCollection AddPaymentServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var payOsOptions = configuration.GetSection(PayOsOptions.SectionName).Get<PayOsOptions>() ?? new PayOsOptions();
        payOsOptions.ClientId = configuration["PAYOS_CLIENT_ID"] ?? payOsOptions.ClientId;
        payOsOptions.ApiKey = configuration["PAYOS_API_KEY"] ?? payOsOptions.ApiKey;
        payOsOptions.ChecksumKey = configuration["PAYOS_CHECKSUM_KEY"] ?? payOsOptions.ChecksumKey;
        payOsOptions.WebhookUrl = configuration["PAYOS_WEBHOOK_URL"] ?? payOsOptions.WebhookUrl;
        services.AddSingleton(payOsOptions);
        services.AddSingleton<PayOsPaymentGateway>();

        var mock = new MockPaymentGateway();
        services.AddSingleton(mock);
        services.AddSingleton<IMockPaymentGateway>(mock);

        var gatewayChoice = configuration["Payment:Gateway"]?.ToLowerInvariant() ?? "mock";
        if (gatewayChoice == "payos")
        {
            services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<PayOsPaymentGateway>());
        }
        else
        {
            services.AddSingleton<IPaymentGateway>(mock);
        }

        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
