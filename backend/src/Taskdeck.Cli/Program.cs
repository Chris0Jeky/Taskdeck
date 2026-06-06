using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Services;
using Taskdeck.Cli;
using Taskdeck.Cli.Commands;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;

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
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<ColumnService>();
builder.Services.AddScoped<CardService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddScoped<BoardsCommandHandler>();
builder.Services.AddScoped<ColumnsCommandHandler>();
builder.Services.AddScoped<CardsCommandHandler>();
builder.Services.AddScoped<ApiKeysCommandHandler>();

using var host = builder.Build();

using (var startupScope = host.Services.CreateScope())
{
    var dbContext = startupScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
    // Serialize migrations across processes (API/MCP/CLI) via a file lock (#1164).
    // Pass no logger: CLI stdout must stay clean JSON (the helper never writes to stdout,
    // but logging is suppressed here regardless).
    SerializedMigrator.Migrate(dbContext);
}

var dispatcher = new CommandDispatcher(host.Services);
return await dispatcher.DispatchAsync(args);
