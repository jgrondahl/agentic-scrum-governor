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
    private RefineTechUseCase _useCase = null!;

    [SetUp]
    public void Setup()
    {
        _backlogStoreMock = new Mock<IBacklogStore>();
        _runArtifactStoreMock = new Mock<IRunArtifactStore>();

        _useCase = new RefineTechUseCase(
            _backlogStoreMock.Object,
            _runArtifactStoreMock.Object);
    }

    [Test]
    public void Process_WithValidItem_ProducesTypedPatchPreview()
    {
        // Arrange
        var backlog = new BacklogFile
        {
            Backlog = new List<BacklogItem>
            {
                new BacklogItem { Id = 1, Title = "Test Item", Status = "candidate" }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        var request = new RefineTechRequest
        {
            ItemId = 1,
            BacklogPath = "/tmp/backlog.yaml",
            RunsDir = "/tmp/runs",
            Workdir = "/tmp",
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
        _runArtifactStoreMock.Verify(s => s.WriteJson(It.IsAny<string>(), "patch.preview.json", It.IsAny<PatchPreview>()), Times.Once);
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
            BacklogPath = "/tmp/backlog.yaml",
            RunsDir = "/tmp/runs",
            Workdir = "/tmp",
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
                new BacklogItem { Id = 1, Title = "Test", Status = "candidate", Estimate = null }
            }
        };

        _backlogStoreMock.Setup(s => s.Load(It.IsAny<string>()))
            .Returns(backlog);

        _runArtifactStoreMock.Setup(s => s.CreateRunFolder(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("/tmp/run");

        var request = new RefineTechRequest
        {
            ItemId = 1,
            BacklogPath = "/tmp/backlog.yaml",
            RunsDir = "/tmp/runs",
            Workdir = "/tmp",
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
            s => s.WriteJson(It.IsAny<string>(), "patch.json", It.IsAny<AppliedPatch>()),
            Times.Once);
    }
}
