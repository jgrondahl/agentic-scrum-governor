using NUnit.Framework;
using System.Text.Json;
using GovernorCli.Application.Models;
using GovernorCli.Infrastructure.Stores;

namespace GovernorCli.Tests.Infrastructure.Stores;

[TestFixture]
public class PlanStoreTests
{
    private string _testWorkdir = null!;
    private PlanStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _testWorkdir = Path.Combine(Path.GetTempPath(), $"plan-store-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkdir);
        _store = new PlanStore();
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
    public void SavePlan_CreatesDirectoriesAndPersistsJson()
    {
        // Arrange
        var plan = new ImplementationPlan
        {
            PlanId = "PLAN-test-001",
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            CreatedFromRunId = "20240115_100000_refine-tech_item-1",
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
                new() { Path = "Program.cs", Kind = "source" }
            },
            BuildPlan = new List<ExecutionStep>
            {
                new() { Tool = "dotnet", Args = new List<string> { "build" }, Cwd = "." }
            },
            RunPlan = new List<ExecutionStep>
            {
                new() { Tool = "dotnet", Args = new List<string> { "run" }, Cwd = "." }
            },
            ValidationChecks = new List<ValidationCheck>
            {
                new() { Type = "exit_code_equals", Value = "0" }
            },
            PatchPolicy = new PatchPolicy
            {
                ExcludeGlobs = new List<string> { "bin/**", "obj/**" }
            },
            Notes = "Test plan"
        };

        // Act
        _store.SavePlan(_testWorkdir, 1, plan);

        // Assert
        var planPath = _store.GetPlanPath(_testWorkdir, 1);
        Assert.That(File.Exists(planPath), Is.True);

        var loaded = _store.LoadPlan(_testWorkdir, 1);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.PlanId, Is.EqualTo("PLAN-test-001"));
        Assert.That(loaded.ItemId, Is.EqualTo(1));
    }

    [Test]
    public void LoadPlan_ReturnsNullWhenNotFound()
    {
        // Act
        var loaded = _store.LoadPlan(_testWorkdir, 999);

        // Assert
        Assert.That(loaded, Is.Null);
    }

    [Test]
    public void GetPlanPath_ReturnsCorrectPath()
    {
        // Act
        var path = _store.GetPlanPath(_testWorkdir, 1);

        // Assert
        Assert.That(path, Does.Contain("state"));
        Assert.That(path, Does.Contain("plans"));
        Assert.That(path, Does.Contain("item-1"));
        Assert.That(path, Does.EndWith("implementation.plan.json"));
    }
}
