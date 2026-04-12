using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class NoteImportServiceTests
{
    private readonly Mock<ICaptureService> _captureServiceMock;
    private readonly NoteImportService _sut;

    public NoteImportServiceTests()
    {
        _captureServiceMock = new Mock<ICaptureService>();
        _sut = new NoteImportService(_captureServiceMock.Object);
    }

    private void SetupCaptureServiceReturnsSuccess()
    {
        var counter = 0;
        _captureServiceMock
            .Setup(s => s.CreateAsync(
                It.IsAny<Guid>(),
                It.IsAny<CreateCaptureItemDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                counter++;
                var itemId = Guid.NewGuid();
                return Result.Success(new CaptureItemDto(
                    itemId,
                    Guid.NewGuid(),
                    null,
                    CaptureStatus.New,
                    CaptureSource.MarkdownImport,
                    "raw text",
                    "excerpt",
                    DateTimeOffset.UtcNow,
                    null,
                    0));
            });
    }

    // --- Markdown import tests ---

    [Fact]
    public async Task ImportMarkdownAsync_ShouldFail_WhenUserIdIsEmpty()
    {
        var request = new MarkdownImportRequestDto("test.md", "# Hello");

        var result = await _sut.ImportMarkdownAsync(Guid.Empty, request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldFail_WhenRequestIsNull()
    {
        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), null!);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldFail_WhenFileNameIsEmpty()
    {
        var request = new MarkdownImportRequestDto("", "# Hello");

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("File name");
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldFail_WhenFileNameContainsPathTraversal()
    {
        var request = new MarkdownImportRequestDto("../../../etc/passwd", "# Hello");

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("invalid characters");
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldFail_WhenContentIsEmpty()
    {
        var request = new MarkdownImportRequestDto("notes.md", "");

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("content is required");
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldFail_WhenContentExceedsMaxLength()
    {
        var request = new MarkdownImportRequestDto(
            "notes.md",
            new string('x', NoteImportService.MaxMarkdownContentLength + 1));

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("cannot exceed");
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldCreateCaptureItems_ForEachSection()
    {
        SetupCaptureServiceReturnsSuccess();

        var content = "# Section One\nBody of section one\n\n# Section Two\nBody of section two";
        var request = new MarkdownImportRequestDto("notes.md", content);
        var userId = Guid.NewGuid();

        var result = await _sut.ImportMarkdownAsync(userId, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.ItemsCreated.Should().Be(2);
        result.Value.Items.Should().HaveCount(2);

        _captureServiceMock.Verify(
            s => s.CreateAsync(userId, It.IsAny<CreateCaptureItemDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldUseCaptureSourceMarkdownImport()
    {
        SetupCaptureServiceReturnsSuccess();

        var request = new MarkdownImportRequestDto("notes.md", "# Hello\nWorld");
        var userId = Guid.NewGuid();

        await _sut.ImportMarkdownAsync(userId, request);

        _captureServiceMock.Verify(
            s => s.CreateAsync(userId,
                It.Is<CreateCaptureItemDto>(dto => dto.Source == "MarkdownImport"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldPreserveSourceFileName_InExternalRef()
    {
        SetupCaptureServiceReturnsSuccess();

        var request = new MarkdownImportRequestDto("my-notes.md", "# Heading\nBody text");

        await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        _captureServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Guid>(),
                It.Is<CreateCaptureItemDto>(dto =>
                    dto.ExternalRef != null && dto.ExternalRef.Contains("my-notes.md")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldSetTitleHint_FromHeading()
    {
        SetupCaptureServiceReturnsSuccess();

        var request = new MarkdownImportRequestDto("notes.md", "# My Important Note\nContent here");

        await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        _captureServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Guid>(),
                It.Is<CreateCaptureItemDto>(dto => dto.TitleHint == "My Important Note"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldHandlePlainTextWithoutHeadings()
    {
        SetupCaptureServiceReturnsSuccess();

        var request = new MarkdownImportRequestDto("notes.md", "Just some plain text content\nWith multiple lines");

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeTrue();
        result.Value.ItemsCreated.Should().Be(1);
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldPassBoardId_WhenProvided()
    {
        SetupCaptureServiceReturnsSuccess();

        var boardId = Guid.NewGuid();
        var request = new MarkdownImportRequestDto("notes.md", "# Hello\nWorld", boardId);

        await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        _captureServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Guid>(),
                It.Is<CreateCaptureItemDto>(dto => dto.BoardId == boardId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldReturnItemSourceType_AsMarkdown()
    {
        SetupCaptureServiceReturnsSuccess();

        var request = new MarkdownImportRequestDto("notes.md", "# Hello\nWorld");

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items[0].SourceType.Should().Be("markdown");
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldFail_WhenAllSectionsFail()
    {
        _captureServiceMock
            .Setup(s => s.CreateAsync(
                It.IsAny<Guid>(),
                It.IsAny<CreateCaptureItemDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CaptureItemDto>(ErrorCodes.Forbidden, "You do not have access to this board"));

        var content = "# Section One\nBody of section one\n\n# Section Two\nBody of section two";
        var request = new MarkdownImportRequestDto("notes.md", content);

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("failed to import");
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldReturnPartialSuccess_WhenSomeSectionsFail()
    {
        var callCount = 0;
        _captureServiceMock
            .Setup(s => s.CreateAsync(
                It.IsAny<Guid>(),
                It.IsAny<CreateCaptureItemDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 2)
                {
                    return Result.Failure<CaptureItemDto>(ErrorCodes.Forbidden, "Access denied");
                }
                return Result.Success(new CaptureItemDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    null,
                    CaptureStatus.New,
                    CaptureSource.MarkdownImport,
                    "raw text",
                    "excerpt",
                    DateTimeOffset.UtcNow,
                    null,
                    0));
            });

        var content = "# Section One\nBody of section one\n\n# Section Two\nBody of section two\n\n# Section Three\nBody three";
        var request = new MarkdownImportRequestDto("notes.md", content);

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeTrue();
        result.Value.ItemsCreated.Should().Be(2);
        result.Value.Errors.Should().NotBeNull();
        result.Value.Errors.Should().HaveCount(1);
        result.Value.Errors![0].SectionIndex.Should().Be(1);
        result.Value.Errors![0].Heading.Should().Be("Section Two");
        result.Value.Errors![0].ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldReturnWarning_WhenSectionsTruncated()
    {
        SetupCaptureServiceReturnsSuccess();

        // Create content with more than MaxSectionsPerFile sections
        var sections = Enumerable.Range(1, NoteImportService.MaxSectionsPerFile + 5)
            .Select(i => $"# Section {i}\nBody {i}")
            .ToList();
        var content = string.Join("\n\n", sections);

        var request = new MarkdownImportRequestDto("notes.md", content);

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeTrue();
        result.Value.ItemsCreated.Should().Be(NoteImportService.MaxSectionsPerFile);
        result.Value.Warnings.Should().NotBeNull();
        result.Value.Warnings.Should().HaveCount(1);
        result.Value.Warnings![0].Should().Contain("5 section(s) were skipped");
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldReturnTruncatedExternalRef_InResponseItems()
    {
        SetupCaptureServiceReturnsSuccess();

        // Use a heading long enough that after md:// prefix the ref would be very long
        var request = new MarkdownImportRequestDto("notes.md", "# Short Heading\nBody text");

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeTrue();
        // The sourceRef in the response should not exceed MaxExternalRefLength
        foreach (var item in result.Value.Items)
        {
            item.SourceRef.Should().NotBeNull();
            item.SourceRef!.Length.Should().BeLessOrEqualTo(CaptureRequestContract.MaxExternalRefLength);
        }
    }

    // --- Web clip import tests ---

    [Fact]
    public async Task ImportWebClipAsync_ShouldFail_WhenUserIdIsEmpty()
    {
        var request = new WebClipImportRequestDto("https://example.com", "content");

        var result = await _sut.ImportWebClipAsync(Guid.Empty, request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldFail_WhenRequestIsNull()
    {
        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), null!);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldFail_WhenUrlIsEmpty()
    {
        var request = new WebClipImportRequestDto("", "content");

        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("URL is required");
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldFail_WhenUrlIsNotHttpOrHttps()
    {
        var request = new WebClipImportRequestDto("ftp://evil.com/file", "content");

        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("valid HTTP or HTTPS URL");
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldFail_WhenUrlIsInvalid()
    {
        var request = new WebClipImportRequestDto("not a url", "content");

        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldFail_WhenContentIsEmpty()
    {
        var request = new WebClipImportRequestDto("https://example.com", "");

        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("content is required");
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldFail_WhenContentExceedsMaxLength()
    {
        var request = new WebClipImportRequestDto(
            "https://example.com",
            new string('x', NoteImportService.MaxWebClipContentLength + 1));

        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("cannot exceed");
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldCreateSingleCaptureItem()
    {
        SetupCaptureServiceReturnsSuccess();

        var request = new WebClipImportRequestDto(
            "https://example.com/article",
            "Important content from article");
        var userId = Guid.NewGuid();

        var result = await _sut.ImportWebClipAsync(userId, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.ItemsCreated.Should().Be(1);
        result.Value.Items.Should().HaveCount(1);

        _captureServiceMock.Verify(
            s => s.CreateAsync(userId, It.IsAny<CreateCaptureItemDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldUseCaptureSourceWebClip()
    {
        SetupCaptureServiceReturnsSuccess();

        var request = new WebClipImportRequestDto(
            "https://example.com",
            "content");

        await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        _captureServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Guid>(),
                It.Is<CreateCaptureItemDto>(dto => dto.Source == "WebClip"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldPreserveUrl_InExternalRef()
    {
        SetupCaptureServiceReturnsSuccess();

        var request = new WebClipImportRequestDto(
            "https://example.com/important",
            "content");

        await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        _captureServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Guid>(),
                It.Is<CreateCaptureItemDto>(dto =>
                    dto.ExternalRef == "https://example.com/important"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldIncludeUrlInCaptureText()
    {
        SetupCaptureServiceReturnsSuccess();

        var request = new WebClipImportRequestDto(
            "https://example.com/article",
            "Content from the article");

        await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        _captureServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Guid>(),
                It.Is<CreateCaptureItemDto>(dto =>
                    dto.Text.Contains("https://example.com/article") &&
                    dto.Text.Contains("Content from the article")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldSetTitleHint_WhenProvided()
    {
        SetupCaptureServiceReturnsSuccess();

        var request = new WebClipImportRequestDto(
            "https://example.com",
            "content",
            "Article Title");

        await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        _captureServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Guid>(),
                It.Is<CreateCaptureItemDto>(dto => dto.TitleHint == "Article Title"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldPassBoardId_WhenProvided()
    {
        SetupCaptureServiceReturnsSuccess();

        var boardId = Guid.NewGuid();
        var request = new WebClipImportRequestDto(
            "https://example.com",
            "content",
            null,
            boardId);

        await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        _captureServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Guid>(),
                It.Is<CreateCaptureItemDto>(dto => dto.BoardId == boardId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldReturnItemSourceType_AsWebClip()
    {
        SetupCaptureServiceReturnsSuccess();

        var request = new WebClipImportRequestDto(
            "https://example.com",
            "content");

        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items[0].SourceType.Should().Be("webclip");
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldFail_WhenUrlExceedsMaxLength()
    {
        var request = new WebClipImportRequestDto(
            "https://example.com/" + new string('a', NoteImportService.MaxUrlLength),
            "content");

        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldFail_WhenTitleExceedsMaxLength()
    {
        var request = new WebClipImportRequestDto(
            "https://example.com",
            "content",
            new string('t', NoteImportService.MaxTitleLength + 1));

        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    // --- Markdown section splitting tests ---

    [Fact]
    public void SplitMarkdownIntoSections_ShouldHandleSingleSection()
    {
        var sections = NoteImportService.SplitMarkdownIntoSections("# Title\nBody text");

        sections.Should().HaveCount(1);
        sections[0].Heading.Should().Be("Title");
        sections[0].Body.Should().Be("Body text");
    }

    [Fact]
    public void SplitMarkdownIntoSections_ShouldHandleMultipleSections()
    {
        var sections = NoteImportService.SplitMarkdownIntoSections(
            "# First\nBody one\n\n# Second\nBody two\n\n## Subsection\nBody three");

        sections.Should().HaveCount(3);
        sections[0].Heading.Should().Be("First");
        sections[1].Heading.Should().Be("Second");
        sections[2].Heading.Should().Be("Subsection");
    }

    [Fact]
    public void SplitMarkdownIntoSections_ShouldHandleContentBeforeFirstHeading()
    {
        var sections = NoteImportService.SplitMarkdownIntoSections(
            "Preamble text\n\n# First Heading\nBody");

        sections.Should().HaveCount(2);
        sections[0].Heading.Should().BeNull();
        sections[0].Body.Should().Be("Preamble text");
        sections[1].Heading.Should().Be("First Heading");
    }

    [Fact]
    public void SplitMarkdownIntoSections_ShouldHandlePlainTextWithNoHeadings()
    {
        var sections = NoteImportService.SplitMarkdownIntoSections(
            "Just some plain text\nWith multiple lines");

        sections.Should().HaveCount(1);
        sections[0].Heading.Should().BeNull();
        sections[0].Body.Should().Contain("Just some plain text");
    }

    [Fact]
    public void SplitMarkdownIntoSections_ShouldHandleEmptyBodyAfterHeading()
    {
        var sections = NoteImportService.SplitMarkdownIntoSections("# Empty Section");

        sections.Should().HaveCount(1);
        sections[0].Heading.Should().Be("Empty Section");
        sections[0].Body.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldFail_WhenFileNameContainsBackslash()
    {
        var request = new MarkdownImportRequestDto("..\\..\\secret.md", "# Hello");

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ImportMarkdownAsync_ShouldFail_WhenFileNameContainsForwardSlash()
    {
        var request = new MarkdownImportRequestDto("path/to/file.md", "# Hello");

        var result = await _sut.ImportMarkdownAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldRejectJavascriptUrl()
    {
        var request = new WebClipImportRequestDto(
            "javascript:alert(1)",
            "content");

        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldRejectDataUrl()
    {
        var request = new WebClipImportRequestDto(
            "data:text/html,<script>alert(1)</script>",
            "content");

        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ImportWebClipAsync_ShouldReturnTruncatedRef_WhenUrlExceedsLimit()
    {
        SetupCaptureServiceReturnsSuccess();

        // Create a URL that is longer than MaxExternalRefLength
        var longPath = new string('a', CaptureRequestContract.MaxExternalRefLength);
        var longUrl = $"https://example.com/{longPath}";
        var request = new WebClipImportRequestDto(longUrl[..NoteImportService.MaxUrlLength], "content");

        var result = await _sut.ImportWebClipAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items[0].SourceRef.Should().NotBeNull();
        result.Value.Items[0].SourceRef!.Length.Should().BeLessOrEqualTo(CaptureRequestContract.MaxExternalRefLength);
    }
}
