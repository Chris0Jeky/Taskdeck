using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class OAuthAuthCodeConfiguration : IEntityTypeConfiguration<OAuthAuthCode>
{
    public void Configure(EntityTypeBuilder<OAuthAuthCode> builder)
    {
        builder.ToTable("OAuthAuthCodes");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Code)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.Token)
            .IsRequired();

        builder.Property(e => e.Purpose)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("login");

        builder.Property(e => e.ProviderData)
            .HasMaxLength(4096);

        builder.Property(e => e.ExpiresAt)
            .IsRequired();

        builder.Property(e => e.IsConsumed)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.ConsumedAt);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        // Unique index on code for fast lookup
        builder.HasIndex(e => e.Code)
            .IsUnique();

        // Index for TTL cleanup queries
        builder.HasIndex(e => e.ExpiresAt);
    }
}
