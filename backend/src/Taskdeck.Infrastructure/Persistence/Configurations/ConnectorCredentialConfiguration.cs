using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ConnectorCredentialConfiguration : IEntityTypeConfiguration<ConnectorCredential>
{
    public void Configure(EntityTypeBuilder<ConnectorCredential> builder)
    {
        builder.ToTable("ConnectorCredentials");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ConnectorId)
            .IsRequired();

        builder.Property(c => c.UserId)
            .IsRequired();

        builder.Property(c => c.AuthMethod)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(c => c.Label)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.EncryptedValue)
            .IsRequired()
            .HasMaxLength(8000);

        builder.Property(c => c.KeyVersion)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(c => c.RotatedAt);
        builder.Property(c => c.ExpiresAt);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        // One credential per connector per user (unique constraint).
        builder.HasIndex(c => new { c.ConnectorId, c.UserId })
            .IsUnique();

        // FK to IntegrationConnectors (cascade delete: credential removed when connector is deleted).
        builder.HasOne<IntegrationConnector>()
            .WithMany()
            .HasForeignKey(c => c.ConnectorId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK to Users (cascade delete: credential removed when user is deleted).
        // Explicit FK prevents orphaned credentials if the user is deleted
        // outside the IntegrationConnector cascade chain.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.UserId);
    }
}
