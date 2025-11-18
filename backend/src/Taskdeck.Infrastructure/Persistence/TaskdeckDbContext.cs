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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskdeckDbContext).Assembly);
    }
}
