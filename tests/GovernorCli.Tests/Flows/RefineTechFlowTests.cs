using Moq;
using NUnit.Framework;
using GovernorCli.Application.Stores;
using GovernorCli.Application.UseCases;
using GovernorCli.Domain.Enums;
using GovernorCli.Domain.Exceptions;
using GovernorCli.Flows;
using GovernorCli.State;

namespace GovernorCli.Tests.Flows;

[TestFixture]
public class RefineTechFlowTests
{
    private Mock<IBacklogStore> _backlogStoreMock = null!;
    private Mock<IRunArtifactStore> _runArtifactStoreMock = null!;
    private Mock<IDecisionStore> _decisionStoreMock = null!;
    private RefineTechFlow _flow = null!;
    private string _testWorkdir = null!;

    [SetUp]
    public void Setup()
    {
        _backlogStoreMock = new Mock<IBacklogStore>();
        _runArtifactStoreMock = new Mock<IRunArtifactStore>();
        _decisionStoreMock = new Mock<IDecisionStore>();

        var useCase = new RefineTechUseCase(
            _backlogStoreMock.Object,
            _runArtifactStoreMock.Object);

        _flow = new RefineTechFlow(useCase, _decisionStoreMock.Object);

        // Create a temporary test directory that passes repo layout validation
        _testWorkdir = Path.Combine(Path.GetTempPath(), $"governor-test-{Guid.NewGuid():N}");
        SetupValidTestRepository(_testWorkdir);
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

    private void SetupValidTestRepository(string testDir)
    {
        // Create all required directories
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

    [Test]
    public void Execute_WithValidItem_ReturnsSuccess()
    {
        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem { Id = 1, Title = "Test" }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Path.Combine(_testWorkdir, "state", "runs", "test-run"));

        // Act
        var exitCode = _flow.Execute(_testWorkdir, 1, false, false);

        // Assert
        Assert.That(exitCode, Is.EqualTo(FlowExitCode.Success));
    }

    [Test]
    public void Execute_WithMissingItem_ReturnsItemNotFoundExitCode()
    {
        // Arrange
        var backlog = new BacklogFile { Backlog = new List<BacklogItem>() };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        // Act
        var exitCode = _flow.Execute(_testWorkdir, 999, false, false);

        // Assert
        // ✅ Exception is caught and mapped to exit code
        Assert.That(exitCode, Is.EqualTo(FlowExitCode.ItemNotFound));
    }

    [Test]
    public void Execute_WithParseError_ReturnsBacklogParseError()
    {
        // Arrange
        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Throws(new BacklogParseException(Path.Combine(_testWorkdir, "state", "backlog.yaml"), "Invalid YAML"));

        // Act
        var exitCode = _flow.Execute(_testWorkdir, 1, false, false);

        // Assert
        Assert.That(exitCode, Is.EqualTo(FlowExitCode.BacklogParseError));
    }

    [Test]
    public void Execute_WithApproveTrue_LogsDecision()
    {
        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem { Id = 1, Title = "Test", Status = "candidate" }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Path.Combine(_testWorkdir, "state", "runs", "test-run"));

        // Act
        var exitCode = _flow.Execute(_testWorkdir, 1, false, approve: true);

        // Assert
        Assert.That(exitCode, Is.EqualTo(FlowExitCode.Success));

        // ✅ Verify decision was logged (Flow responsibility)
        _decisionStoreMock.Verify(
            s => s.LogDecision(It.IsAny<string>(), It.Is<string>(e => e.Contains("refine-tech approved"))),
            Times.Once);
    }

    [Test]
    public void Execute_WithMissingItem_DoesNotLogDecision()
    {
        // Arrange: Item not found (will cause ItemNotFoundException)
        var backlog = new BacklogFile { Backlog = new List<BacklogItem>() };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        // Act: Try to approve missing item
        var exitCode = _flow.Execute(_testWorkdir, 999, false, approve: true);

        // Assert
        Assert.That(exitCode, Is.EqualTo(FlowExitCode.ItemNotFound));

        // ✅ Critical: Decision NOT logged on error (even if approve=true)
        _decisionStoreMock.Verify(
            s => s.LogDecision(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }
}
