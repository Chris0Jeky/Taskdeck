using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class WorkspaceMemoryConfiguration : IEntityTypeConfiguration<WorkspaceMemory>
{
    public void Configure(EntityTypeBuilder<WorkspaceMemory> b)
    {
        b.ToTable("WorkspaceMemories"); b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(240); b.Property(x => x.Text).HasMaxLength(8000);
        b.Property(x => x.OriginalText).HasMaxLength(8000); b.Property(x => x.Status).HasMaxLength(30);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.UserId, x.BoardId });
        b.Property(x => x.SourceQuestionHash).HasMaxLength(64);
        b.HasIndex(x => new { x.UserId, x.SourceCardId, x.SourceLayerId, x.SourceQuestionHash }).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Board>().WithMany().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.History).WithOne().HasForeignKey(x => x.MemoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class WorkspaceMemoryRevisionConfiguration : IEntityTypeConfiguration<WorkspaceMemoryRevision>
{
    public void Configure(EntityTypeBuilder<WorkspaceMemoryRevision> b)
    {
        b.ToTable("WorkspaceMemoryRevisions"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Title).HasMaxLength(240); b.Property(x => x.Text).HasMaxLength(8000);
        b.Property(x => x.Status).HasMaxLength(30);
        b.HasIndex(x => new { x.MemoryId, x.Revision }).IsUnique();
    }
}
public class QuietInsightConfiguration : IEntityTypeConfiguration<QuietInsight>
{
    public void Configure(EntityTypeBuilder<QuietInsight> b)
    {
        b.ToTable("QuietInsights"); b.HasKey(x => x.Id);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.Property(x => x.Rule).HasMaxLength(80); b.Property(x => x.TargetKey).HasMaxLength(80);
        b.Property(x => x.Title).HasMaxLength(300); b.Property(x => x.Detail).HasMaxLength(8000);
        b.Property(x => x.Evidence).HasMaxLength(8000); b.Property(x => x.State).HasMaxLength(30);
        b.HasIndex(x => new { x.UserId, x.BoardId, x.Rule, x.TargetKey }).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Board>().WithMany().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
    }
}
