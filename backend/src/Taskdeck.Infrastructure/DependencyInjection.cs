using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;

namespace Taskdeck.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=taskdeck.db";

        services.AddDbContext<TaskdeckDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IBoardRepository, BoardRepository>();
        services.AddScoped<IColumnRepository, ColumnRepository>();
        services.AddScoped<ICardRepository, CardRepository>();
        services.AddScoped<ILabelRepository, LabelRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IBoardAccessRepository, BoardAccessRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<ILlmQueueRepository, LlmQueueRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
