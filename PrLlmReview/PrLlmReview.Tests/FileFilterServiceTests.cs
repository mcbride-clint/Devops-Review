using FluentAssertions;
using Microsoft.Extensions.Configuration;
using PrLlmReview.Services;

namespace PrLlmReview.Tests;

public sealed class FileFilterServiceTests
{
    private static FileFilterService BuildSut(int maxFiles = 20, string[]? extraPatterns = null)
    {
        var patterns = new[]
        {
            "**/*.png", "**/*.jpg", "**/*.dll", "**/*.exe",
            "**/package-lock.json", "**/yarn.lock",
            "**/*.Designer.cs", "**/*.g.cs",
            "**/Migrations/*"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Review:MaxFilesPerReview"] = maxFiles.ToString(),
                ["Review:ExcludePatterns:0"] = patterns[0],
                ["Review:ExcludePatterns:1"] = patterns[1],
                ["Review:ExcludePatterns:2"] = patterns[2],
                ["Review:ExcludePatterns:3"] = patterns[3],
                ["Review:ExcludePatterns:4"] = patterns[4],
                ["Review:ExcludePatterns:5"] = patterns[5],
                ["Review:ExcludePatterns:6"] = patterns[6],
                ["Review:ExcludePatterns:7"] = patterns[7],
                ["Review:ExcludePatterns:8"] = patterns[8],
            })
            .Build();

        return new FileFilterService(config);
    }

    [Theory]
    [InlineData("src/MyService.cs",     false)]
    [InlineData("assets/logo.png",      true)]
    [InlineData("package-lock.json",    true)]
    [InlineData("obj/Generated.g.cs",   true)]
    [InlineData("Migrations/001_Init.sql", true)]
    public void Filter_ExcludesExpectedPatterns(string path, bool shouldBeExcluded)
    {
        var sut   = BuildSut();
        var files = new List<ChangedFileInfo> { new(path, "edit") };

        var result = sut.Filter(files);

        if (shouldBeExcluded)
            result.Excluded.Should().ContainSingle(f => f.Path == path);
        else
            result.Included.Should().ContainSingle(f => f.Path == path);
    }

    [Fact]
    public void Filter_EnforcesMaxFilesPerReviewCap()
    {
        var sut = BuildSut(maxFiles: 3);
        var files = Enumerable.Range(1, 10)
            .Select(i => new ChangedFileInfo($"src/File{i}.cs", "edit"))
            .ToList();

        var result = sut.Filter(files);

        result.Included.Should().HaveCount(3);
        result.Skipped.Should().HaveCount(7);
    }
}
