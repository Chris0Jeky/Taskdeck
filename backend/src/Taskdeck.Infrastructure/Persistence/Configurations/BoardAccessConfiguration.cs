using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class BoardAccessConfiguration : IEntityTypeConfiguration<BoardAccess>
{
    public void Configure(EntityTypeBuilder<BoardAccess> builder)
    {
        builder.ToTable("BoardAccesses");

        builder.HasKey(ba => ba.Id);

        builder.Property(ba => ba.BoardId)
            .IsRequired();

        builder.Property(ba => ba.UserId)
            .IsRequired();

        builder.Property(ba => ba.Role)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(ba => ba.GrantedBy)
            .IsRequired();

        builder.Property(ba => ba.GrantedAt)
            .IsRequired();

        builder.Property(ba => ba.CreatedAt)
            .IsRequired();

        builder.Property(ba => ba.UpdatedAt)
            .IsRequired();

        // Relationships
        builder.HasOne(ba => ba.Board)
            .WithMany(b => b.BoardAccesses)
            .HasForeignKey(ba => ba.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ba => ba.User)
            .WithMany()
            .HasForeignKey(ba => ba.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(ba => new { ba.BoardId, ba.UserId })
            .IsUnique();

        builder.HasIndex(ba => ba.UserId);
    }
}
