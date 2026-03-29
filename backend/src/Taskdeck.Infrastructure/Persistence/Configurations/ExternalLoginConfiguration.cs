using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.ToTable("ExternalLogins");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.ProviderUserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.ProviderDisplayName)
            .HasMaxLength(255);

        builder.Property(e => e.AvatarUrl)
            .HasMaxLength(2048);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        // Foreign key to Users with cascading delete
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique constraint: one external login per provider+providerUserId
        builder.HasIndex(e => new { e.Provider, e.ProviderUserId })
            .IsUnique();

        // Index for looking up all external logins for a user
        builder.HasIndex(e => e.UserId);
    }
}
