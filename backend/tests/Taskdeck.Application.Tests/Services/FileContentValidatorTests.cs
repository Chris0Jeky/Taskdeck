using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class FileContentValidatorTests
{
    // =====================================================================
    // ValidateTextContent tests
    // =====================================================================

    [Fact]
    public void ValidateTextContent_ValidMarkdown_ReturnsSuccess()
    {
        var content = "# Hello World\n\nThis is a **markdown** document.\n\n- Item 1\n- Item 2";

        var result = FileContentValidator.ValidateTextContent(content, "Markdown content");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateTextContent_ValidCsvPayload_ReturnsSuccess()
    {
        var content = "Name,Email,Company\nAlice,alice@example.com,Acme\nBob,bob@example.com,Widget Corp";

        var result = FileContentValidator.ValidateTextContent(content, "CSV payload");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateTextContent_NullContent_ReturnsFailure()
    {
        var result = FileContentValidator.ValidateTextContent(null, "Test content");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Test content");
        result.ErrorMessage.Should().Contain("required");
    }

    [Fact]
    public void ValidateTextContent_EmptyContent_ReturnsFailure()
    {
        var result = FileContentValidator.ValidateTextContent("", "Test content");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void ValidateTextContent_ContentWithNullBytes_ReturnsFailure()
    {
        var content = "This looks like text\0but has null bytes";

        var result = FileContentValidator.ValidateTextContent(content, "Test content");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("binary data");
    }

    [Fact]
    public void ValidateTextContent_BinaryContentDisguisedAsText_ReturnsFailure()
    {
        // Simulate binary content: mix of valid text and control characters
        var content = "Normal text\x01\x02\x03hidden binary";

        var result = FileContentValidator.ValidateTextContent(content, "Markdown content");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("binary data");
    }

    [Fact]
    public void ValidateTextContent_ContentWithDelCharacter_ReturnsFailure()
    {
        var content = "Normal text\x7Fmore text";

        var result = FileContentValidator.ValidateTextContent(content, "Test content");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("binary data");
    }

    [Fact]
    public void ValidateTextContent_ContentWithTabsAndNewlines_ReturnsSuccess()
    {
        var content = "Column1\tColumn2\tColumn3\r\nValue1\tValue2\tValue3\n";

        var result = FileContentValidator.ValidateTextContent(content, "CSV payload");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateTextContent_ContentExceedsSizeLimit_ReturnsFailure()
    {
        var content = new string('A', 1000);

        var result = FileContentValidator.ValidateTextContent(content, "Test content", maxBytes: 500);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("maximum allowed size");
        result.ErrorMessage.Should().Contain("500 bytes");
    }

    [Fact]
    public void ValidateTextContent_ContentExactlyAtSizeLimit_ReturnsSuccess()
    {
        var content = new string('A', 100);

        var result = FileContentValidator.ValidateTextContent(content, "Test content", maxBytes: 100);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateTextContent_ZeroMaxBytes_SkipsSizeCheck()
    {
        var content = new string('A', 10_000);

        var result = FileContentValidator.ValidateTextContent(content, "Test content", maxBytes: 0);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateTextContent_UnicodeContent_ReturnsSuccess()
    {
        var content = "日本語テスト\nEmoji: 🎉\nFrench: café\nArabic: مرحبا";

        var result = FileContentValidator.ValidateTextContent(content, "Unicode content");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateTextContent_WindowsSmartQuotes_ReturnsSuccess()
    {
        // Windows-1252 smart quotes (U+0091-0094) are allowed
        var content = "He said “Hello” and she replied ‘Hi’";

        var result = FileContentValidator.ValidateTextContent(content, "Test content");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateTextContent_ContentWithBom_ReturnsSuccess()
    {
        var content = "﻿# BOM-prefixed markdown";

        var result = FileContentValidator.ValidateTextContent(content, "Markdown content");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateTextContent_MultibyteUnicodeExceedsByteLimitButNotCharLimit_ReturnsFailure()
    {
        // Each CJK character is 3 bytes in UTF-8
        // 40 CJK characters = 120 bytes > 100 byte limit
        var content = new string('日', 40);

        var result = FileContentValidator.ValidateTextContent(content, "Test content", maxBytes: 100);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("maximum allowed size");
    }

    // =====================================================================
    // ValidateJsonContent tests
    // =====================================================================

    [Fact]
    public void ValidateJsonContent_ValidJsonObject_ReturnsSuccess()
    {
        var json = """{"name":"Test Board","columns":[{"name":"Todo"}]}""";

        var result = FileContentValidator.ValidateJsonContent(json, "Board JSON");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateJsonContent_ValidJsonArray_ReturnsSuccess()
    {
        var json = """[{"id":1},{"id":2}]""";

        var result = FileContentValidator.ValidateJsonContent(json, "JSON data");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateJsonContent_NullContent_ReturnsFailure()
    {
        var result = FileContentValidator.ValidateJsonContent(null, "JSON data");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("required");
    }

    [Fact]
    public void ValidateJsonContent_EmptyContent_ReturnsFailure()
    {
        var result = FileContentValidator.ValidateJsonContent("", "JSON data");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ValidateJsonContent_NonJsonText_ReturnsFailure()
    {
        var result = FileContentValidator.ValidateJsonContent("This is not JSON", "JSON data");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Expected content starting with");
    }

    [Fact]
    public void ValidateJsonContent_MalformedJson_ReturnsFailure()
    {
        var result = FileContentValidator.ValidateJsonContent("{invalid json: broken}", "JSON data");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("malformed JSON");
    }

    [Fact]
    public void ValidateJsonContent_JsonWithBinaryContent_ReturnsFailure()
    {
        var json = "{\"name\": \"test\0binary\"}";

        var result = FileContentValidator.ValidateJsonContent(json, "JSON data");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("binary data");
    }

    [Fact]
    public void ValidateJsonContent_JsonExceedsSizeLimit_ReturnsFailure()
    {
        var json = "{\"data\":\"" + new string('X', 2000) + "\"}";

        var result = FileContentValidator.ValidateJsonContent(json, "JSON data", maxBytes: 1000);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("maximum allowed size");
    }

    [Fact]
    public void ValidateJsonContent_JsonWithBom_ReturnsSuccess()
    {
        var json = "﻿{\"name\": \"test\"}";

        var result = FileContentValidator.ValidateJsonContent(json, "JSON data");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateJsonContent_WhitespaceOnlyContent_ReturnsFailure()
    {
        var result = FileContentValidator.ValidateJsonContent("   \n\t  ", "JSON data");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty after removing whitespace");
    }

    [Fact]
    public void ValidateJsonContent_JsonStartingWithWhitespace_ReturnsSuccess()
    {
        var json = "  \n  {\"name\": \"test\"}";

        var result = FileContentValidator.ValidateJsonContent(json, "JSON data");

        result.IsSuccess.Should().BeTrue();
    }

    // =====================================================================
    // ValidateSqliteContent tests
    // =====================================================================

    [Fact]
    public void ValidateSqliteContent_ValidSqliteHeader_ReturnsSuccess()
    {
        var data = CreateSqlitePayload();

        var result = FileContentValidator.ValidateSqliteContent(data, maxBytes: 10 * 1024 * 1024);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateSqliteContent_NullData_ReturnsFailure()
    {
        var result = FileContentValidator.ValidateSqliteContent(null, maxBytes: 10 * 1024 * 1024);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("required");
    }

    [Fact]
    public void ValidateSqliteContent_EmptyData_ReturnsFailure()
    {
        var result = FileContentValidator.ValidateSqliteContent(Array.Empty<byte>(), maxBytes: 10 * 1024 * 1024);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ValidateSqliteContent_WrongMagicBytes_ReturnsFailure()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("This is not a SQLite file at all");

        var result = FileContentValidator.ValidateSqliteContent(data, maxBytes: 10 * 1024 * 1024);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not match expected database format");
    }

    [Fact]
    public void ValidateSqliteContent_TruncatedHeader_ReturnsFailure()
    {
        var data = "SQLite for"u8.ToArray(); // Only 10 bytes, header needs 16

        var result = FileContentValidator.ValidateSqliteContent(data, maxBytes: 10 * 1024 * 1024);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not match expected database format");
    }

    [Fact]
    public void ValidateSqliteContent_ExceedsSizeLimit_ReturnsFailure()
    {
        var data = CreateSqlitePayload(size: 2000);

        var result = FileContentValidator.ValidateSqliteContent(data, maxBytes: 1000);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("maximum allowed size");
    }

    // =====================================================================
    // ContainsBinaryContent tests (internal)
    // =====================================================================

    [Theory]
    [InlineData("\0")]
    [InlineData("\x01")]
    [InlineData("\x02")]
    [InlineData("\x1F")]
    [InlineData("\x7F")]
    public void ContainsBinaryContent_ControlCharacters_ReturnsTrue(string content)
    {
        FileContentValidator.ContainsBinaryContent(content).Should().BeTrue();
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("normal text")]
    [InlineData("")]
    public void ContainsBinaryContent_NormalTextChars_ReturnsFalse(string content)
    {
        FileContentValidator.ContainsBinaryContent(content).Should().BeFalse();
    }

    [Fact]
    public void ContainsBinaryContent_C1ControlRange_DetectsBinaryExceptCommonWindows1252()
    {
        // Uncommon C1 control chars (0x80, 0x81) should be detected as binary
        FileContentValidator.ContainsBinaryContent("\x80").Should().BeTrue();
        FileContentValidator.ContainsBinaryContent("\x81").Should().BeTrue();

        // Common Windows-1252 characters should be allowed:
        // 0x85 (NEL/ellipsis), 0x91-0x94 (smart quotes), 0x96-0x97 (dashes)
        FileContentValidator.ContainsBinaryContent("\x85").Should().BeFalse();
        FileContentValidator.ContainsBinaryContent("\x91").Should().BeFalse();
        FileContentValidator.ContainsBinaryContent("\x92").Should().BeFalse();
        FileContentValidator.ContainsBinaryContent("\x93").Should().BeFalse();
        FileContentValidator.ContainsBinaryContent("\x94").Should().BeFalse();
        FileContentValidator.ContainsBinaryContent("\x96").Should().BeFalse();
        FileContentValidator.ContainsBinaryContent("\x97").Should().BeFalse();
    }

    // =====================================================================
    // StripBomAndWhitespace tests (internal)
    // =====================================================================

    [Fact]
    public void StripBomAndWhitespace_BomPrefix_StripsIt()
    {
        var content = "﻿{\"test\": true}";

        var result = FileContentValidator.StripBomAndWhitespace(content);

        result.Should().Be("{\"test\": true}");
    }

    [Fact]
    public void StripBomAndWhitespace_WhitespacePrefix_StripsIt()
    {
        var result = FileContentValidator.StripBomAndWhitespace("  \t\n  hello");

        result.Should().Be("hello");
    }

    [Fact]
    public void StripBomAndWhitespace_BomAndWhitespace_StripsBoth()
    {
        var result = FileContentValidator.StripBomAndWhitespace("﻿  {\"test\": true}");

        result.Should().Be("{\"test\": true}");
    }

    [Fact]
    public void StripBomAndWhitespace_NoBomNoWhitespace_ReturnsUnchanged()
    {
        var result = FileContentValidator.StripBomAndWhitespace("{\"test\": true}");

        result.Should().Be("{\"test\": true}");
    }

    // =====================================================================
    // Edge cases
    // =====================================================================

    [Fact]
    public void ValidateTextContent_ErrorMessageIncludesContentLabel()
    {
        var result = FileContentValidator.ValidateTextContent("binary\0content", "Markdown content");

        result.ErrorMessage.Should().Contain("Markdown content");
    }

    [Fact]
    public void ValidateJsonContent_ErrorMessageIncludesContentLabel()
    {
        var result = FileContentValidator.ValidateJsonContent("not json", "Board import JSON");

        result.ErrorMessage.Should().Contain("Board import JSON");
    }

    [Fact]
    public void ValidateTextContent_OnlyNullByte_ReturnsFailure()
    {
        var result = FileContentValidator.ValidateTextContent("\0", "Content");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ValidateJsonContent_NestedJsonObject_ReturnsSuccess()
    {
        var json = """
        {
            "board": {
                "name": "Test",
                "columns": [
                    {"name": "Todo", "position": 0},
                    {"name": "Done", "position": 1}
                ]
            },
            "metadata": {"version": "1.0"}
        }
        """;

        var result = FileContentValidator.ValidateJsonContent(json, "JSON data");

        result.IsSuccess.Should().BeTrue();
    }

    // =====================================================================
    // Constant value tests (ensures limits match between validator and services)
    // =====================================================================

    [Fact]
    public void MaxMarkdownContentBytes_MatchesExpectedValue()
    {
        FileContentValidator.MaxMarkdownContentBytes.Should().Be(102_400);
    }

    [Fact]
    public void MaxWebClipContentBytes_MatchesExpectedValue()
    {
        FileContentValidator.MaxWebClipContentBytes.Should().Be(20_000);
    }

    [Fact]
    public void MaxCsvPayloadBytes_MatchesExpectedValue()
    {
        FileContentValidator.MaxCsvPayloadBytes.Should().Be(1_048_576);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static byte[] CreateSqlitePayload(int size = 100)
    {
        var header = "SQLite format 3\0"u8.ToArray();
        var payload = new byte[Math.Max(size, header.Length)];
        header.CopyTo(payload, 0);
        return payload;
    }
}
