using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Extensions;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Real-Kestrel proof for #2279. HTTP/1.1 permits proxy-style absolute-form request targets;
/// Kestrel accepts them and exposes the decoded path plus the original absolute RawTarget to
/// middleware. The machine-path guard must therefore inspect the path component of that raw form,
/// not silently fall back to the already-decoded <see cref="HttpRequest.Path"/>.
/// </summary>
public sealed class MachinePathAbsoluteFormKestrelTests
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData("/%61pi/boards", HttpStatusCode.NotFound)]
    [InlineData("/ap%69/boards?trace=%61", HttpStatusCode.NotFound)]
    [InlineData("/api/boards", HttpStatusCode.NoContent)]
    [InlineData("/api/boards?trace=%61", HttpStatusCode.NoContent)]
    [InlineData("/%77orkspace/home", HttpStatusCode.NoContent)]
    public async Task AbsoluteFormTarget_EnforcesOnlyTheMachinePrefixSpelling(
        string rawPathAndQuery,
        HttpStatusCode expectedStatus)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

        await using var app = builder.Build();
        app.UseMachinePathCanonicalGuard();
        app.MapGet("/api/boards", () => Results.NoContent());
        app.MapFallback(() => Results.NoContent());

        await app.StartAsync().WaitAsync(IoTimeout);
        try
        {
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses;
            addresses.Should().ContainSingle("the isolated Kestrel host listens on one loopback endpoint");
            var port = new Uri(addresses!.Single()).Port;

            var status = await SendAbsoluteFormRequestAsync(port, rawPathAndQuery);

            status.Should().Be(expectedStatus);
        }
        finally
        {
            await app.StopAsync().WaitAsync(IoTimeout);
        }
    }

    private static async Task<HttpStatusCode> SendAbsoluteFormRequestAsync(
        int port,
        string rawPathAndQuery)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(IoTimeout);
        await using var stream = client.GetStream();

        var target = $"http://127.0.0.1:{port}{rawPathAndQuery}";
        var request =
            $"GET {target} HTTP/1.1\r\n" +
            $"Host: 127.0.0.1:{port}\r\n" +
            "Connection: close\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(bytes).AsTask().WaitAsync(IoTimeout);
        await stream.FlushAsync().WaitAsync(IoTimeout);

        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        var statusLine = await reader.ReadLineAsync().WaitAsync(IoTimeout);
        statusLine.Should().NotBeNullOrWhiteSpace("Kestrel must return an HTTP response status line");

        var parts = statusLine!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        parts.Should().HaveCountGreaterThanOrEqualTo(2);
        int.TryParse(parts[1], out var statusCode).Should().BeTrue(
            "the second status-line token must be an integer HTTP status code; line was {0}",
            statusLine);
        return (HttpStatusCode)statusCode;
    }
}
