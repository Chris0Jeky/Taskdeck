using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class KnowledgeDocument : Entity
{
    private const int MaxTitleLength = 200;
    private const int MaxContentLength = 50000;
    private const int MaxTagsLength = 2000;
    private const int MaxSourceUrlLength = 2000;

    public Guid UserId { get; private set; }
    public Guid? BoardId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public KnowledgeSourceType SourceType { get; private set; }
    public string? SourceUrl { get; private set; }
    public string? Tags { get; private set; }
    public bool IsArchived { get; private set; }

    private KnowledgeDocument() : base()
    {
    }

    public KnowledgeDocument(
        Guid userId,
        string title,
        string content,
        KnowledgeSourceType sourceType,
        Guid? boardId = null,
        string? sourceUrl = null,
        string? tags = null)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        ValidateTitle(title);
        ValidateContent(content);

        if (sourceUrl is not null && sourceUrl.Length > MaxSourceUrlLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Source URL cannot exceed {MaxSourceUrlLength} characters");

        if (tags is not null && tags.Length > MaxTagsLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Tags cannot exceed {MaxTagsLength} characters");

        UserId = userId;
        BoardId = boardId;
        Title = title;
        Content = content;
        SourceType = sourceType;
        SourceUrl = sourceUrl;
        Tags = tags;
        IsArchived = false;
    }

    public void Update(string title, string content, string? tags = null)
    {
        if (IsArchived)
            throw new DomainException(ErrorCodes.ValidationError, "Archived documents cannot be edited");

        ValidateTitle(title);
        ValidateContent(content);

        if (tags is not null && tags.Length > MaxTagsLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Tags cannot exceed {MaxTagsLength} characters");

        Title = title;
        Content = content;
        Tags = tags;
        Touch();
    }

    public void Archive()
    {
        if (IsArchived)
            return;

        IsArchived = true;
        Touch();
    }

    public void Unarchive()
    {
        if (!IsArchived)
            return;

        IsArchived = false;
        Touch();
    }

    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException(ErrorCodes.ValidationError, "Title cannot be empty");

        if (title.Length > MaxTitleLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Title cannot exceed {MaxTitleLength} characters");
    }

    private static void ValidateContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new DomainException(ErrorCodes.ValidationError, "Content cannot be empty");

        if (content.Length > MaxContentLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Content cannot exceed {MaxContentLength} characters");
    }
}
