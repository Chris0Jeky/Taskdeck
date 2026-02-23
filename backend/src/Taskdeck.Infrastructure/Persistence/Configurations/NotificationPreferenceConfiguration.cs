using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.UserId)
            .IsRequired();

        builder.Property(p => p.InAppChannelEnabled)
            .IsRequired();

        builder.Property(p => p.MentionImmediateEnabled)
            .IsRequired();

        builder.Property(p => p.MentionDigestEnabled)
            .IsRequired();

        builder.Property(p => p.AssignmentImmediateEnabled)
            .IsRequired();

        builder.Property(p => p.AssignmentDigestEnabled)
            .IsRequired();

        builder.Property(p => p.ProposalOutcomeImmediateEnabled)
            .IsRequired();

        builder.Property(p => p.ProposalOutcomeDigestEnabled)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .IsRequired();

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.UserId)
            .IsUnique();
    }
}
