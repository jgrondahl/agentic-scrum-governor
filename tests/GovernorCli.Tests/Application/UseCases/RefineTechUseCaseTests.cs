using Moq;
using NUnit.Framework;
using GovernorCli.Application.Models;
using GovernorCli.Application.Stores;
using GovernorCli.Application.UseCases;
using GovernorCli.Domain.Exceptions;
using GovernorCli.State;

namespace GovernorCli.Tests.Application.UseCases;

[TestFixture]
public class RefineTechUseCaseTests
{
    private Mock<IBacklogStore> _backlogStoreMock = null!;
    private Mock<IRunArtifactStore> _runArtifactStoreMock = null!;
    private Mock<IEpicStore> _epicStoreMock = null!;
    private Mock<IPlanStore> _planStoreMock = null!;
    private Mock<IPatchPreviewService> _patchPreviewServiceMock = null!;
    private RefineTechUseCase _useCase = null!;
    private string _testWorkdir = null!;

    [SetUp]
    public void Setup()
    {
        _backlogStoreMock = new Mock<IBacklogStore>();
        _runArtifactStoreMock = new Mock<IRunArtifactStore>();
        _epicStoreMock = new Mock<IEpicStore>();
        _planStoreMock = new Mock<IPlanStore>();
        _patchPreviewServiceMock = new Mock<IPatchPreviewService>();

        _useCase = new RefineTechUseCase(
            _backlogStoreMock.Object,
            _runArtifactStoreMock.Object,
            _epicStoreMock.Object,
            _planStoreMock.Object,
            _patchPreviewServiceMock.Object);

        _testWorkdir = Path.Combine(Path.GetTempPath(), $"governor-test-{Guid.NewGuid():N}");
        SetupTestPrompts(_testWorkdir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testWorkdir))
        {
            try { Directory.Delete(_testWorkdir, recursive: true); }
            catch { }
        }
    }

    private void SetupTestPrompts(string testDir)
    {
        Directory.CreateDirectory(Path.Combine(testDir, "prompts", "flows"));
        Directory.CreateDirectory(Path.Combine(testDir, "prompts", "personas"));

        File.WriteAllText(Path.Combine(testDir, "prompts", "flows", "refine-tech.md"), "# Test refine-tech prompt\n");
        File.WriteAllText(Path.Combine(testDir, "prompts", "personas", "senior-architect-dev.md"), "# Test SAD prompt\n");
        File.WriteAllText(Path.Combine(testDir, "prompts", "personas", "senior-audio-dev.md"), "# Test SASD prompt\n");
        File.WriteAllText(Path.Combine(testDir, "prompts", "personas", "qa-engineer.md"), "# Test QA prompt\n");
    }

    [Test]
    public void Process_WithValidItem_ProducesTypedPatchPreview()
    {
        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem 
                { 
                    Id = 1, 
                    Title = "Test Item", 
                    Status = "candidate",
                    EpicId = "epic-1"
                }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        _epicStoreMock.Setup(s => s.ResolveAppId(It.IsAny<string>(), "epic-1"))
            .Returns("test-app");

        var patchPreview = new PatchPreviewData
        {
            ComputedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            ItemId = 1,
            Changes = new List<PatchFileChange>()
        };

        _patchPreviewServiceMock.Setup(s => s.ComputePatchPreview(It.IsAny<string>(), 1, It.IsAny<string>()))
            .Returns(patchPreview);

        _patchPreviewServiceMock.Setup(s => s.FormatDiffLines(It.IsAny<PatchPreviewData>()))
            .Returns(new List<string>());

        var request = new RefineTechRequest
        {
            ItemId = 1,
            BacklogPath = Path.Combine(_testWorkdir, "backlog.yaml"),
            RunsDir = Path.Combine(_testWorkdir, "runs"),
            Workdir = _testWorkdir,
            RunId = "20240115_103000_refine-tech_item-1",
            Approve = false
        };

        // Act
        var result = _useCase.Process(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.RunId, Is.EqualTo("20240115_103000_refine-tech_item-1"));

        // ✅ Patch is TYPED, not anonymous object
        Assert.That(result.Patch, Is.Not.Null);
        Assert.That(result.Patch!.ItemId, Is.EqualTo(1));
        Assert.That(result.Patch.Changes.Status.Before, Is.EqualTo("candidate"));
        Assert.That(result.Patch.Changes.Status.After, Is.EqualTo("ready_for_dev"));

        // Verify stores called correctly
        _backlogStoreMock.Verify(s => s.Load(It.IsAny<string>()), Times.Once);
        _runArtifactStoreMock.Verify(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _runArtifactStoreMock.Verify(s => s.WriteJson(It.IsAny<string>(), "implementation.plan.json", It.IsAny<ImplementationPlan>()), Times.Once);
    }

    [Test]
    public void Process_WithMissingItem_ThrowsItemNotFound()
    {
        // Arrange
        var backlog = new BacklogFile { Backlog = new List<BacklogItem>() };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        var request = new RefineTechRequest
        {
            ItemId = 999,
            BacklogPath = Path.Combine(_testWorkdir, "backlog.yaml"),
            RunsDir = Path.Combine(_testWorkdir, "runs"),
            Workdir = _testWorkdir,
            RunId = "20240115_103000_refine-tech_item-999",
            Approve = false
        };

        // Act & Assert
        Assert.Throws<ItemNotFoundException>(() => _useCase.Process(request));
    }

    [Test]
    public void Process_WithApproveTrue_PersistsBacklog()
    {
        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem { Id = 1, Title = "Test", Status = "candidate", EpicId = "epic-1", Estimate = null }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _backlogStoreMock.Setup(s => s.Save(It.IsAny<string>(), It.IsAny<BacklogFile>()))
            .Verifiable();

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        _runArtifactStoreMock.Setup(s => s.WriteJson(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
            .Verifiable();

        _runArtifactStoreMock.Setup(s => s.WriteText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Verifiable();

        _epicStoreMock.Setup(s => s.ResolveAppId(It.IsAny<string>(), "epic-1"))
            .Returns("test-app");

        var patchPreview = new PatchPreviewData
        {
            ComputedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            ItemId = 1,
            Changes = new List<PatchFileChange>()
        };

        _patchPreviewServiceMock.Setup(s => s.ComputePatchPreview(It.IsAny<string>(), 1, It.IsAny<string>()))
            .Returns(patchPreview);

        _patchPreviewServiceMock.Setup(s => s.FormatDiffLines(It.IsAny<PatchPreviewData>()))
            .Returns(new List<string>());

        _planStoreMock.Setup(s => s.GetPlanPath(It.IsAny<string>(), 1))
            .Returns("/tmp/plans/item-1/implementation.plan.json");

        _planStoreMock.Setup(s => s.SavePlan(It.IsAny<string>(), 1, It.IsAny<ImplementationPlan>()))
            .Verifiable(); // Setup SavePlan to succeed

        var request = new RefineTechRequest
        {
            ItemId = 1,
            BacklogPath = Path.Combine(_testWorkdir, "backlog.yaml"),
            RunsDir = Path.Combine(_testWorkdir, "runs"),
            Workdir = _testWorkdir,
            RunId = "20240115_103000_refine-tech_item-1",
            Approve = true  // ← Key difference
        };

        // Act
        var result = _useCase.Process(request);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify backlog was saved
        _backlogStoreMock.Verify(s => s.Save(It.IsAny<string>(), It.IsAny<BacklogFile>()), Times.Once);

        // Verify applied patch was written
        _runArtifactStoreMock.Verify(
            s => s.WriteJson(It.IsAny<string>(), "patch.backlog.applied.json", It.IsAny<object>()),
            Times.Once);
    }
}
