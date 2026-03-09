using FluentAssertions;
using PrLlmReview.Models;
using PrLlmReview.Services;

namespace PrLlmReview.Tests;

public sealed class LlmClientServiceTests
{
    [Fact]
    public void MergeResults_EmptyList_ReturnsDefault()
    {
        var result = LlmClientService.MergeResults([]);
        result.Summary.Should().BeEmpty();
        result.OverallSeverity.Should().Be("pass");
        result.InlineComments.Should().BeEmpty();
    }

    [Fact]
    public void MergeResults_SingleResult_ReturnsSame()
    {
        var input = new LlmReviewResult
        {
            Summary = "Looks good",
            OverallSeverity = "low",
            InlineComments = [new InlineComment { FilePath = "Foo.cs", Line = 1, Severity = "low", Category = "style", Comment = "rename" }]
        };

        var result = LlmClientService.MergeResults([input]);

        result.Summary.Should().Be("Looks good");
        result.OverallSeverity.Should().Be("low");
        result.InlineComments.Should().HaveCount(1);
    }

    [Fact]
    public void MergeResults_MultipleResults_PicksWorstSeverity()
    {
        var results = new List<LlmReviewResult>
        {
            new() { Summary = "A", OverallSeverity = "low",  InlineComments = [] },
            new() { Summary = "B", OverallSeverity = "high", InlineComments = [] },
            new() { Summary = "C", OverallSeverity = "pass", InlineComments = [] },
        };

        var merged = LlmClientService.MergeResults(results);

        merged.OverallSeverity.Should().Be("high");
        merged.Summary.Should().Contain("Chunk 1").And.Contain("Chunk 2").And.Contain("Chunk 3");
    }

    [Fact]
    public void MergeResults_CombinesInlineComments()
    {
        var comment1 = new InlineComment { FilePath = "A.cs", Line = 1, Severity = "high",   Category = "security",    Comment = "c1" };
        var comment2 = new InlineComment { FilePath = "B.cs", Line = 5, Severity = "medium",  Category = "correctness", Comment = "c2" };

        var results = new List<LlmReviewResult>
        {
            new() { Summary = "A", OverallSeverity = "high",   InlineComments = [comment1] },
            new() { Summary = "B", OverallSeverity = "medium", InlineComments = [comment2] },
        };

        var merged = LlmClientService.MergeResults(results);

        merged.InlineComments.Should().HaveCount(2);
        merged.InlineComments.Should().Contain(comment1).And.Contain(comment2);
    }
}
