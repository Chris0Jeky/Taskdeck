using System.Text.RegularExpressions;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Handles note-style import (markdown files, web clips) by creating
/// capture items through the standard capture pipeline. Imported content
/// never bypasses review — all items enter capture → triage → proposal flow.
/// </summary>
public sealed class NoteImportService : INoteImportService
{
    /// <summary>Maximum markdown content length (100 KB).</summary>
    internal const int MaxMarkdownContentLength = 102_400;

    /// <summary>Maximum web clip content length (20 KB).</summary>
    internal const int MaxWebClipContentLength = 20_000;

    /// <summary>Maximum filename length.</summary>
    internal const int MaxFileNameLength = 255;

    /// <summary>Maximum URL length.</summary>
    internal const int MaxUrlLength = 2_048;

    /// <summary>Maximum title hint length (from CaptureRequestContract).</summary>
    internal const int MaxTitleLength = 240;

    /// <summary>Maximum number of sections extracted from a single markdown file.</summary>
    internal const int MaxSectionsPerFile = 50;

    private static readonly Regex HeadingPattern = new(
        @"^(#{1,6})\s+(.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly ICaptureService _captureService;

    public NoteImportService(ICaptureService captureService)
    {
        _captureService = captureService;
    }

    public async Task<Result<NoteImportResultDto>> ImportMarkdownAsync(
        Guid userId,
        MarkdownImportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<NoteImportResultDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (request == null)
            return Result.Failure<NoteImportResultDto>(ErrorCodes.ValidationError, "Request body is required");

        if (string.IsNullOrWhiteSpace(request.FileName))
            return Result.Failure<NoteImportResultDto>(ErrorCodes.ValidationError, "File name is required");

        if (request.FileName.Length > MaxFileNameLength)
            return Result.Failure<NoteImportResultDto>(
                ErrorCodes.ValidationError,
                $"File name cannot exceed {MaxFileNameLength} characters");

        if (!IsValidFileName(request.FileName))
            return Result.Failure<NoteImportResultDto>(
                ErrorCodes.ValidationError,
                "File name contains invalid characters");

        if (string.IsNullOrWhiteSpace(request.Content))
            return Result.Failure<NoteImportResultDto>(ErrorCodes.ValidationError, "Markdown content is required");

        if (request.Content.Length > MaxMarkdownContentLength)
            return Result.Failure<NoteImportResultDto>(
                ErrorCodes.ValidationError,
                $"Markdown content cannot exceed {MaxMarkdownContentLength} characters");

        var sections = SplitMarkdownIntoSections(request.Content);
        if (sections.Count == 0)
            return Result.Failure<NoteImportResultDto>(ErrorCodes.ValidationError, "No content sections found in markdown");

        if (sections.Count > MaxSectionsPerFile)
        {
            sections = sections.Take(MaxSectionsPerFile).ToList();
        }

        var items = new List<NoteImportItemResultDto>();
        var sectionsAttempted = 0;
        string? lastErrorCode = null;
        string? lastErrorMessage = null;

        foreach (var section in sections)
        {
            var externalRef = $"md://{SanitizeForExternalRef(request.FileName)}";
            if (!string.IsNullOrWhiteSpace(section.Heading))
            {
                externalRef += $"#{SanitizeForExternalRef(section.Heading)}";
            }

            var captureText = BuildCaptureText(section);
            if (string.IsNullOrWhiteSpace(captureText))
                continue;

            sectionsAttempted++;

            // Truncate to CaptureRequestContract max if needed
            if (captureText.Length > CaptureRequestContract.MaxRawTextLength)
            {
                captureText = captureText[..CaptureRequestContract.MaxRawTextLength];
            }

            var titleHint = section.Heading;
            if (titleHint != null && titleHint.Length > MaxTitleLength)
            {
                titleHint = titleHint[..MaxTitleLength];
            }

            var truncatedRef = TruncateExternalRef(externalRef);

            var dto = new CreateCaptureItemDto(
                request.BoardId,
                captureText,
                Source: CaptureSource.MarkdownImport.ToString(),
                TitleHint: titleHint,
                ExternalRef: truncatedRef);

            var result = await _captureService.CreateAsync(userId, dto, cancellationToken);
            if (!result.IsSuccess)
            {
                lastErrorCode = result.ErrorCode;
                lastErrorMessage = result.ErrorMessage;
                continue;
            }

            items.Add(new NoteImportItemResultDto(
                result.Value.Id,
                BuildExcerpt(captureText, 200),
                "markdown",
                truncatedRef));
        }

        if (items.Count == 0 && sectionsAttempted > 0)
        {
            return Result.Failure<NoteImportResultDto>(
                lastErrorCode ?? ErrorCodes.UnexpectedError,
                $"All {sectionsAttempted} section(s) failed to import. Last error: {lastErrorMessage ?? "unknown"}");
        }

        return Result.Success(new NoteImportResultDto(items.Count, items));
    }

    public async Task<Result<NoteImportResultDto>> ImportWebClipAsync(
        Guid userId,
        WebClipImportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<NoteImportResultDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (request == null)
            return Result.Failure<NoteImportResultDto>(ErrorCodes.ValidationError, "Request body is required");

        if (string.IsNullOrWhiteSpace(request.Url))
            return Result.Failure<NoteImportResultDto>(ErrorCodes.ValidationError, "URL is required");

        if (request.Url.Length > MaxUrlLength)
            return Result.Failure<NoteImportResultDto>(
                ErrorCodes.ValidationError,
                $"URL cannot exceed {MaxUrlLength} characters");

        if (!IsValidUrl(request.Url))
            return Result.Failure<NoteImportResultDto>(
                ErrorCodes.ValidationError,
                "URL must be a valid HTTP or HTTPS URL");

        if (string.IsNullOrWhiteSpace(request.Content))
            return Result.Failure<NoteImportResultDto>(ErrorCodes.ValidationError, "Clip content is required");

        if (request.Content.Length > MaxWebClipContentLength)
            return Result.Failure<NoteImportResultDto>(
                ErrorCodes.ValidationError,
                $"Clip content cannot exceed {MaxWebClipContentLength} characters");

        if (request.Title != null && request.Title.Length > MaxTitleLength)
            return Result.Failure<NoteImportResultDto>(
                ErrorCodes.ValidationError,
                $"Title cannot exceed {MaxTitleLength} characters");

        var captureText = $"[Web Clip] {request.Url}\n\n{request.Content}";
        if (captureText.Length > CaptureRequestContract.MaxRawTextLength)
        {
            captureText = captureText[..CaptureRequestContract.MaxRawTextLength];
        }

        var externalRef = TruncateExternalRef(request.Url);

        var dto = new CreateCaptureItemDto(
            request.BoardId,
            captureText,
            Source: CaptureSource.WebClip.ToString(),
            TitleHint: request.Title,
            ExternalRef: externalRef);

        var result = await _captureService.CreateAsync(userId, dto, cancellationToken);
        if (!result.IsSuccess)
            return Result.Failure<NoteImportResultDto>(result.ErrorCode, result.ErrorMessage);

        var item = new NoteImportItemResultDto(
            result.Value.Id,
            BuildExcerpt(captureText, 200),
            "webclip",
            request.Url);

        return Result.Success(new NoteImportResultDto(1, new List<NoteImportItemResultDto> { item }));
    }

    internal static List<MarkdownSection> SplitMarkdownIntoSections(string content)
    {
        var sections = new List<MarkdownSection>();
        var lines = content.Split('\n');
        string? currentHeading = null;
        var currentBody = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var match = HeadingPattern.Match(line);
            if (match.Success)
            {
                // Flush previous section
                if (currentHeading != null || currentBody.Count > 0)
                {
                    var bodyText = string.Join("\n", currentBody).Trim();
                    if (!string.IsNullOrWhiteSpace(bodyText) || currentHeading != null)
                    {
                        sections.Add(new MarkdownSection(currentHeading, bodyText));
                    }
                }

                currentHeading = match.Groups[2].Value.Trim();
                currentBody.Clear();
            }
            else
            {
                currentBody.Add(line);
            }
        }

        // Flush final section
        if (currentHeading != null || currentBody.Count > 0)
        {
            var bodyText = string.Join("\n", currentBody).Trim();
            if (!string.IsNullOrWhiteSpace(bodyText) || currentHeading != null)
            {
                sections.Add(new MarkdownSection(currentHeading, bodyText));
            }
        }

        return sections;
    }

    private static string BuildCaptureText(MarkdownSection section)
    {
        if (string.IsNullOrWhiteSpace(section.Heading))
            return section.Body;

        if (string.IsNullOrWhiteSpace(section.Body))
            return section.Heading;

        return $"{section.Heading}\n\n{section.Body}";
    }

    private static bool IsValidFileName(string fileName)
    {
        // Reject path traversal and dangerous characters
        if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return false;

        var invalidChars = Path.GetInvalidFileNameChars();
        return !fileName.Any(c => invalidChars.Contains(c));
    }

    private static bool IsValidUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static string SanitizeForExternalRef(string value)
    {
        // Replace whitespace with dashes, strip control chars
        var sanitized = Regex.Replace(value, @"[\s]+", "-");
        sanitized = Regex.Replace(sanitized, @"[^\w\-\.\(\)]", "");
        return sanitized;
    }

    private static string TruncateExternalRef(string value)
    {
        return value.Length <= CaptureRequestContract.MaxExternalRefLength
            ? value
            : value[..CaptureRequestContract.MaxExternalRefLength];
    }

    private static string BuildExcerpt(string text, int maxLength)
    {
        var normalized = string.Join(
            " ",
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    internal sealed record MarkdownSection(string? Heading, string Body);
}
