using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Services;
using Taskdeck.Cli.Commands;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

// CLI stdout must be clean JSON. Remove all default logging providers so EF Core
// and framework diagnostics never corrupt JSON output parsed by callers.
builder.Logging.ClearProviders();

var fallbackConnectionString = Environment.GetEnvironmentVariable("TASKDECK_CONNECTION_STRING")
    ?? "Data Source=taskdeck.db";

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = fallbackConnectionString
    });
}

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
    dbContext.Database.Migrate();
}

var dispatcher = new CommandDispatcher(host.Services);
return await dispatcher.DispatchAsync(args);
