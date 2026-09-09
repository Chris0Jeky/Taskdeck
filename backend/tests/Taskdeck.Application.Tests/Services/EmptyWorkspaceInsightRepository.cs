using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Tests.Services;

internal static class EmptyWorkspaceInsightRepository
{
    public static IWorkspaceInsightRepository Create()
    {
        var repository = new Mock<IWorkspaceInsightRepository>();
        repository.Setup(x => x.MemoriesByUserAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkspaceMemory>());
        repository.Setup(x => x.InsightsByUserAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<QuietInsight>());
        return repository.Object;
    }
}
