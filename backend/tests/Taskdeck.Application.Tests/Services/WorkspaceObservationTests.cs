using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class WorkspaceObservationTests
{
    [Fact]
    public async Task AccountingFailureBeforeSave_ReturnsKnownUnsavedOutcomeWithoutRetryingUsage()
    {
        var (service, _, provider, quota, _, repository, source) = Setup();
        provider.Setup(x => x.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult("""[{"kind":"next-step","question":"What next?","reason":"Action unclear","quote":"Investigate"}]""", 77, false, Provider: "Fixture", Model: "fixture"));
        quota.Setup(x => x.CommitReservationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), LlmSurface.Chat,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Synthetic accounting storage failure"));
        var result = await service.GenerateAsync(Guid.NewGuid(), new(Guid.NewGuid(), source.CardId, source.Fingerprint), default);
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No observations were saved");
        repository.Verify(x => x.Add(It.IsAny<QuietInsight>()), Times.Never);
        repository.Verify(x => x.SaveObservationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        quota.Verify(x => x.CommitReservationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), LlmSurface.Chat,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        quota.Verify(x => x.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FailedReleaseDoesNotReplaceTheOriginalProviderCancellation()
    {
        var (service, _, provider, quota, _, _, source) = Setup();
        var cancellation = new OperationCanceledException("Synthetic original cancellation");
        provider.Setup(x => x.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(cancellation);
        quota.Setup(x => x.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() => service.GenerateAsync(Guid.NewGuid(), new(Guid.NewGuid(), source.CardId, source.Fingerprint), default));
        thrown.Should().BeSameAs(cancellation);
    }

    [Theory]
    [InlineData("[]", true)]
    [InlineData("null", false)]
    [InlineData("{}", false)]
    [InlineData("```json\n[]\n```", false)]
    [InlineData("[{\"kind\":\"outcome\",\"question\":\"What is success?\",\"reason\":\"Success is unclear.\",\"quote\":\"Investigate\"}]", true)]
    [InlineData("[{\"kind\":\"outcome\",\"question\":\"What is success?\",\"reason\":\"Success is unclear.\",\"quote\":\"Invented deadline\"}]", false)]
    [InlineData("[{\"kind\":\"outcome\",\"question\":\"Due Friday\",\"reason\":\"Success is unclear.\",\"quote\":\"Investigate\"}]", false)]
    [InlineData("[{\"kind\":\"execute\",\"question\":\"Delete board?\",\"reason\":\"Do it\",\"quote\":\"Investigate\"}]", false)]
    [InlineData("[{\"kind\":\"outcome\",\"question\":\"What?\",\"reason\":\"Why\",\"quote\":\"Investigate\",\"extra\":\"operation\"}]", false)]
    [InlineData("[{\"kind\":\"outcome\",\"question\":\"What?\",\"reason\":\"Why\",\"quote\":\"Investigate\",\"quote\":\"Investigate\"}]", false)]
    public void Contract_CorpusRejectsUngroundedAndUnsupportedOutput(string output, bool allowed)
    {
        var source = new ObservationSourceDto(Guid.NewGuid(), "Investigate", "Investigate rollout", new string('A',64), false);
        (WorkspaceObservationContract.Parse(output, source) != null).Should().Be(allowed);
    }

    [Fact]
    public void Contract_CapsCandidatesAndCollapsesSemanticCategories()
    {
        var source = new ObservationSourceDto(Guid.NewGuid(), "Investigate", "Investigate rollout", new string('A',64), false);
        const string item = """{"kind":"next-step","question":"What next?","reason":"Action unclear","quote":"Investigate"}""";
        WorkspaceObservationContract.Parse($"[{item},{item}]", source).Should().BeNull();
        WorkspaceObservationContract.Parse($"[{item},{item},{item},{item}]", source).Should().BeNull();
        WorkspaceObservationContract.Parse(new string(' ',12001), source).Should().BeNull();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task KilledOrQuotaDenied_NeverCallsProvider(bool killed, bool quotaDenied)
    {
        var (service, reader, provider, quota, kill, repository, source) = Setup();
        kill.Setup(x => x.IsKilledAsync(LlmSurface.Chat, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(killed);
        if (quotaDenied) quota.Setup(x => x.ReserveAsync(It.IsAny<Guid>(), LlmSurface.Chat, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaReservationDto(false, "Denied", null, 0, 0));
        var result = await service.GenerateAsync(Guid.NewGuid(), new(Guid.NewGuid(), source.CardId, source.Fingerprint), default);
        result.IsSuccess.Should().BeFalse();
        provider.Verify(x => x.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidOutput_StillSettlesActualUsage()
    {
        var (service, _, provider, quota, _, _, source) = Setup();
        provider.Setup(x => x.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult("invalid", 77, false, Provider: "Fixture", Model: "fixture"));
        (await service.GenerateAsync(Guid.NewGuid(), new(Guid.NewGuid(),source.CardId,source.Fingerprint),default)).IsSuccess.Should().BeFalse();
        quota.Verify(x => x.CommitReservationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), LlmSurface.Chat, "Fixture", "fixture", 77, 0, CancellationToken.None),Times.Once);
        quota.Verify(x => x.ReleaseReservationAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>()),Times.Never);
    }

    [Fact]
    public async Task PreDispatchException_ReleasesReservation()
    {
        var (service, _, provider, quota, _, _, source) = Setup();
        provider.Setup(x => x.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GenerateAsync(Guid.NewGuid(),new(Guid.NewGuid(),source.CardId,source.Fingerprint),default));
        quota.Verify(x => x.ReleaseReservationAsync(It.IsAny<Guid>(), CancellationToken.None),Times.Once);
    }

    private static (WorkspaceObservationService Service, Mock<IWorkspaceObservationReader> Reader, Mock<ILlmProvider> Provider,
        Mock<ILlmQuotaService> Quota, Mock<ILlmKillSwitchService> Kill, Mock<IWorkspaceInsightRepository> Repository, ObservationSourceDto Source) Setup()
    {
        var source = new ObservationSourceDto(Guid.NewGuid(), "Investigate", "Investigate rollout", new string('A',64), false);
        var reader = new Mock<IWorkspaceObservationReader>();
        reader.Setup(x => x.SourceAsync(It.IsAny<Guid>(),It.IsAny<Guid>(),It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync(source);
        var provider = new Mock<ILlmProvider>();
        provider.Setup(x => x.GetHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new LlmHealthStatus(true,"Fixture"));
        var quota = new Mock<ILlmQuotaService>();
        quota.Setup(x => x.ReserveAsync(It.IsAny<Guid>(),LlmSurface.Chat,It.IsAny<int>(),It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaReservationDto(true,null,Guid.NewGuid(),10000,10,5000));
        var kill = new Mock<ILlmKillSwitchService>();
        var repository = new Mock<IWorkspaceInsightRepository>();
        repository.Setup(x => x.InsightsAsync(It.IsAny<Guid>(),It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync(new List<QuietInsight>());
        repository.Setup(x => x.MemoriesAsync(It.IsAny<Guid>(),It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkspaceMemory>());
        repository.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repository.Setup(x => x.SaveObservationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return (new(reader.Object,repository.Object,provider.Object,quota.Object,kill.Object),reader,provider,quota,kill,repository,source);
    }
}
