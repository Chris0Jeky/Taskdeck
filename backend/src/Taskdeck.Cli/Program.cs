using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Cli;
using Taskdeck.Cli.Commands;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;

var startupTrace = CliStartupTrace.CreateFromTestHarnessEnvironment();
startupTrace.Record(CliStartupTrace.ManagedEntryPhase);

// `taskdeck --version` answers before anything else is touched: no configuration,
// no encryption-key bootstrap, no database, no migrations (#1804). It must stay
// answerable on a machine whose data directory is missing or corrupt.
if (VersionCommand.IsVersionRequest(args))
{
    return VersionCommand.Execute();
}

var builder = Host.CreateApplicationBuilder(args);

// CLI stdout must be clean JSON. Remove all default logging providers so EF Core
// and framework diagnostics never corrupt JSON output parsed by callers.
builder.Logging.ClearProviders();

// Honor the documented TASKDECK_-prefixed environment variables. The default host
// only registers the no-prefix provider, so TASKDECK_CONNECTORS__ENCRYPTIONKEY
// (advertised in docs and the AddInfrastructure fail-fast message) would otherwise
// never map to Connectors:EncryptionKey. Registering the TASKDECK_ prefix maps it
// (the canonical Connectors__EncryptionKey keeps working via the no-prefix provider).
builder.Configuration.AddEnvironmentVariables("TASKDECK_");

var fallbackConnectionString = Environment.GetEnvironmentVariable("TASKDECK_CONNECTION_STRING")
    ?? "Data Source=taskdeck.db";

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = fallbackConnectionString
    });
}

// Fresh-machine bootstrap: provision the connector encryption key before
// AddInfrastructure (which fail-fasts on a missing key). Must run after the
// connection string is resolved so the key is persisted next to the data dir.
CliFirstRunBootstrapper.EnsureConnectorEncryptionKey(builder.Configuration);

builder.Services.AddInfrastructure(builder.Configuration);
var registrationSettings = builder.Configuration
    .GetSection("Auth:Registration")
    .Get<RegistrationSettings>() ?? new RegistrationSettings();
builder.Services.AddSingleton(registrationSettings);
builder.Services.AddScoped<IRegistrationPolicyService, RegistrationPolicyService>();
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<ColumnService>();
builder.Services.AddScoped<CardService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddScoped<BoardsCommandHandler>();
builder.Services.AddScoped<ColumnsCommandHandler>();
builder.Services.AddScoped<CardsCommandHandler>();
builder.Services.AddScoped<ApiKeysCommandHandler>();
builder.Services.AddScoped<InvitesCommandHandler>();

startupTrace.Record(CliStartupTrace.HostBuildBeginPhase);
var exitCode = 1;
using (var host = builder.Build())
{
    startupTrace.Record(CliStartupTrace.HostBuildEndPhase);

    startupTrace.Record(CliStartupTrace.MigrationBeginPhase);
    using (var startupScope = host.Services.CreateScope())
    {
        var dbContext = startupScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var backupSettings = startupScope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseBackupSettings>>().Value;
        // Serialize migrations across processes (API/MCP/CLI) via a file lock (#1164), taking a
        // fail-closed pre-migration snapshot of the SQLite file when migrations are pending (#1803).
        // Pass no logger: CLI stdout must stay clean JSON (the helper never writes to stdout,
        // but logging is suppressed here regardless). A backup failure still surfaces: it throws
        // PreMigrationBackupException out of startup rather than migrating unprotected.
        SerializedMigrator.Migrate(dbContext, backupSettings);
    }

    // ADR-0065 / CF-01 (#2255): the CLI shares the database with the API and MCP hosts, so it runs
    // the same post-migration data step. No logger: CLI stdout stays clean JSON. Idempotent -- on a
    // migrated database this is one marker read and one indexed count.
    ContextFabricBootstrap.RunCaptureBackfill(host.Services);

    startupTrace.Record(CliStartupTrace.MigrationEndPhase);
    var dispatcher = new CommandDispatcher(host.Services);
    startupTrace.Record(CliStartupTrace.DispatchBeginPhase);
    exitCode = await dispatcher.DispatchAsync(args);
    startupTrace.Record(CliStartupTrace.DispatchEndPhase);
}

startupTrace.Record(CliStartupTrace.DisposalEndPhase);
return exitCode;
