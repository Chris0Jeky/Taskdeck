using Taskdeck.Api.FirstRun;
using Xunit;

namespace Taskdeck.Api.Tests.FirstRun;

public sealed class DesktopRuntimeConnectorKeyFailureTests
{
    [Theory]
    [InlineData(
        FirstRunBootstrapper.ExistingDatabaseMissingConnectorEncryptionKeyMessagePrefix,
        "connector_encryption_key_unrecoverable",
        "Taskdeck found existing data but could not recover its connector encryption key. Restore the original key " +
        "or set the matching Connectors__EncryptionKey value before restarting. Do not generate a replacement key " +
        "for this data. No paths or settings were printed.")]
    [InlineData(
        FirstRunBootstrapper.ConnectorEncryptionKeyPersistenceFailureMessagePrefix,
        "connector_encryption_key_persistence_failed",
        "Taskdeck could not securely persist its connector encryption key. Make the local-config directory writable " +
        "on a filesystem that supports owner-only permissions, or set a stable Connectors__EncryptionKey value and " +
        "restart. No paths or settings were printed.")]
    public void FormatFatalStartup_MapsConnectorIdentityFailuresWithoutLeakingExceptionDetails(
        string messagePrefix,
        string expectedCode,
        string expectedGuidance)
    {
        const string sensitiveDetail = @"C:\private\taskdeck\appsettings.local.json synthetic-secret-value";
        var exception = new InvalidOperationException($"{messagePrefix}: {sensitiveDetail}");

        var output = DesktopRuntime.FormatFatalStartup(exception);

        Assert.Equal(
            [
                $"TASKDECK_DESKTOP_FATAL code={expectedCode}",
                expectedGuidance
            ],
            output);
        Assert.All(output, line => Assert.DoesNotContain(sensitiveDetail, line));
        Assert.All(output, line => Assert.DoesNotContain("synthetic-secret", line));
        Assert.All(output, line => Assert.DoesNotContain("C:\\private", line));
    }
}
