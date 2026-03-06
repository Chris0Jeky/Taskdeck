using Microsoft.Extensions.Logging;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.TestUtilities;

public class InMemoryLoggerTests
{
    [Fact]
    public async Task Log_ShouldCaptureEntriesAcrossConcurrentWriters()
    {
        var logger = new InMemoryLogger<InMemoryLoggerTests>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, 250),
            async (index, cancellationToken) =>
            {
                await Task.Yield();
                logger.LogInformation("message {Index}", index);
            });

        Assert.Equal(250, logger.Entries.Count);
    }
}
