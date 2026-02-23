using NUnit.Framework;

namespace GovernorCli.Tests;

[TestFixture]
public class RepoChecksTests
{
    private string _testWorkdir = null!;

    [SetUp]
    public void Setup()
    {
        // Create a temporary test directory
        _testWorkdir = Path.Combine(Path.GetTempPath(), $"governor-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkdir);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test directory
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

    #region EnsureDirectoriesExist Tests

    [Test]
    public void EnsureDirectoriesExist_CreatesAppsDirectory()
    {
        // Arrange
        var appsDir = Path.Combine(_testWorkdir, "apps");
        Assert.That(Directory.Exists(appsDir), Is.False, "apps directory should not exist initially");

        // Act
        var created = RepoChecks.EnsureDirectoriesExist(_testWorkdir);

        // Assert
        Assert.That(Directory.Exists(appsDir), Is.True, "apps directory should be created");
        Assert.That(created, Does.Contain("apps"), "Should report apps as created");
    }

    [Test]
    public void EnsureDirectoriesExist_CreatesStateRunsDirectory()
    {
        // Arrange
        var runsDir = Path.Combine(_testWorkdir, "state", "runs");
        Assert.That(Directory.Exists(runsDir), Is.False, "state/runs directory should not exist initially");

        // Act
        var created = RepoChecks.EnsureDirectoriesExist(_testWorkdir);

        // Assert
        Assert.That(Directory.Exists(runsDir), Is.True, "state/runs directory should be created");
        Assert.That(created, Does.Contain(Path.Combine("state", "runs")), "Should report state/runs as created");
    }

    [Test]
    public void EnsureDirectoriesExist_CreatesAllRequiredDirectories()
    {
        // Act
        var created = RepoChecks.EnsureDirectoriesExist(_testWorkdir);

        // Assert
        var expectedDirs = new[]
        {
            "src",
            "state",
            "prompts",
            Path.Combine("prompts", "personas"),
            Path.Combine("prompts", "flows"),
            Path.Combine("state", "decisions"),
            Path.Combine("state", "runs"),
            "apps"
        };

        foreach (var dir in expectedDirs)
        {
            var fullPath = Path.Combine(_testWorkdir, dir);
            Assert.That(Directory.Exists(fullPath), Is.True, $"Directory {dir} should be created");
            Assert.That(created, Does.Contain(dir), $"Should report {dir} as created");
        }
    }

    [Test]
    public void EnsureDirectoriesExist_DoesNotReportExistingDirectories()
    {
        // Arrange - create one directory beforehand
        Directory.CreateDirectory(Path.Combine(_testWorkdir, "apps"));

        // Act
        var created = RepoChecks.EnsureDirectoriesExist(_testWorkdir);

        // Assert
        Assert.That(created, Does.Not.Contain("apps"), "Should not report already-existing apps directory");
    }

    [Test]
    public void EnsureDirectoriesExist_CreatesNestedDirectories()
    {
        // Act
        var created = RepoChecks.EnsureDirectoriesExist(_testWorkdir);

        // Assert
        var promptsPersonasDir = Path.Combine(_testWorkdir, "prompts", "personas");
        var promptsFlowsDir = Path.Combine(_testWorkdir, "prompts", "flows");
        var stateDecisionsDir = Path.Combine(_testWorkdir, "state", "decisions");
        var stateRunsDir = Path.Combine(_testWorkdir, "state", "runs");

        Assert.That(Directory.Exists(promptsPersonasDir), Is.True, "prompts/personas should be created");
        Assert.That(Directory.Exists(promptsFlowsDir), Is.True, "prompts/flows should be created");
        Assert.That(Directory.Exists(stateDecisionsDir), Is.True, "state/decisions should be created");
        Assert.That(Directory.Exists(stateRunsDir), Is.True, "state/runs should be created");
    }

    #endregion

    #region ValidateLayout Tests

    [Test]
    public void ValidateLayout_WithMissingAppsDirectory_ReturnsProblem()
    {
        // Arrange
        SetupMinimalValidRepository(_testWorkdir);
        // Intentionally do NOT create the "apps" directory

        // Act
        var problems = RepoChecks.ValidateLayout(_testWorkdir);

        // Assert
        Assert.That(problems, Does.Contain("Missing directory: apps"),
            "Should report missing apps directory");
    }

    [Test]
    public void ValidateLayout_WithAppsDirectory_DoesNotReportAppsMissing()
    {
        // Arrange
        SetupMinimalValidRepository(_testWorkdir);
        Directory.CreateDirectory(Path.Combine(_testWorkdir, "apps"));

        // Act
        var problems = RepoChecks.ValidateLayout(_testWorkdir);

        // Assert
        Assert.That(problems, Does.Not.Contain("Missing directory: apps"),
            "Should NOT report missing apps directory when it exists");
    }

    [Test]
    public void ValidateLayout_WithAppsDirectory_ReturnsNoProblems()
    {
        // Arrange
        SetupCompleteValidRepository(_testWorkdir);

        // Act
        var problems = RepoChecks.ValidateLayout(_testWorkdir);

        // Assert
        Assert.That(problems, Is.Empty,
            "Should return no problems when repository layout is complete and valid");
    }

    [Test]
    public void ValidateLayout_WithMissingEpicsYaml_ReturnsProblem()
    {
        // Arrange
        SetupMinimalValidRepository(_testWorkdir);
        Directory.CreateDirectory(Path.Combine(_testWorkdir, "apps"));
        Directory.CreateDirectory(Path.Combine(_testWorkdir, "state", "runs"));

        // Act
        var problems = RepoChecks.ValidateLayout(_testWorkdir);

        // Assert
        var expectedMessage = $"Missing file: {Path.Combine("state", "epics.yaml")}";
        Assert.That(problems, Does.Contain(expectedMessage),
            "Should report missing state/epics.yaml file");
    }

    [Test]
    public void ValidateLayout_WithEpicsYaml_DoesNotReportEpicsMissing()
    {
        // Arrange
        SetupCompleteValidRepository(_testWorkdir);

        // Act
        var problems = RepoChecks.ValidateLayout(_testWorkdir);

        // Assert
        Assert.That(problems, Does.Not.Contain("Missing file: state/epics.yaml"),
            "Should NOT report missing state/epics.yaml when it exists");
    }

    [Test]
    public void ValidateLayout_WithMissingStateRunsDirectory_ReturnsProblem()
    {
        // Arrange
        SetupMinimalValidRepository(_testWorkdir);
        Directory.CreateDirectory(Path.Combine(_testWorkdir, "apps"));
        File.WriteAllText(Path.Combine(_testWorkdir, "state", "epics.yaml"), "# epics\n");
        // Intentionally do NOT create "state/runs" directory

        // Act
        var problems = RepoChecks.ValidateLayout(_testWorkdir);

        // Assert
        var expectedMessage = $"Missing directory: {Path.Combine("state", "runs")}";
        Assert.That(problems, Does.Contain(expectedMessage),
            "Should report missing state/runs directory");
    }

    [Test]
    public void ValidateLayout_WithStateRunsDirectory_DoesNotReportRunsMissing()
    {
        // Arrange
        SetupCompleteValidRepository(_testWorkdir);

        // Act
        var problems = RepoChecks.ValidateLayout(_testWorkdir);

        // Assert
        Assert.That(problems, Does.Not.Contain("Missing directory: state/runs"),
            "Should NOT report missing state/runs directory when it exists");
    }

    #endregion

    #region Helper Methods

    private void SetupMinimalValidRepository(string testDir)
    {
        // Create minimal required directories (excluding apps and state/runs for optional testing)
        var requiredDirs = new[]
        {
            "src",
            "state",
            Path.Combine("state", "decisions"),
            "prompts",
            Path.Combine("prompts", "personas"),
            Path.Combine("prompts", "flows")
        };

        foreach (var dir in requiredDirs)
        {
            Directory.CreateDirectory(Path.Combine(testDir, dir));
        }

        // Create all required files with minimal content
        var requiredFiles = new[]
        {
            Path.Combine("state", "team-board.md"),
            Path.Combine("state", "backlog.yaml"),
            Path.Combine("state", "risks.md"),
            Path.Combine("state", "decisions", "decision-log.md"),
            Path.Combine("prompts", "personas", "product-owner.md"),
            Path.Combine("prompts", "personas", "senior-architect-dev.md"),
            Path.Combine("prompts", "personas", "senior-audio-dev.md"),
            Path.Combine("prompts", "personas", "qa-engineer.md"),
            Path.Combine("prompts", "personas", "music-biz-specialist.md"),
            Path.Combine("prompts", "flows", "intake.md"),
            Path.Combine("prompts", "flows", "refine.md"),
            Path.Combine("prompts", "flows", "sprint-planning.md"),
            Path.Combine("prompts", "flows", "review.md")
        };

        foreach (var file in requiredFiles)
        {
            var filePath = Path.Combine(testDir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "# Test file\n");
        }
    }

    private void SetupCompleteValidRepository(string testDir)
    {
        // Create all required directories including apps and state/runs
        var requiredDirs = new[]
        {
            "src",
            "state",
            Path.Combine("state", "decisions"),
            Path.Combine("state", "runs"),
            "apps",
            "prompts",
            Path.Combine("prompts", "personas"),
            Path.Combine("prompts", "flows")
        };

        foreach (var dir in requiredDirs)
        {
            Directory.CreateDirectory(Path.Combine(testDir, dir));
        }

        // Create all required files with minimal content
        var requiredFiles = new[]
        {
            Path.Combine("state", "team-board.md"),
            Path.Combine("state", "backlog.yaml"),
            Path.Combine("state", "risks.md"),
            Path.Combine("state", "decisions", "decision-log.md"),
            Path.Combine("state", "epics.yaml"),
            Path.Combine("prompts", "personas", "product-owner.md"),
            Path.Combine("prompts", "personas", "senior-architect-dev.md"),
            Path.Combine("prompts", "personas", "senior-audio-dev.md"),
            Path.Combine("prompts", "personas", "qa-engineer.md"),
            Path.Combine("prompts", "personas", "music-biz-specialist.md"),
            Path.Combine("prompts", "flows", "intake.md"),
            Path.Combine("prompts", "flows", "refine.md"),
            Path.Combine("prompts", "flows", "sprint-planning.md"),
            Path.Combine("prompts", "flows", "review.md")
        };

        foreach (var file in requiredFiles)
        {
            var filePath = Path.Combine(testDir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "# Test file\n");
        }
    }

    #endregion
}
