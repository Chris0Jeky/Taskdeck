using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class CardAssignmentConfiguration : IEntityTypeConfiguration<CardAssignment>
{
    public void Configure(EntityTypeBuilder<CardAssignment> builder)
    {
        builder.ToTable("CardAssignments");
        builder.HasKey(a => new { a.CardId, a.UserId });
        builder.HasOne(a => a.Card).WithMany(c => c.Assignments).HasForeignKey(a => a.CardId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.AssignedAt).IsRequired();
        builder.Navigation(a => a.User).AutoInclude();
    }
}
