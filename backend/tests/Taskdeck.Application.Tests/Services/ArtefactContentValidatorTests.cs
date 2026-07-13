using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class ArtefactContentValidatorTests
{
    [Theory]
    [MemberData(nameof(AllowedFixtures))]
    public async Task ReadAndValidateAsync_ShouldAcceptAllowedMagicBytes(
        string fileName,
        string mimeType,
        byte[] bytes,
        ArtefactKind expectedKind)
    {
        await using var stream = new MemoryStream(bytes);

        var result = await ArtefactContentValidator.ReadAndValidateAsync(
            stream,
            fileName,
            mimeType,
            1024);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(expectedKind);
        result.Value.Bytes.Should().Equal(bytes);
        result.Value.Sha256.Should().HaveLength(64);
    }

    [Fact]
    public async Task ReadAndValidateAsync_ShouldRejectExecutableRenamedAsPng()
    {
        await using var stream = new MemoryStream("MZ executable"u8.ToArray());

        var result = await ArtefactContentValidator.ReadAndValidateAsync(
            stream,
            "evidence.png",
            "image/png",
            1024);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ReadAndValidateAsync_ShouldStopAtStreamingSizeLimit()
    {
        await using var stream = new MemoryStream(new byte[1025]);

        var result = await ArtefactContentValidator.ReadAndValidateAsync(
            stream,
            "large.txt",
            "text/plain",
            1024);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PayloadTooLarge);
        stream.Position.Should().Be(1025);
    }

    [Fact]
    public async Task ReadAndValidateAsync_ShouldAcceptUnicodeThroughRuneValidation()
    {
        await using var stream = new MemoryStream("Notes: café 🚀"u8.ToArray());

        var result = await ArtefactContentValidator.ReadAndValidateAsync(
            stream,
            "notes.txt",
            "text/plain",
            1024);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidTextFixtures))]
    public async Task ReadAndValidateAsync_ShouldRejectInvalidUtf8AndDisallowedControls(byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);

        var result = await ArtefactContentValidator.ReadAndValidateAsync(
            stream,
            "notes.txt",
            "text/plain",
            1024);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("../evidence.png")]
    [InlineData("folder\\evidence.png")]
    public async Task ReadAndValidateAsync_ShouldRejectPathBearingFileNames(string fileName)
    {
        await using var stream = new MemoryStream(PngBytes());

        var result = await ArtefactContentValidator.ReadAndValidateAsync(
            stream,
            fileName,
            "image/png",
            1024);

        result.IsSuccess.Should().BeFalse();
    }

    public static IEnumerable<object[]> AllowedFixtures()
    {
        yield return ["image.png", "image/png", PngBytes(), ArtefactKind.Image];
        yield return ["photo.jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, ArtefactKind.Image];
        yield return ["picture.webp", "image/webp", "RIFF1234WEBP"u8.ToArray(), ArtefactKind.Image];
        yield return ["document.pdf", "application/pdf", "%PDF-1.7"u8.ToArray(), ArtefactKind.Pdf];
        yield return ["notes.txt", "text/plain", "plain notes"u8.ToArray(), ArtefactKind.TextFile];
        yield return ["notes.md", "text/markdown", "# Notes"u8.ToArray(), ArtefactKind.TextFile];
    }

    public static IEnumerable<object[]> InvalidTextFixtures()
    {
        yield return [new byte[] { 0xC3, 0x28 }];
        yield return [new byte[] { (byte)'a', 0x00, (byte)'b' }];
    }

    private static byte[] PngBytes()
        => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
}
