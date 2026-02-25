using Moq;
using NUnit.Framework;
using GovernorCli.Application.Models.Deliver;
using GovernorCli.Application.Stores;
using GovernorCli.Application.UseCases;
using GovernorCli.Domain.Enums;
using GovernorCli.Flows;
using GovernorCli.State;

namespace GovernorCli.Tests.Flows;

[TestFixture]
public class DeliverFlowTests
{
    private Mock<IDeliverUseCase> _useCaseMock = null!;
    private Mock<IBacklogStore> _backlogStoreMock = null!;
    private Mock<IEpicStore> _epicStoreMock = null!;
    private Mock<IWorkspaceStore> _workspaceStoreMock = null!;
    private Mock<IRunArtifactStore> _runArtifactStoreMock = null!;
    private Mock<IAppDeployer> _appDeployerMock = null!;
    private Mock<IDecisionStore> _decisionStoreMock = null!;
    private DeliverFlow _flow = null!;
    private string _testWorkdir = null!;

    [SetUp]
    public void Setup()
    {
        _useCaseMock = new Mock<IDeliverUseCase>();

        _backlogStoreMock = new Mock<IBacklogStore>();
        _epicStoreMock = new Mock<IEpicStore>();
        _workspaceStoreMock = new Mock<IWorkspaceStore>();
        _runArtifactStoreMock = new Mock<IRunArtifactStore>();
        _appDeployerMock = new Mock<IAppDeployer>();
        _decisionStoreMock = new Mock<IDecisionStore>();

        _flow = new DeliverFlow(
            _useCaseMock.Object,
            _backlogStoreMock.Object,
            _epicStoreMock.Object,
            _workspaceStoreMock.Object,
            _runArtifactStoreMock.Object,
            _appDeployerMock.Object,
            _decisionStoreMock.Object);

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
            Path.Combine("state", "plans"),
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
            Path.Combine("prompts", "flows", "refine-tech.md"),
            Path.Combine("prompts", "flows", "sprint-planning.md"),
            Path.Combine("prompts", "flows", "review.md")
        };

        foreach (var file in requiredFiles)
        {
            var filePath = Path.Combine(testDir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "# Test file\n");
        }

        // Create plan file for delivery tests
        var planDir = Path.Combine(testDir, "state", "plans", "item-1");
        Directory.CreateDirectory(planDir);
        File.WriteAllText(Path.Combine(planDir, "implementation.plan.json"), "{}");
    }

    [Test]
    public void Execute_WithInvalidRepoLayout_ReturnsInvalidRepoLayout()
    {
        // Act
        var exitCode = _flow.Execute("/nonexistent/repo", 1, false, false);

        // Assert
        Assert.That(exitCode, Is.EqualTo(FlowExitCode.InvalidRepoLayout));
    }

    [Test]
    public void Execute_ApproveTrue_WithValidationFailed_DoesNotAppendDecision()
    {
        // Governance: --approve with validation failed → NO decision log append
        // This test must be in Flow tests because decision logging is Flow's responsibility
        // (Though we verify via UseCase returning validation failed)

        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem 
                { 
                    Id = 1, 
                    Title = "Test", 
                    Status = "ready_for_dev",
                    EpicId = "epic-1",
                    ImplementationPlanRef = "state/plans/item-1/implementation.plan.json",
                    Estimate = new BacklogEstimate { StoryPoints = 5 }
                }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _epicStoreMock.Setup(s => s.ResolveAppId(It.IsAny<string>(), "epic-1"))
            .Returns("myapp");

        _workspaceStoreMock.Setup(s => s.ResetAndCreateWorkspace(It.IsAny<string>(), "myapp"))
            .Returns("/tmp/state/workspaces/myapp");

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        // UseCase returns validation FAILED
        var failedResponse = new DeliverResponse
        {
            Success = false,
            ValidationPassed = false,  // 🚨 Validation failed
            Result = new DeliverResult { }
        };
        _useCaseMock.Setup(s => s.Process(It.IsAny<DeliverRequest>()))
            .Returns(failedResponse);

        // Act
        var exitCode = _flow.Execute(_testWorkdir, 1, false, approve: true);

        // Assert
        Assert.That(exitCode, Is.EqualTo(FlowExitCode.ValidationFailed));

        // ✅ GOVERNANCE: Decision log NOT appended when validation fails
        _decisionStoreMock.Verify(
            s => s.LogDecision(It.IsAny<string>(), It.IsAny<string>()), 
            Times.Never,
            "Decision log should NOT be appended when validation fails, even with --approve");
    }

    [Test]
    public void Execute_ApproveTrue_WithValidationPassed_AppendsDecision()
    {
        // Governance: --approve with validation passed → decision log MUST be appended

        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem 
                { 
                    Id = 1, 
                    Title = "Test", 
                    Status = "ready_for_dev",
                    EpicId = "epic-1",
                    ImplementationPlanRef = "state/plans/item-1/implementation.plan.json",
                    Estimate = new BacklogEstimate { StoryPoints = 5 }
                }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _epicStoreMock.Setup(s => s.ResolveAppId(It.IsAny<string>(), "epic-1"))
            .Returns("myapp");

        _workspaceStoreMock.Setup(s => s.ResetAndCreateWorkspace(It.IsAny<string>(), "myapp"))
            .Returns("/tmp/state/workspaces/myapp");

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        // UseCase returns validation PASSED
        var deployedFiles = new List<PatchFile> { new PatchFile { Action = "A", Path = "app/file.txt", Size = 100, WorkspaceSha256 = "abc" } };
        var successResponse = new DeliverResponse
        {
            Success = true,
            ValidationPassed = true,  // ✅ Validation passed
            Result = new DeliverResult { Plan = new() { ItemId = 1 } }
        };
        _useCaseMock.Setup(s => s.Process(It.IsAny<DeliverRequest>()))
            .Returns(successResponse);

        _appDeployerMock.Setup(s => s.Deploy(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(deployedFiles);

        // Act
        var exitCode = _flow.Execute(_testWorkdir, 1, false, approve: true);

        // Assert
        Assert.That(exitCode, Is.EqualTo(FlowExitCode.Success));

        // ✅ GOVERNANCE: Decision log APPENDED when validation passed and approval given
        _decisionStoreMock.Verify(
            s => s.LogDecision(It.IsAny<string>(), It.IsAny<string>()), 
            Times.Once,
            "Decision log MUST be appended when validation passed and --approve given");
    }
}
