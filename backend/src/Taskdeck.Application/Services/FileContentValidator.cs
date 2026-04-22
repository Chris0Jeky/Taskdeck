using System.Text;
using System.Text.Json;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Validates file content for import operations. Ensures text-based imports
/// contain valid UTF-8 text (no disguised binary content), validates JSON
/// structure for JSON imports, and enforces size limits.
/// </summary>
public static class FileContentValidator
{
    /// <summary>
    /// Maximum allowed content size for text imports (1 MB).
    /// Individual endpoints may enforce tighter limits via the maxBytes parameter.
    /// </summary>
    public const int DefaultMaxTextContentBytes = 1_048_576;

    /// <summary>
    /// Maximum allowed content size for JSON imports (2 MB).
    /// </summary>
    public const int DefaultMaxJsonContentBytes = 2_097_152;

    /// <summary>Maximum markdown content size in bytes (100 KB), matching NoteImportService.</summary>
    public const int MaxMarkdownContentBytes = 102_400;

    /// <summary>Maximum web clip content size in bytes (20 KB), matching NoteImportService.</summary>
    public const int MaxWebClipContentBytes = 20_000;

    /// <summary>Maximum CSV payload size in bytes (1 MB), matching CsvExternalImportAdapter.</summary>
    public const int MaxCsvPayloadBytes = 1_048_576;

    /// <summary>
    /// Validates that string content is safe text (no binary content disguised as text).
    /// Rejects content containing null bytes or non-whitespace control characters,
    /// which indicate binary data being passed as text.
    /// </summary>
    /// <param name="content">The string content to validate.</param>
    /// <param name="contentLabel">Human-readable label for error messages (e.g. "Markdown content").</param>
    /// <param name="maxBytes">Maximum allowed size in UTF-8 bytes. Use 0 to skip size check.</param>
    /// <returns>Success or failure with a descriptive error message.</returns>
    public static Result ValidateTextContent(string? content, string contentLabel, int maxBytes = DefaultMaxTextContentBytes)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Result.Failure(ErrorCodes.ValidationError, $"{contentLabel} is required.");
        }

        if (maxBytes > 0)
        {
            var byteCount = Encoding.UTF8.GetByteCount(content);
            if (byteCount > maxBytes)
            {
                return Result.Failure(
                    ErrorCodes.ValidationError,
                    $"{contentLabel} exceeds maximum allowed size of {maxBytes} bytes.");
            }
        }

        if (ContainsBinaryContent(content))
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"{contentLabel} contains binary data. Only valid text content is accepted.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Validates that string content is valid JSON and safe text.
    /// Checks for binary content, validates JSON structure (must start
    /// with { or [ after optional BOM/whitespace), and attempts a parse.
    /// </summary>
    /// <param name="json">The JSON string to validate.</param>
    /// <param name="contentLabel">Human-readable label for error messages.</param>
    /// <param name="maxBytes">Maximum allowed size in UTF-8 bytes. Use 0 to skip size check.</param>
    /// <returns>Success or failure with a descriptive error message.</returns>
    public static Result ValidateJsonContent(string? json, string contentLabel, int maxBytes = DefaultMaxJsonContentBytes)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Result.Failure(ErrorCodes.ValidationError, $"{contentLabel} is required.");
        }

        if (maxBytes > 0)
        {
            var byteCount = Encoding.UTF8.GetByteCount(json);
            if (byteCount > maxBytes)
            {
                return Result.Failure(
                    ErrorCodes.ValidationError,
                    $"{contentLabel} exceeds maximum allowed size of {maxBytes} bytes.");
            }
        }

        if (ContainsBinaryContent(json))
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"{contentLabel} contains binary data. Only valid JSON text is accepted.");
        }

        // Verify JSON structure: after stripping optional BOM and whitespace,
        // content must start with { or [
        var trimmed = StripBomAndWhitespace(json);
        if (trimmed.Length == 0)
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"{contentLabel} is empty after removing whitespace.");
        }

        var firstChar = trimmed[0];
        if (firstChar != '{' && firstChar != '[')
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"{contentLabel} does not contain valid JSON. Expected content starting with '{{' or '['.");
        }

        // Attempt a full parse to catch malformed JSON.
        // Use the BOM-stripped content since JsonDocument.Parse does not always
        // handle the BOM marker gracefully.
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"{contentLabel} contains malformed JSON that could not be parsed.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Validates that a byte array has the expected SQLite magic bytes.
    /// This is a convenience wrapper — the existing DatabaseFileExportImportService
    /// already performs this check, but this provides a consistent API.
    /// </summary>
    /// <param name="data">The byte array to validate.</param>
    /// <param name="maxBytes">Maximum allowed size in bytes.</param>
    /// <returns>Success or failure with a descriptive error message.</returns>
    public static Result ValidateSqliteContent(byte[]? data, int maxBytes)
    {
        if (data == null || data.Length == 0)
        {
            return Result.Failure(ErrorCodes.ValidationError, "Database file content is required.");
        }

        if (data.Length > maxBytes)
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"Database file exceeds maximum allowed size of {maxBytes} bytes.");
        }

        // SQLite files start with "SQLite format 3\0" (16 bytes)
        ReadOnlySpan<byte> sqliteHeader = "SQLite format 3\0"u8;
        if (data.Length < sqliteHeader.Length || !data.AsSpan(0, sqliteHeader.Length).SequenceEqual(sqliteHeader))
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                "File content does not match expected database format. Expected a valid SQLite file.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Detects binary content in a string by scanning for null bytes and
    /// non-whitespace control characters. Common text control characters
    /// (tab, newline, carriage return) are permitted.
    /// </summary>
    internal static bool ContainsBinaryContent(string content)
    {
        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];

            // Null byte is the strongest binary indicator
            if (ch == '\0')
                return true;

            // Allow standard text whitespace/control: tab, newline, carriage return
            if (ch == '\t' || ch == '\n' || ch == '\r')
                continue;

            // C0 control characters (0x01-0x1F excluding tab/LF/CR) indicate binary
            if (ch < 0x20)
                return true;

            // DEL character (0x7F) indicates binary
            if (ch == 0x7F)
                return true;

            // C1 control characters (0x80-0x9F) — these are control codes in ISO-8859-1.
            // In valid UTF-8, these code points appear only in multibyte sequences.
            // When they appear as literal chars in a C# string (which is UTF-16),
            // they indicate content that is not standard text.
            if (ch >= 0x80 && ch <= 0x9F)
            {
                // Exception: some of these are used in Windows-1252 text.
                // Allow commonly seen Windows-1252 characters that map to C1 range:
                // 0x85 (NEL/ellipsis), 0x91-0x94 (smart quotes), 0x96-0x97 (dashes)
                if (ch == 0x85 || (ch >= 0x91 && ch <= 0x94) || ch == 0x96 || ch == 0x97)
                    continue;

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Strips the UTF-8 BOM marker and leading whitespace from content.
    /// </summary>
    internal static string StripBomAndWhitespace(string content)
    {
        var span = content.AsSpan();

        // Strip UTF-8 BOM (U+FEFF) if present
        if (span.Length > 0 && span[0] == '﻿')
        {
            span = span[1..];
        }

        // Trim leading whitespace
        return span.TrimStart().ToString();
    }
}
