using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;
namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class BoardDependenciesConfiguration : IEntityTypeConfiguration<BoardDependencies>
{
    public void Configure(EntityTypeBuilder<BoardDependencies> builder)
    {
        builder.ToTable("BoardDependencies");
        builder.HasKey(graph => graph.BoardId);
        builder.Property(graph => graph.Revision).IsConcurrencyToken();
        builder.HasMany(graph => graph.Relations).WithOne().HasForeignKey(edge => edge.BoardId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(graph => graph.Relations).AutoInclude();
        builder.HasOne<Board>().WithOne().HasForeignKey<BoardDependencies>(graph => graph.BoardId).OnDelete(DeleteBehavior.Cascade);
    }
}
