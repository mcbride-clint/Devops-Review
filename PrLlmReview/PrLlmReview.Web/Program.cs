using PrLlmReview.BackgroundServices;
using PrLlmReview.History;
using PrLlmReview.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorPages();

// Core services
builder.Services.AddSingleton<ReviewQueue>();
builder.Services.AddHostedService<ReviewQueueService>();

builder.Services.AddHttpClient<AdoClientService>();
builder.Services.AddHttpClient<LlmClientService>();

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
