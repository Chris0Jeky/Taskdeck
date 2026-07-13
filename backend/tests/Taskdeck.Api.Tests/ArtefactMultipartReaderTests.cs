using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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

    [Fact]
    public async Task ReadAsync_ShouldReturnValidationErrorForInvalidMultipartHeaderLine()
    {
        const string boundary = "artefact-invalid-header";
        var body = $"--{boundary}\r\nnot-a-header\r\n\r\nnotes\r\n--{boundary}--\r\n";
        var context = new DefaultHttpContext();
        context.Request.ContentType = $"multipart/form-data; boundary={boundary}";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));

        var result = await ArtefactMultipartReader.ReadAsync(
            context.Request,
            new ArtefactStorageSettings { MaxBytesPerArtefact = 1024, MaxBytesPerUser = 1024 },
            default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("malformed multipart");
    }

    [Fact]
    public void ConfigureRequestBodyLimit_ShouldAllowConfiguredFilePlusBoundedMultipartOverhead()
    {
        var context = new DefaultHttpContext();
        var feature = new MutableMaxRequestBodySizeFeature
        {
            MaxRequestBodySize = 30_000_000
        };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        const long configuredFileBytes = 50L * 1024 * 1024;

        ArtefactMultipartReader.ConfigureRequestBodyLimit(context, configuredFileBytes);

        feature.MaxRequestBodySize.Should().Be(
            configuredFileBytes + ArtefactMultipartReader.MultipartRequestOverheadBytes);
    }

    private sealed class MutableMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }
}
