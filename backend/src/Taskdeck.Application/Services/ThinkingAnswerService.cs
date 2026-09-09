using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class ThinkingAnswerService(IUnitOfWork unitOfWork, IThinkingDeckRepository decks,
    IWorkspaceInsightRepository memories, IAuthorizationService authorization)
{
    public async Task<Result<WorkspaceMemoryDto?>> GetAsync(Guid userId, Guid boardId, Guid cardId, Guid layerId, CancellationToken ct)
    {
        var source = await SourceAsync(userId, boardId, cardId, layerId, ct);
        if (!source.IsSuccess) return Result.Failure<WorkspaceMemoryDto?>(source.ErrorCode, source.ErrorMessage);
        var (_, layer) = source.Value;
        var memory = await memories.ThinkingAnswerAsync(userId, cardId, layerId, Hash(layer), ct);
        return Result.Success(memory is null ? null : WorkspaceInsightService.MapMemory(memory));
    }

    public async Task<Result<WorkspaceMemoryDto>> AnswerAsync(Guid userId, Guid boardId, Guid cardId, Guid layerId, ThinkingAnswerDto dto, CancellationToken ct)
    {
        var source = await SourceAsync(userId, boardId, cardId, layerId, ct);
        if (!source.IsSuccess) return Result.Failure<WorkspaceMemoryDto>(source.ErrorCode, source.ErrorMessage);
        var (deck, layer) = source.Value;
        if (deck.Revision != dto.ExpectedRevision) return Conflict();
        var hash = Hash(layer);
        var prior = await memories.ThinkingAnswerAsync(userId, cardId, layerId, hash, ct);
        if (prior is not null)
            return prior.Text == dto.Text && prior.Status == dto.Status && !prior.Archived
                ? Result.Success(WorkspaceInsightService.MapMemory(prior)) : Conflict();
        try
        {
            var evidence = $"Thinking question: {layer.Title}\n{layer.Body}";
            var memory = new WorkspaceMemory(userId, boardId, string.IsNullOrWhiteSpace(layer.Title) ? "Thinking question" : layer.Title,
                dto.Text, dto.Status, originalEvidence: evidence);
            memory.AttachThinkingSource(cardId, layerId, deck.Revision, hash);
            memories.Add(memory);
            decks.GuardRevision(deck);
            return await memories.SaveAsync(ct) ? Result.Success(WorkspaceInsightService.MapMemory(memory)) : Conflict();
        }
        catch (DomainException ex) { return Result.Failure<WorkspaceMemoryDto>(ex.ErrorCode, ex.Message); }
    }

    private async Task<Result<(ThinkingDeck Deck, ThinkingLayer Layer)>> SourceAsync(Guid userId, Guid boardId, Guid cardId, Guid layerId, CancellationToken ct)
    {
        var permission = await authorization.CanReadBoardAsync(userId, boardId);
        if (!permission.IsSuccess || !permission.Value)
            return Missing();
        var board = await unitOfWork.Boards.GetByIdAsync(boardId, ct);
        var card = await unitOfWork.Cards.GetByIdAsync(cardId, ct);
        if (board is null || board.IsArchived || card?.BoardId != boardId) return Missing();
        var deck = await decks.GetAsync(cardId, ct);
        var layer = deck?.ReadLayers().SingleOrDefault(x => x.Id == layerId && x.Kind == "question");
        return deck is null || layer is null ? Missing() : Result.Success((deck, layer));
    }
    private static string Hash(ThinkingLayer layer) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { layer.Title, layer.Body }))));
    private static Result<(ThinkingDeck, ThinkingLayer)> Missing() => Result.Failure<(ThinkingDeck, ThinkingLayer)>(ErrorCodes.NotFound, "This saved question is unavailable.");
    private static Result<WorkspaceMemoryDto> Conflict() => Result.Failure<WorkspaceMemoryDto>(ErrorCodes.Conflict,
        "The question or your private answer changed. Keep your draft and reload. Correct saved answers in private memory.");
}
