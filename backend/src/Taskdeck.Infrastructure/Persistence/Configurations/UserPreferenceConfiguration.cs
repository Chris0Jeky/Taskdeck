using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.ToTable("UserPreferences");

        builder.HasKey(preference => preference.Id);

        builder.Property(preference => preference.Id)
            .ValueGeneratedNever();

        builder.Property(preference => preference.UserId)
            .IsRequired();

        builder.Property(preference => preference.WorkspaceMode)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(preference => preference.OnboardingVisibility)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(preference => preference.OnboardingDismissedAt);

        builder.Property(preference => preference.OnboardingCompletedAt);

        builder.Property(preference => preference.CreatedAt)
            .IsRequired();

        builder.Property(preference => preference.UpdatedAt)
            .IsRequired();

        builder.HasOne(preference => preference.User)
            .WithMany()
            .HasForeignKey(preference => preference.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(preference => preference.UserId)
            .IsUnique();
    }
}
