from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    target = Path(path)
    text = target.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one anchor, found {count}: {old[:100]!r}")
    target.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")


bootstrap_path = "backend/src/Taskdeck.Api/FirstRun/FirstRunBootstrapper.cs"
replace_once(
    bootstrap_path,
    """    internal const string MissingConnectorEncryptionKeyMessagePrefix =
        "SECURITY: The Connectors:EncryptionKey is not configured.";

    private const string LocalConfigFileName = "appsettings.local.json";
""",
    """    internal const string MissingConnectorEncryptionKeyMessagePrefix =
        "SECURITY: The Connectors:EncryptionKey is not configured.";

    internal const string ExistingDatabaseMissingConnectorEncryptionKeyMessagePrefix =
        "First-run: An existing database was found";

    internal const string ConnectorEncryptionKeyPersistenceFailureMessagePrefix =
        "First-run: Could not persist the connector encryption key";

    private const string LocalConfigFileName = "appsettings.local.json";
""",
)
replace_once(
    bootstrap_path,
    "                $\"First-run: An existing database was found at {databasePath}, but no supplied or \" +\n",
    "                ExistingDatabaseMissingConnectorEncryptionKeyMessagePrefix +\n"
    "                $\" at {databasePath}, but no supplied or \" +\n",
)
replace_once(
    bootstrap_path,
    "                    $\"First-run: Could not persist the connector encryption key to {localConfigPath} \" +\n",
    "                    ConnectorEncryptionKeyPersistenceFailureMessagePrefix +\n"
    "                    $\" to {localConfigPath} \" +\n",
)

runtime_path = "backend/src/Taskdeck.Api/FirstRun/DesktopRuntime.cs"
runtime_marker = "        if (exception is RetiredLlmProviderConfigurationException)\n"
runtime_classifiers = """        if (exception is InvalidOperationException existingDataFailure
            && existingDataFailure.Message.StartsWith(
                FirstRunBootstrapper.ExistingDatabaseMissingConnectorEncryptionKeyMessagePrefix,
                StringComparison.Ordinal))
        {
            return
            [
                "TASKDECK_DESKTOP_FATAL code=connector_encryption_key_unrecoverable",
                "Taskdeck found existing data but could not recover its connector encryption key. Restore the original key " +
                "or set the matching Connectors__EncryptionKey value before restarting. Do not generate a replacement key " +
                "for this data. No paths or settings were printed."
            ];
        }

        if (exception is InvalidOperationException persistenceFailure
            && persistenceFailure.Message.StartsWith(
                FirstRunBootstrapper.ConnectorEncryptionKeyPersistenceFailureMessagePrefix,
                StringComparison.Ordinal))
        {
            return
            [
                "TASKDECK_DESKTOP_FATAL code=connector_encryption_key_persistence_failed",
                "Taskdeck could not securely persist its connector encryption key. Make the local-config directory writable " +
                "on a filesystem that supports owner-only permissions, or set a stable Connectors__EncryptionKey value and " +
                "restart. No paths or settings were printed."
            ];
        }

"""
replace_once(runtime_path, runtime_marker, runtime_classifiers + runtime_marker)

config_path = "docs/platform/CONFIGURATION_REFERENCE.md"
replace_once(
    config_path,
    """- Development, Test/Staging, and headless Production: the historical
  executable-local path, preserving development and container compatibility.

For MCP stdio, `DOTNET_ENVIRONMENT` is authoritative when it is nonblank;
""",
    """- Development, Test/Staging, and headless Production: the historical
  executable-local path, preserving development and container compatibility.

| Environment variable | Interpretation | Storage effect | Required? |
| --- | --- | --- | --- |
| `TASKDECK_HEADLESS` | Presence-based (`1`, `true`, or any other nonblank value all enable it) | Disables desktop-style connector-key generation and automatic per-user database relocation. It does **not** choose a data directory. | Set explicitly for server/container or packaged automation runs; leave unset for normal desktop use. |

`TASKDECK_HEADLESS` changes bootstrap identity policy, not the storage root. There is no separate
headless app-data-root override. To keep a packaged automation run from writing state beside the
executable, supply stable `Jwt__SecretKey` and `Connectors__EncryptionKey` values on every start and
set `ConnectionStrings__DefaultConnection` to an absolute SQLite path. With both identities supplied,
the bootstrapper has no generated secret to persist. `FirstRun__ResolveAppDataDbPath` does not relocate
a headless database; container and automation operators must make the database path explicit. Reuse the
same connector key after restart because replacing it makes stored connector credentials unreadable.

For MCP stdio, `DOTNET_ENVIRONMENT` is authoritative when it is nonblank;
""",
)

quick_path = "docs/releases/WINDOWS_QUICK_START.md"
replace_once(
    quick_path,
    """The console prints the effective data directory on every packaged start. An explicit absolute
`ConnectionStrings__DefaultConnection` override can move the database; if you use one, back up that
exact path instead.
""",
    """The console prints the effective data directory on every packaged start. An explicit absolute
`ConnectionStrings__DefaultConnection` override can move the database; if you use one, back up that
exact path instead.

`TASKDECK_HEADLESS` is an advanced server/automation switch, not an alternate desktop data-root
selector. It leaves local configuration executable-local and disables automatic per-user database
relocation. A packaged headless run that must keep all writable state outside the extracted release
folder must provide stable `Jwt__SecretKey` and `Connectors__EncryptionKey` values on every start and
an absolute `ConnectionStrings__DefaultConnection` path. See the
[generated local-configuration contract](https://github.com/Chris0Jeky/Taskdeck/blob/main/docs/platform/CONFIGURATION_REFERENCE.md#generated-local-configuration-file).
""",
)
replace_once(
    quick_path,
    "- `TASKDECK_DESKTOP_FATAL code=retired_provider_configuration`: Taskdeck found configuration for the\n",
    """- **Unreleased (`main` after this repair)** `TASKDECK_DESKTOP_FATAL
  code=connector_encryption_key_unrecoverable`: Taskdeck found an existing database but could not
  recover the connector key that protects stored credentials. Restore the original key or set that
  exact value through `Connectors__EncryptionKey`; do not generate a replacement for the existing data.
- **Unreleased (`main` after this repair)** `TASKDECK_DESKTOP_FATAL
  code=connector_encryption_key_persistence_failed`: normal desktop startup could not securely persist
  its generated connector key. Use a writable local-config directory on NTFS or another filesystem
  supporting owner-only permissions, or supply a stable `Connectors__EncryptionKey` and restart.
- `TASKDECK_DESKTOP_FATAL code=retired_provider_configuration`: Taskdeck found configuration for the
""",
)
