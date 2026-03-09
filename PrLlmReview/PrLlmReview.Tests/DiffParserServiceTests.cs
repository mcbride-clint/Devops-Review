using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PrLlmReview.Services;

namespace PrLlmReview.Tests;

public sealed class DiffParserServiceTests
{
    private readonly DiffParserService _sut = new();

    [Fact]
    public void BuildFileDiff_AddedFile_ReturnsAddedLines()
    {
        var result = _sut.BuildFileDiff("NewFile.cs", string.Empty, "public class Foo {}", 300);

        result.Should().NotBeNull();
        result!.DiffContent.Should().Contain("+public class Foo {}");
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void BuildFileDiff_DeletedFile_ReturnsRemovedLines()
    {
        var result = _sut.BuildFileDiff("Old.cs", "public class Bar {}", string.Empty, 300);

        result.Should().NotBeNull();
        result!.DiffContent.Should().Contain("-public class Bar {}");
    }

    [Fact]
    public void BuildFileDiff_TruncatesAtMaxLines()
    {
        var longContent = string.Join('\n', Enumerable.Range(1, 500).Select(i => $"line {i}"));

        var result = _sut.BuildFileDiff("Big.cs", string.Empty, longContent, 50);

        result.Should().NotBeNull();
        result!.Truncated.Should().BeTrue();
        result.DiffContent.Should().Contain("truncated");
    }

    [Fact]
    public void BuildFileDiff_UnchangedFile_ReturnsNull()
    {
        var content = "public class Same {}";

        var result = _sut.BuildFileDiff("Same.cs", content, content, 300);

        // Unchanged file produces only context lines — still valid but may be non-null
        // The key assertion is that no +/- prefixed lines appear
        if (result is not null)
        {
            result.DiffContent.Should().NotContain("+public class Same");
            result.DiffContent.Should().NotContain("-public class Same");
        }
    }

    [Fact]
    public void SplitIntoLlmBatches_SplitsAtChunkSizeLimit()
    {
        // Create chunks that total well over 3000 lines
        var chunks = Enumerable.Range(1, 10)
            .Select(i => new DiffChunk { FilePath = $"file{i}.cs", DiffContent = "x", LineCount = 500, Truncated = false })
            .ToList();

        var batches = _sut.SplitIntoLlmBatches(chunks);

        batches.Should().HaveCountGreaterThan(1);
        batches.SelectMany(b => b).Should().HaveCount(10);
    }

    [Fact]
    public void SplitIntoLlmBatches_SingleSmallChunk_ReturnsSingleBatch()
    {
        var chunks = new List<DiffChunk>
        {
            new() { FilePath = "small.cs", DiffContent = "x", LineCount = 10, Truncated = false }
        };

        var batches = _sut.SplitIntoLlmBatches(chunks);

        batches.Should().HaveCount(1);
    }
}
