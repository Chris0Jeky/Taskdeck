using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Full-HTTP golden-path integration tests for the LLM-backed transcript triage lane
/// (REVIVAL-08 M1): transcript capture -> triage -> TranscriptTriageWorker ->
/// ICaptureTriageService -> ILlmCaptureTriageExtractor against a DI-substituted non-mock
/// ILlmProvider stub -> proposal -> approve/execute -> board cards. Mirrors the flow of
/// <see cref="CaptureToBoardGoldenPathIntegrationTests"/> and the provider stub substitution
/// pattern of <see cref="ChatApiLiveProviderStubTests"/>.
/// </summary>
public class TranscriptTriageLlmGoldenPathIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private const string StubProviderName = "StubOpenAI";
    private const string StubModelName = "stub-model";

    /// <summary>
    /// Transcript-shaped capture text. The LLM golden-path test never depends on its structure
    /// (the stub returns canned tasks), but the evidence quotes below are verbatim lines from it,
    /// matching the contract the real prompt demands.
    /// </summary>
    private const string TranscriptText =
        "Alice: Thanks everyone for joining the weekly sync.\n" +
        "Bob: I will send the Q3 budget summary to finance by Friday.\n" +
        "Alice: Great. Let's schedule a follow-up demo with the pilot team next week.\n" +
        "Carol: Someone needs to file the onboarding regression bug today.\n" +
        "Alice: Perfect, that wraps it up.";

    /// <summary>
    /// Transcript text crafted so the deterministic extractor's whole-text fallback yields exactly
    /// one card: no checklist/bullet/numbered lines, no " - " dash delimiters, no semicolons.
    /// </summary>
    private const string FallbackTranscriptText =
        "Team sync notes covering the pilot rollout.\n" +
        "We walked through the release timeline and the pilot feedback in detail.\n" +
        "No owners were assigned during the call.";

    private static readonly (string Title, string Evidence)[] StubTasks =
    [
        ("Send the Q3 budget summary to finance",
            "I will send the Q3 budget summary to finance by Friday."),
        ("Schedule a follow-up demo with the pilot team",
            "Let's schedule a follow-up demo with the pilot team next week."),
        ("File the onboarding regression bug",
            "Someone needs to file the onboarding regression bug today.")
    ];

    /// <summary>The exact {"tasks":[{"title","evidence"}]} shape LlmCaptureTriagePrompt.TryParseTasks expects.</summary>
    private static readonly string StubTasksJson = JsonSerializer.Serialize(new
    {
        tasks = StubTasks.Select(task => new { title = task.Title, evidence = task.Evidence }).ToArray()
    });

    private readonly TestWebApplicationFactory _baseFactory;

    public TranscriptTriageLlmGoldenPathIntegrationTests(TestWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task LlmGoldenPath_TranscriptCaptureTriageApproveExecute_ShouldCreateStubCardsWithLlmProvenance()
    {
        var providerStub = new TriageJsonProviderStub(StubTasksJson);
        using var factory = CreateFactoryWithProviderStub(providerStub);
        using var client = factory.CreateClient();

        var user = await ApiTestHarness.AuthenticateAsync(client, "transcript-llm-golden");
        var (board, column) = await CreateBoardWithBacklogColumnAsync(client, "transcript-llm-golden-board");

        // Act 1: Create a transcript-source capture
        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, TranscriptText, "TranscriptPaste"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();
        capture!.Status.Should().Be(CaptureStatus.New);
        capture.Source.Should().Be(CaptureSource.TranscriptPaste);

        // Act 2: Trigger triage (marks the item Processing for the transcript worker lane)
        var triageResponse = await client.PostAsync($"/api/capture/items/{capture.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Act 3: Wait for the TranscriptTriageWorker to run the LLM leg and create the proposal
        var triaged = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.ProposalCreated);
        triaged.Status.Should().Be(CaptureStatus.ProposalCreated);
        triaged.Provenance.Should().NotBeNull();
        triaged.Provenance!.ProposalId.Should().NotBeNull();

        // Provenance names the REAL provider/model from the completion result, not the extractor.
        triaged.Provenance.Provider.Should().Be(StubProviderName);
        triaged.Provenance.Model.Should().Be(StubModelName);
        triaged.Provenance.PromptVersion.Should().Be("llm-triage.v1");
        var proposalId = triaged.Provenance.ProposalId!.Value;

        // The extractor sent the transcript text under the triage system prompt with capture attribution.
        providerStub.CompletionCallCount.Should().Be(1);
        var llmRequest = providerStub.LastRequest;
        llmRequest.Should().NotBeNull();
        llmRequest!.SystemPrompt.Should().Be(LlmCaptureTriagePrompt.SystemPrompt);
        llmRequest.Messages.Should().ContainSingle(message => message.Content == TranscriptText);
        llmRequest.Attribution.Should().NotBeNull();
        llmRequest.Attribution!.UserId.Should().Be(user.UserId);
        llmRequest.Attribution.SourceSurface.Should().Be(LlmRequestSourceSurface.Capture);

        // Act 4: Verify the proposal carries one create-card operation per stub task
        var proposalResponse = await client.GetAsync($"/api/automation/proposals/{proposalId}");
        proposalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await proposalResponse.Content.ReadFromJsonAsync<ProposalDto>();
        proposal.Should().NotBeNull();
        proposal!.Status.Should().Be(ProposalStatus.PendingReview);
        proposal.BoardId.Should().Be(board.Id);
        proposal.SourceType.Should().Be(ProposalSourceType.Queue);
        proposal.SourceReferenceId.Should().Be(capture.Id.ToString());
        proposal.Operations.Should().HaveCount(StubTasks.Length);
        proposal.Operations.Should().OnlyContain(op => op.ActionType == "create" && op.TargetType == "card");
        foreach (var (title, _) in StubTasks)
        {
            proposal.Operations.Should().Contain(op => op.Parameters.Contains(title));
        }

        // Act 5: Approve the proposal
        var approveResponse = await client.PostAsync($"/api/automation/proposals/{proposalId}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResponse.Content.ReadFromJsonAsync<ProposalDto>();
        approved!.Status.Should().Be(ProposalStatus.Approved);

        // Act 6: Execute the proposal
        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var executed = await executeResponse.Content.ReadFromJsonAsync<ProposalDto>();
        executed!.Status.Should().Be(ProposalStatus.Applied);
        executed.AppliedAt.Should().NotBeNull();

        // Assert: the stub's tasks landed as cards (evidence as card description)
        var cardsResponse = await client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
        cards.Should().NotBeNull();
        cards!.Should().HaveCount(StubTasks.Length);
        cards!.Select(c => c.Title).Should().BeEquivalentTo(StubTasks.Select(t => t.Title));
        cards!.Select(c => c.Description).Should().BeEquivalentTo(StubTasks.Select(t => t.Evidence));
        cards!.Should().OnlyContain(c => c.ColumnId == column.Id, "all cards should be placed in the Backlog column");

        // Assert: LLM provenance survives conversion intact
        var converted = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.Converted);
        converted.Status.Should().Be(CaptureStatus.Converted);
        converted.Provenance!.ProposalId.Should().Be(proposalId);
        converted.Provenance.ConvertedAt.Should().NotBeNull();
        converted.Provenance.Provider.Should().Be(StubProviderName);
        converted.Provenance.Model.Should().Be(StubModelName);
        converted.Provenance.PromptVersion.Should().Be("llm-triage.v1");

        // Exactly one LLM call for the whole pipeline (no retries, no re-extraction on conversion).
        providerStub.CompletionCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DeterministicFallback_DegradedProvider_ShouldTriageTranscriptWithDeterministicProvenance()
    {
        var providerStub = new DegradedTriageProviderStub();
        using var factory = CreateFactoryWithProviderStub(providerStub);
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "transcript-llm-degraded");
        var (board, _) = await CreateBoardWithBacklogColumnAsync(client, "transcript-llm-degraded-board");

        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, FallbackTranscriptText, "TranscriptPaste"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();

        var triageResponse = await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // The degraded LLM leg must fall back to the deterministic extractor, not fail the capture.
        var triaged = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.ProposalCreated);
        triaged.Status.Should().Be(CaptureStatus.ProposalCreated);
        triaged.Provenance.Should().NotBeNull();
        triaged.Provenance!.ProposalId.Should().NotBeNull();

        // #1273: the deterministic extractor produced the output, so provenance names it — never the
        // live provider whose completion was discarded as degraded.
        triaged.Provenance.Provider.Should().Be(CaptureTriageService.TriageProviderName);
        triaged.Provenance.Model.Should().Be(CaptureTriageService.TriageModelName);
        triaged.Provenance.PromptVersion.Should().Be("triage.v1");

        // The LLM leg was attempted exactly once before degrading.
        providerStub.CompletionCallCount.Should().Be(1);

        // Whole-text fallback on unstructured prose yields a single create-card operation.
        var proposalResponse = await client.GetAsync($"/api/automation/proposals/{triaged.Provenance.ProposalId!.Value}");
        proposalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await proposalResponse.Content.ReadFromJsonAsync<ProposalDto>();
        proposal.Should().NotBeNull();
        proposal!.Operations.Should().ContainSingle();
        proposal.Operations[0].ActionType.Should().Be("create");
        proposal.Operations[0].TargetType.Should().Be("card");
    }

    [Fact]
    public async Task TypedCapture_WithLiveProviderStub_ShouldNotInvokeLlmAndRecordDeterministicProvenance()
    {
        var providerStub = new TriageJsonProviderStub(StubTasksJson);
        using var factory = CreateFactoryWithProviderStub(providerStub);
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "transcript-llm-typed-control");
        var (board, _) = await CreateBoardWithBacklogColumnAsync(client, "transcript-llm-typed-board");

        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "- [ ] Review the standup notes", "Typed"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();
        capture!.Source.Should().Be(CaptureSource.Typed);

        var triageResponse = await client.PostAsync($"/api/capture/items/{capture.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triaged = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.ProposalCreated);
        triaged.Status.Should().Be(CaptureStatus.ProposalCreated);
        triaged.Provenance.Should().NotBeNull();

        // Non-transcript sources never take the LLM leg, even with a live provider configured.
        triaged.Provenance!.Provider.Should().Be(CaptureTriageService.TriageProviderName);
        triaged.Provenance.Model.Should().Be(CaptureTriageService.TriageModelName);
        triaged.Provenance.PromptVersion.Should().Be("triage.v1");
        providerStub.CompletionCallCount.Should().Be(0);
    }

    [Fact]
    public async Task TranscriptCapture_ShouldPersistTranscriptRequestType()
    {
        var providerStub = new TriageJsonProviderStub(StubTasksJson);
        using var factory = CreateFactoryWithProviderStub(providerStub);
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "transcript-request-type");
        var board = await ApiTestHarness.CreateBoardAsync(client, "transcript-request-type-board");

        var transcriptResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, TranscriptText, "TranscriptPaste"));
        transcriptResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var transcriptCapture = await transcriptResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        transcriptCapture.Should().NotBeNull();

        var typedResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "Follow up on the sprint retro", "Typed"));
        typedResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var typedCapture = await typedResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        typedCapture.Should().NotBeNull();

        // Transcript captures route to the transcript worker lane via their dedicated request type.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var persistedTranscript = await db.LlmRequests.SingleAsync(r => r.Id == transcriptCapture!.Id);
        persistedTranscript.RequestType.Should().Be(CaptureRequestContract.RequestTypeTranscriptV1);
        persistedTranscript.RequestType.Should().Be("inbox.capture.transcript.v1");

        var persistedTyped = await db.LlmRequests.SingleAsync(r => r.Id == typedCapture!.Id);
        persistedTyped.RequestType.Should().Be(CaptureRequestContract.RequestTypeV1);
    }

    [Fact]
    public async Task LongTranscript_ShouldMapReduceIntoOnePendingProposalWithoutDirectBoardMutation()
    {
        const string mapTaskTitle = "Publish the consolidated meeting notes";
        const string mapTaskEvidence = "Alice: I will publish the consolidated meeting notes.";
        var providerStub = new TriageJsonProviderStub(JsonSerializer.Serialize(new
        {
            tasks = new[] { new { title = mapTaskTitle, evidence = mapTaskEvidence } }
        }));
        var settings = new LlmCaptureTriageSettings
        {
            MaxInputTokensPerChunk = 10_000,
            ChunkOverlapTokens = 256
        };
        using var factory = CreateFactoryWithProviderStub(providerStub, settings);
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "transcript-llm-long-map-reduce");
        var (board, _) = await CreateBoardWithBacklogColumnAsync(client, "transcript-llm-long-map-reduce-board");
        var segment = mapTaskEvidence + " We also reviewed the release timeline and risks.\n\n";
        var repeatedSegments = string.Concat(Enumerable.Repeat(
            segment,
            (CaptureRequestContract.MaxTranscriptTextLength / segment.Length) - 1));
        var longTranscript = repeatedSegments.PadRight(CaptureRequestContract.MaxTranscriptTextLength, 'x');
        var expectedChunkCount = TranscriptTriageChunker.Chunk(
            longTranscript,
            settings.MaxInputTokensPerChunk,
            settings.ChunkOverlapTokens).Count;

        longTranscript.Length.Should().Be(CaptureRequestContract.MaxTranscriptTextLength);
        expectedChunkCount.Should().BeGreaterThan(1);

        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, longTranscript, "TranscriptPaste"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();

        var triageResponse = await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var triaged = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.ProposalCreated);

        triaged.Provenance!.ProposalId.Should().NotBeNull();
        providerStub.CompletionCallCount.Should().Be(expectedChunkCount);

        var proposalResponse = await client.GetAsync($"/api/automation/proposals/{triaged.Provenance.ProposalId}");
        proposalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await proposalResponse.Content.ReadFromJsonAsync<ProposalDto>();
        proposal.Should().NotBeNull();
        proposal!.Status.Should().Be(ProposalStatus.PendingReview);
        proposal.SourceReferenceId.Should().Be(capture.Id.ToString());
        proposal.Operations.Should().ContainSingle();
        proposal.Operations[0].Parameters.Should().Contain(mapTaskTitle);

        var cardsResponse = await client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>()).Should().BeEmpty(
            "map-reduce triage must remain proposal-first until the user explicitly approves and executes");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.AutomationProposals.CountAsync(proposal => proposal.SourceReferenceId == capture.Id.ToString()))
            .Should().Be(1, "all mapped chunks reduce into the capture's single idempotent proposal");
    }

    /// <summary>
    /// Derives a factory whose scoped <see cref="ILlmProvider"/> is the given non-mock stub,
    /// mirroring <see cref="ChatApiLiveProviderStubTests"/>. The LLM triage extractor gates on
    /// GetHealthAsync (IsMock/IsAvailable), so the stub reporting IsMock:false is what routes
    /// transcript captures onto the live LLM leg.
    /// </summary>
    private WebApplicationFactory<Program> CreateFactoryWithProviderStub(
        ILlmProvider providerStub,
        LlmCaptureTriageSettings? triageSettings = null)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILlmProvider>();
                services.AddScoped<ILlmProvider>(_ => providerStub);
                if (triageSettings is not null)
                {
                    services.RemoveAll<LlmCaptureTriageSettings>();
                    services.AddSingleton(triageSettings);
                }
            });
        });
    }

    private static async Task<(BoardDto Board, ColumnDto Column)> CreateBoardWithBacklogColumnAsync(
        HttpClient client,
        string stem)
    {
        var board = await ApiTestHarness.CreateBoardAsync(client, stem);

        var columnResponse = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        columnResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var column = await columnResponse.Content.ReadFromJsonAsync<ColumnDto>();
        column.Should().NotBeNull();

        return (board, column!);
    }

    private static async Task<CaptureItemDto> WaitForCaptureStatusAsync(
        HttpClient client,
        Guid itemId,
        CaptureStatus expectedStatus)
    {
        return await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var response = await client.GetAsync($"/api/capture/items/{itemId}");
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var item = await response.Content.ReadFromJsonAsync<CaptureItemDto>();
                item.Should().NotBeNull();
                return item!;
            },
            item => item.Status == expectedStatus ||
                    (item.Status == CaptureStatus.Failed && expectedStatus != CaptureStatus.Failed),
            $"capture item {itemId} status to become {expectedStatus}",
            maxAttempts: 40,
            interval: TimeSpan.FromMilliseconds(250),
            diagnostics: item => item is null
                ? "item=null"
                : $"status={item.Status}, proposalId={item.Provenance?.ProposalId?.ToString() ?? "null"}, error={item.ErrorMessage ?? "null"}");
    }

    /// <summary>
    /// Non-mock provider stub whose completion is a fixed triage-contract JSON payload
    /// ({"tasks":[{"title","evidence"}]}). GetHealthAsync reports IsMock:false / IsAvailable:true so
    /// <see cref="LlmCaptureTriageExtractor"/> treats it as a resolved live provider.
    /// </summary>
    private sealed class TriageJsonProviderStub : ILlmProvider
    {
        private readonly string _completionContent;
        private ChatCompletionRequest? _lastRequest;
        private int _completionCalls;

        public TriageJsonProviderStub(string completionContent)
        {
            _completionContent = completionContent;
        }

        public int CompletionCallCount => Volatile.Read(ref _completionCalls);

        public ChatCompletionRequest? LastRequest => Volatile.Read(ref _lastRequest);

        public Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _completionCalls);
            Volatile.Write(ref _lastRequest, request);
            return Task.FromResult(new LlmCompletionResult(
                Content: _completionContent,
                TokensUsed: 321,
                IsActionable: false,
                Provider: StubProviderName,
                Model: StubModelName));
        }

        public async IAsyncEnumerable<LlmTokenEvent> StreamAsync(ChatCompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            yield return new LlmTokenEvent(_completionContent, true);
            await Task.CompletedTask;
        }

        public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new LlmHealthStatus(
                IsAvailable: true,
                ProviderName: StubProviderName,
                Model: StubModelName,
                IsMock: false));
        }

        public Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new LlmHealthStatus(
                IsAvailable: true,
                ProviderName: StubProviderName,
                Model: StubModelName,
                IsMock: false,
                IsProbed: true));
        }
    }

    /// <summary>
    /// Non-mock provider stub that is healthy but returns a degraded completion, forcing the LLM
    /// extraction leg to fall back to the deterministic extractor.
    /// </summary>
    private sealed class DegradedTriageProviderStub : ILlmProvider
    {
        private int _completionCalls;

        public int CompletionCallCount => Volatile.Read(ref _completionCalls);

        public Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _completionCalls);
            return Task.FromResult(new LlmCompletionResult(
                Content: string.Empty,
                TokensUsed: 17,
                IsActionable: false,
                Provider: StubProviderName,
                Model: StubModelName,
                IsDegraded: true,
                DegradedReason: "Live provider request failed."));
        }

        public async IAsyncEnumerable<LlmTokenEvent> StreamAsync(ChatCompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            yield return new LlmTokenEvent("degraded", true);
            await Task.CompletedTask;
        }

        public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new LlmHealthStatus(
                IsAvailable: true,
                ProviderName: StubProviderName,
                Model: StubModelName,
                IsMock: false));
        }

        public Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new LlmHealthStatus(
                IsAvailable: true,
                ProviderName: StubProviderName,
                Model: StubModelName,
                IsMock: false,
                IsProbed: true));
        }
    }
}
