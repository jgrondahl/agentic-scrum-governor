using Moq;
using NUnit.Framework;
using GovernorCli.Application.Models.Deliver;
using GovernorCli.Application.Stores;
using GovernorCli.Application.UseCases;
using GovernorCli.Domain.Enums;
using GovernorCli.Domain.Exceptions;
using GovernorCli.State;

namespace GovernorCli.Tests.Application.UseCases;

[TestFixture]
public class DeliverUseCaseTests
{
    private Mock<IBacklogStore> _backlogStoreMock = null!;
    private Mock<IRunArtifactStore> _runArtifactStoreMock = null!;
    private Mock<IProcessRunner> _processRunnerMock = null!;
    private Mock<IAppDeployer> _appDeployerMock = null!;
    private DeliverUseCase _useCase = null!;

    [SetUp]
    public void Setup()
    {
        _backlogStoreMock = new Mock<IBacklogStore>();
        _runArtifactStoreMock = new Mock<IRunArtifactStore>();
        _processRunnerMock = new Mock<IProcessRunner>();
        _appDeployerMock = new Mock<IAppDeployer>();

        _useCase = new DeliverUseCase(
            _backlogStoreMock.Object,
            _runArtifactStoreMock.Object,
            _processRunnerMock.Object,
            _appDeployerMock.Object);
    }

    [Test]
    public void Process_WithValidItemAndPassingValidation_ProducesTypedResult()
    {
        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem { Id = 1, Title = "Test Item", Status = "ready_for_dev" }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        // Mock successful build and run
        _processRunnerMock.Setup(s => s.Run(It.IsAny<AllowedProcess>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(0);

        var request = new DeliverRequest
        {
            ItemId = 1,
            AppId = "myapp",
            TemplateId = "fixture_dotnet_console_hello",
            BacklogPath = "/tmp/backlog.yaml",
            RunsDir = "/tmp/runs",
            Workdir = "/tmp",
            WorkspaceRoot = "/tmp/state/workspaces/myapp",
            RunId = "20240115_103000_deliver_item-1",
            Approve = false
        };

        // Act
        var response = _useCase.Process(request);

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.ValidationPassed, Is.True);
        
        // ✅ Result is TYPED, not anonymous object
        Assert.That(response.Result, Is.Not.Null);
        Assert.That(response.Result.Plan, Is.Not.Null);
        Assert.That(response.Result.Plan.ItemId, Is.EqualTo(1));
        Assert.That(response.Result.Plan.AppId, Is.EqualTo("myapp"));
        Assert.That(response.Result.Plan.TemplateId, Is.EqualTo("fixture_dotnet_console_hello"));
        
        Assert.That(response.Result.Validation, Is.Not.Null);
        Assert.That(response.Result.Validation.Passed, Is.True);
        Assert.That(response.Result.Validation.Commands, Has.Count.EqualTo(2));

        Assert.That(response.Result.Preview, Is.Not.Null);
        Assert.That(response.Result.Preview.AppId, Is.EqualTo("myapp"));
        Assert.That(response.Result.Preview.ValidationPassed, Is.True);

        // PatchApplied should be null (not approved)
        Assert.That(response.Result.PatchApplied, Is.Null);

        // Verify stores called correctly
        _backlogStoreMock.Verify(s => s.Load(It.IsAny<string>()), Times.Once);
        _runArtifactStoreMock.Verify(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _runArtifactStoreMock.Verify(s => s.WriteJson(It.IsAny<string>(), "implementation-plan.json", It.IsAny<ImplementationPlan>()), Times.Once);
        _runArtifactStoreMock.Verify(s => s.WriteJson(It.IsAny<string>(), "validation.json", It.IsAny<ValidationReport>()), Times.Once);
    }

    [Test]
    public void Process_WithFailingValidation_ReturnsFailedValidationWithoutDeploy()
    {
        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem { Id = 1, Title = "Test Item", Status = "ready_for_dev" }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        // Mock failing build
        _processRunnerMock.Setup(s => s.Run(It.IsAny<AllowedProcess>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(1);

        var request = new DeliverRequest
        {
            ItemId = 1,
            AppId = "myapp",
            TemplateId = "fixture_dotnet_console_hello",
            BacklogPath = "/tmp/backlog.yaml",
            RunsDir = "/tmp/runs",
            Workdir = "/tmp",
            WorkspaceRoot = "/tmp/state/workspaces/myapp",
            RunId = "20240115_103000_deliver_item-1",
            Approve = true  // Even with approval, should fail if validation fails
        };

        // Act
        var response = _useCase.Process(request);

        // Assert
        Assert.That(response.ValidationPassed, Is.False);
        
        // PatchApplied should be null (validation failed)
        Assert.That(response.Result.PatchApplied, Is.Null);
        
        // Deployer should NOT be called
        _appDeployerMock.Verify(s => s.Deploy(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void Process_WithApprovalAndPassingValidation_DeploysAndReturnsAppliedPatch()
    {
        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem { Id = 1, Title = "Test Item", Status = "ready_for_dev" }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        // Mock successful build and run
        _processRunnerMock.Setup(s => s.Run(It.IsAny<AllowedProcess>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(0);

        var deployedFiles = new List<PatchFile> 
        { 
            new PatchFile { Action = "A", Path = "myapp/Program.cs", Size = 100, WorkspaceSha256 = "abc123" },
            new PatchFile { Action = "A", Path = "myapp/myapp.csproj", Size = 200, WorkspaceSha256 = "def456" }
        };
        _appDeployerMock.Setup(s => s.Deploy(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(deployedFiles);

        var request = new DeliverRequest
        {
            ItemId = 1,
            AppId = "myapp",
            TemplateId = "fixture_dotnet_console_hello",
            BacklogPath = "/tmp/backlog.yaml",
            RunsDir = "/tmp/runs",
            Workdir = "/tmp",
            WorkspaceRoot = "/tmp/state/workspaces/myapp",
            RunId = "20240115_103000_deliver_item-1",
            Approve = true
        };

        // Act
        var response = _useCase.Process(request);

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.ValidationPassed, Is.True);
        
        // PatchApplied should be set
        Assert.That(response.Result.PatchApplied, Is.Not.Null);
        Assert.That(response.Result.PatchApplied!.ItemId, Is.EqualTo(1));
        Assert.That(response.Result.PatchApplied.AppId, Is.EqualTo("myapp"));
        Assert.That(response.Result.PatchApplied.FilesApplied, Is.EqualTo(deployedFiles));

        // Deployer should be called
        _appDeployerMock.Verify(s => s.Deploy(It.IsAny<string>(), It.IsAny<string>(), "myapp"), Times.Once);
    }

    [Test]
    public void Process_WithMissingItem_ThrowsItemNotFound()
    {
        // Arrange
        var backlog = new BacklogFile { Backlog = new List<BacklogItem>() };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        var request = new DeliverRequest
        {
            ItemId = 999,
            AppId = "myapp",
            BacklogPath = "/tmp/backlog.yaml",
            RunsDir = "/tmp/runs",
            Workdir = "/tmp",
            WorkspaceRoot = "/tmp/state/workspaces/myapp",
            RunId = "20240115_103000_deliver_item-999",
            Approve = false
        };

        // Act & Assert
        Assert.Throws<ItemNotFoundException>(() => _useCase.Process(request));
    }

    [Test]
    public void Process_WithValidationFailed_DoesNotCallDeployer()
    {
        // Governance: If validation fails, deployer must NOT be called
        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem { Id = 1, Title = "Test Item", Status = "ready_for_dev" }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        // Mock FAILING build (exit code 1)
        _processRunnerMock.Setup(s => s.Run(It.IsAny<AllowedProcess>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(1);

        var request = new DeliverRequest
        {
            ItemId = 1,
            AppId = "myapp",
            TemplateId = "fixture_dotnet_console_hello",
            BacklogPath = "/tmp/backlog.yaml",
            RunsDir = "/tmp/runs",
            RunDir = "/tmp/run",
            Workdir = "/tmp",
            WorkspaceRoot = "/tmp/state/workspaces/myapp",
            RunId = "20240115_103000_deliver_item-1",
            Approve = false
        };

        // Act
        var response = _useCase.Process(request);

        // Assert
        Assert.That(response.ValidationPassed, Is.False);

        // ✅ GOVERNANCE: Deployer must NOT be called when validation fails
        _appDeployerMock.Verify(s => s.Deploy(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void Process_ApproveTrue_WithValidationFailed_DoesNotDeploy()
    {
        // Governance: Even with --approve, if validation fails, NO deploy
        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem { Id = 1, Title = "Test Item", Status = "ready_for_dev" }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        // Mock FAILING build
        _processRunnerMock.Setup(s => s.Run(It.IsAny<AllowedProcess>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(1);

        var request = new DeliverRequest
        {
            ItemId = 1,
            AppId = "myapp",
            TemplateId = "fixture_dotnet_console_hello",
            BacklogPath = "/tmp/backlog.yaml",
            RunsDir = "/tmp/runs",
            RunDir = "/tmp/run",
            Workdir = "/tmp",
            WorkspaceRoot = "/tmp/state/workspaces/myapp",
            RunId = "20240115_103000_deliver_item-1",
            Approve = true  // 🚨 Even with approval flag
        };

        // Act
        var response = _useCase.Process(request);

        // Assert
        Assert.That(response.ValidationPassed, Is.False);

        // ✅ STRICT APPROVAL GATE: Validation failed, so NO deploy despite --approve
        _appDeployerMock.Verify(s => s.Deploy(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void Process_ApproveTrue_WithValidationPassed_CallsDeploy()
    {
        // Governance: With --approve and validation passed, MUST deploy
        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem { Id = 1, Title = "Test Item", Status = "ready_for_dev" }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        // Mock PASSING validation
        _processRunnerMock.Setup(s => s.Run(It.IsAny<AllowedProcess>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(0);

        var deployedFiles = new List<PatchFile> 
        { 
            new PatchFile { Action = "A", Path = "myapp/Program.cs", Size = 100, WorkspaceSha256 = "abc123" }
        };
        _appDeployerMock.Setup(s => s.Deploy(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(deployedFiles);

        var request = new DeliverRequest
        {
            ItemId = 1,
            AppId = "myapp",
            TemplateId = "fixture_dotnet_console_hello",
            BacklogPath = "/tmp/backlog.yaml",
            RunsDir = "/tmp/runs",
            RunDir = "/tmp/run",
            Workdir = "/tmp",
            WorkspaceRoot = "/tmp/state/workspaces/myapp",
            RunId = "20240115_103000_deliver_item-1",
            Approve = true
        };

        // Act
        var response = _useCase.Process(request);

        // Assert
        Assert.That(response.ValidationPassed, Is.True);
        Assert.That(response.Result.PatchApplied, Is.Not.Null);

        // ✅ DEPLOYMENT EXECUTED: Validation passed and approve=true
        _appDeployerMock.Verify(s => s.Deploy(It.IsAny<string>(), It.IsAny<string>(), "myapp"), Times.Once);
    }
}
