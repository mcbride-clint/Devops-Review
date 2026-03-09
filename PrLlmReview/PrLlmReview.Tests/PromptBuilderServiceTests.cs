using FluentAssertions;
using PrLlmReview.Models;
using PrLlmReview.Services;

namespace PrLlmReview.Tests;

public sealed class PromptBuilderServiceTests
{
    private readonly PromptBuilderService _sut = new();

    private static ReviewJob MakeJob(string title = "My PR", string description = "Some description") =>
        new(new Models.AdoWebhookPayload
        {
            EventType = "git.pullrequest.created",
            Resource = new()
            {
                PullRequestId = 42,
                Title         = title,
                Description   = description,
                SourceRefName = "refs/heads/feature/my-branch",
                TargetRefName = "refs/heads/main",
                Repository    = new() { Id = "repo-id", Name = "MyRepo" }
            },
            ResourceContainers = new()
            {
                Project    = new() { Id = "proj-id", Name = "MyProject" },
                Collection = new() { BaseUrl = "https://ado/tfs/Default" }
            }
        });

    [Fact]
    public void BuildUserPrompt_IncludesPrTitle()
    {
        var job    = MakeJob("Add invoice feature");
        var chunks = new List<DiffChunk>
        {
            new() { FilePath = "Invoice.cs", DiffContent = "+public void Process() {}", LineCount = 1, Truncated = false }
        };

        var prompt = _sut.BuildUserPrompt(job, chunks);

        prompt.Should().Contain("Add invoice feature");
        prompt.Should().Contain("Invoice.cs");
        prompt.Should().Contain("+public void Process() {}");
    }

    [Fact]
    public void BuildUserPrompt_EmptyDescription_ShowsNone()
    {
        var job    = MakeJob(description: string.Empty);
        var chunks = new List<DiffChunk>
        {
            new() { FilePath = "Foo.cs", DiffContent = "+x", LineCount = 1, Truncated = false }
        };

        var prompt = _sut.BuildUserPrompt(job, chunks);

        prompt.Should().Contain("(none)");
    }

    [Fact]
    public void BuildUserPrompt_TruncatedChunk_AddsTruncationNotice()
    {
        var job    = MakeJob();
        var chunks = new List<DiffChunk>
        {
            new() { FilePath = "Big.cs", DiffContent = "+x", LineCount = 300, Truncated = true }
        };

        var prompt = _sut.BuildUserPrompt(job, chunks);

        prompt.Should().Contain("truncated");
    }

    [Fact]
    public void SystemPrompt_ContainsRequiredInstructions()
    {
        PromptBuilderService.SystemPrompt.Should().Contain("valid JSON");
        PromptBuilderService.SystemPrompt.Should().Contain("overallSeverity");
        PromptBuilderService.SystemPrompt.Should().Contain("inlineComments");
        PromptBuilderService.SystemPrompt.Should().Contain("SQL injection");
    }
}
