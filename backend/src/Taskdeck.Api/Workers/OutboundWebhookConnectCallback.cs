using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Workers;

internal static class OutboundWebhookConnectCallback
{
    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        bool allowLocalhostEndpoints,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var allowedAddresses = await OutboundWebhookEndpointGuard.ResolveAllowedAddressesAsync(
            host,
            allowLocalhostEndpoints,
            cancellationToken);
        if (allowedAddresses.Count == 0)
        {
            throw new HttpRequestException($"Webhook endpoint host '{host}' is not allowed.");
        }

        Exception? lastConnectException = null;

        foreach (var address in allowedAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(address, port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (SocketException ex)
            {
                lastConnectException = ex;
                socket.Dispose();
            }
        }

        throw new HttpRequestException(
            $"Failed to establish webhook connection to '{host}:{port}'.",
            lastConnectException);
    }
}
