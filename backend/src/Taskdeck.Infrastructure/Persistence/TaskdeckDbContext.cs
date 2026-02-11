using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence;

public class TaskdeckDbContext : DbContext
{
    public TaskdeckDbContext(DbContextOptions<TaskdeckDbContext> options) : base(options)
    {
    }

    public DbSet<Board> Boards => Set<Board>();
    public DbSet<Column> Columns => Set<Column>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<CardLabel> CardLabels => Set<CardLabel>();
    public DbSet<User> Users => Set<User>();
    public DbSet<BoardAccess> BoardAccesses => Set<BoardAccess>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LlmRequest> LlmRequests => Set<LlmRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskdeckDbContext).Assembly);
    }
}
