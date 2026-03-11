using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using PrLlmReview.BackgroundServices;
using PrLlmReview.History;
using PrLlmReview.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorPages();

// Build a shared CA bundle (if configured) for HttpClient certificate validation
X509Certificate2Collection? customCaBundle = null;
var caBundlePath = builder.Configuration["CaBundlePath"];
if (!string.IsNullOrWhiteSpace(caBundlePath))
{
    customCaBundle = new X509Certificate2Collection();
    customCaBundle.ImportFromPemFile(caBundlePath);
}

static HttpClientHandler CreateHandler(X509Certificate2Collection? caBundle)
{
    var handler = new HttpClientHandler();
    if (caBundle is { Count: > 0 })
    {
        handler.ServerCertificateCustomValidationCallback =
            (_, cert, chain, errors) =>
            {
                if (errors == SslPolicyErrors.None) return true;
                if (chain is null || cert is null) return false;
                chain.ChainPolicy.ExtraStore.AddRange(caBundle);
                chain.ChainPolicy.VerificationFlags =
                    X509VerificationFlags.AllowUnknownCertificateAuthority;
                return chain.Build(cert);
            };
    }
    return handler;
}

// Core services
builder.Services.AddSingleton<ReviewQueue>();
builder.Services.AddHostedService<ReviewQueueService>();

builder.Services.AddHttpClient<AdoClientService>()
    .ConfigurePrimaryHttpMessageHandler(() => CreateHandler(customCaBundle));
builder.Services.AddHttpClient<LlmClientService>()
    .ConfigurePrimaryHttpMessageHandler(() => CreateHandler(customCaBundle));

builder.Services.AddScoped<DiffParserService>();
builder.Services.AddScoped<FileFilterService>();
builder.Services.AddScoped<PromptBuilderService>();
builder.Services.AddScoped<CommentPosterService>();
builder.Services.AddScoped<ReviewOrchestratorService>();

// History (optional)
var historyEnabled = builder.Configuration.GetValue<bool>("History:Enabled");
if (historyEnabled)
{
    builder.Services.AddSingleton<HistoryRepository>();
}

var app = builder.Build();

// Initialise SQLite schema on startup
if (historyEnabled)
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<HistoryRepository>().EnsureCreated();
}

app.UseRouting();
app.MapControllers();
app.MapRazorPages();

app.Run();
