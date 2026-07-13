using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Taskdeck.Api.Contracts;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ArtefactMultipartReaderTests
{
    [Fact]
    public async Task ReadBoundedBytesAsync_ShouldStopAfterFirstBytePastConfiguredLimit()
    {
        await using var source = new MemoryStream(new byte[1024 * 1024]);

        var result = await ArtefactMultipartReader.ReadBoundedBytesAsync(source, 1024);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PayloadTooLarge);
        source.Position.Should().Be(1025);
    }

    [Fact]
    public async Task ReadAsync_ShouldParseFileBeforeTrailingMetadataFields()
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("notes"u8.ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "notes.txt");
        var boardId = Guid.NewGuid();
        form.Add(new StringContent(boardId.ToString()), "boardId");
        await using var body = new MemoryStream();
        await form.CopyToAsync(body);
        body.Position = 0;
        var context = new DefaultHttpContext();
        context.Request.ContentType = form.Headers.ContentType!.ToString();
        context.Request.Body = body;

        var result = await ArtefactMultipartReader.ReadAsync(
            context.Request,
            new ArtefactStorageSettings { MaxBytesPerArtefact = 1024, MaxBytesPerUser = 1024 },
            default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.FileName.Should().Be("notes.txt");
        result.Value.BoardId.Should().Be(boardId);
        result.Value.Content.Should().Equal("notes"u8.ToArray());
    }
}
