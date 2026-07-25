using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class RegistrationInviteConfiguration : IEntityTypeConfiguration<RegistrationInvite>
{
    public void Configure(EntityTypeBuilder<RegistrationInvite> builder)
    {
        builder.ToTable("RegistrationInvites");
        builder.HasKey(invite => invite.Id);
        builder.Property(invite => invite.Id)
            .ValueGeneratedNever();
        builder.Property(invite => invite.CodeHash)
            .IsRequired()
            .HasMaxLength(64);
        builder.Property(invite => invite.DisplayPrefix)
            .IsRequired()
            .HasMaxLength(12);
        builder.Property(invite => invite.ExpiresAt)
            .IsRequired();
        builder.Property(invite => invite.ConsumedAt);
        builder.Property(invite => invite.CreatedAt)
            .IsRequired();
        builder.Property(invite => invite.UpdatedAt)
            .IsRequired();

        builder.HasIndex(invite => invite.CodeHash)
            .IsUnique();
        builder.HasIndex(invite => new { invite.ConsumedAt, invite.ExpiresAt });
    }
}
