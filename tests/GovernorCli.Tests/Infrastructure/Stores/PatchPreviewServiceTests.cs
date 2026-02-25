using NUnit.Framework;
using System.Text.Json;
using GovernorCli.Application.Models;
using GovernorCli.Application.Stores;
using GovernorCli.Infrastructure.Stores;

namespace GovernorCli.Tests.Infrastructure.Stores;

[TestFixture]
public class PatchPreviewServiceTests
{
    private string _testWorkdir = null!;
    private PatchPreviewService _service = null!;

    [SetUp]
    public void Setup()
    {
        _testWorkdir = Path.Combine(Path.GetTempPath(), $"patch-service-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkdir);
        Directory.CreateDirectory(Path.Combine(_testWorkdir, "state", "runs", "test-run"));
        _service = new PatchPreviewService();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testWorkdir))
        {
            try
            {
                Directory.Delete(_testWorkdir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public void ComputePatchPreview_WithNewPlan_ReturnsChanges()
    {
        // Arrange
        var plan = new ImplementationPlan
        {
            PlanId = "PLAN-test-001",
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            CreatedFromRunId = "test-run",
            ItemId = 1,
            EpicId = "epic-1",
            AppId = "test-app",
            RepoTarget = "apps/test-app",
            AppType = "dotnet_console",
            Stack = new StackInfo
            {
                Language = "csharp",
                Runtime = "net8.0",
                Framework = "dotnet"
            },
            ProjectLayout = new List<ProjectFile>
            {
                new() { Path = "Program.cs", Kind = "source" },
                new() { Path = "test-app.csproj", Kind = "project" }
            },
            BuildPlan = new List<ExecutionStep>(),
            RunPlan = new List<ExecutionStep>(),
            ValidationChecks = new List<ValidationCheck>(),
            PatchPolicy = new PatchPolicy(),
            Notes = "New implementation plan"
        };

        var planPath = Path.Combine(_testWorkdir, "state", "runs", "test-run", "implementation.plan.json");
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(planPath, json);

        // Act
        var preview = _service.ComputePatchPreview(_testWorkdir, 1, planPath);

        // Assert
        Assert.That(preview, Is.Not.Null);
        Assert.That(preview.ItemId, Is.EqualTo(1));
        Assert.That(preview.Changes, Is.Not.Empty);
    }

    [Test]
    public void FormatDiffLines_ProducesCorrectFormat()
    {
        // Arrange
        var preview = new PatchPreviewData
        {
            ComputedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            ItemId = 1,
            Changes = new List<PatchFileChange>
            {
                new()
                {
                    Action = "A",
                    Path = "apps/test-app/Program.cs",
                    CandidateSizeBytes = 100,
                    CandidateSha256 = "abc123",
                    RepoSizeBytes = 0,
                    RepoSha256 = ""
                },
                new()
                {
                    Action = "M",
                    Path = "apps/test-app/test-app.csproj",
                    CandidateSizeBytes = 200,
                    CandidateSha256 = "def456",
                    RepoSizeBytes = 150,
                    RepoSha256 = "abc789"
                }
            }
        };

        // Act
        var lines = _service.FormatDiffLines(preview);

        // Assert
        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[0], Is.EqualTo("A apps/test-app/Program.cs"));
        Assert.That(lines[1], Is.EqualTo("M apps/test-app/test-app.csproj"));
    }

    [Test]
    public void FormatDiffLines_NormalizesWindowsPaths()
    {
        // Arrange
        var preview = new PatchPreviewData
        {
            ComputedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            ItemId = 1,
            Changes = new List<PatchFileChange>
            {
                new()
                {
                    Action = "A",
                    Path = "apps\\test-app\\Program.cs",
                    CandidateSizeBytes = 100,
                    CandidateSha256 = "",
                    RepoSizeBytes = 0,
                    RepoSha256 = ""
                }
            }
        };

        // Act
        var lines = _service.FormatDiffLines(preview);

        // Assert
        Assert.That(lines[0], Does.Not.Contain("\\"));
        Assert.That(lines[0], Is.EqualTo("A apps/test-app/Program.cs"));
    }
}
