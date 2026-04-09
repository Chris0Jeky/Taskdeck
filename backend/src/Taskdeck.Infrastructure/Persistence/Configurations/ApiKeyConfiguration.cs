using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Id)
            .ValueGeneratedNever();

        builder.Property(k => k.UserId)
            .IsRequired();

        builder.Property(k => k.KeyHash)
            .IsRequired()
            .HasMaxLength(64); // SHA-256 hex = 64 chars

        builder.Property(k => k.KeyPrefix_)
            .IsRequired()
            .HasMaxLength(10)
            .HasColumnName("KeyPrefix");

        builder.Property(k => k.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(k => k.ExpiresAt);
        builder.Property(k => k.RevokedAt);
        builder.Property(k => k.LastUsedAt);

        builder.Property(k => k.CreatedAt)
            .IsRequired();

        builder.Property(k => k.UpdatedAt)
            .IsRequired();

        // Index on KeyHash for fast authentication lookups
        builder.HasIndex(k => k.KeyHash)
            .IsUnique();

        // Index on UserId for listing keys per user
        builder.HasIndex(k => k.UserId);

        // Foreign key to Users
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
