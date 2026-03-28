using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class AgentProfileConfiguration : IEntityTypeConfiguration<AgentProfile>
{
    public void Configure(EntityTypeBuilder<AgentProfile> builder)
    {
        builder.ToTable("AgentProfiles");

        builder.HasKey(ap => ap.Id);

        builder.Property(ap => ap.Id)
            .ValueGeneratedNever();

        builder.Property(ap => ap.UserId)
            .IsRequired();

        builder.Property(ap => ap.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(ap => ap.Description)
            .HasMaxLength(2000);

        builder.Property(ap => ap.TemplateKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ap => ap.ScopeType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(ap => ap.ScopeBoardId);

        builder.Property(ap => ap.PolicyJson)
            .HasMaxLength(8000);

        builder.Property(ap => ap.IsEnabled)
            .IsRequired();

        builder.Property(ap => ap.CreatedAt)
            .IsRequired();

        builder.Property(ap => ap.UpdatedAt)
            .IsRequired();

        builder.HasIndex(ap => ap.UserId);
        builder.HasIndex(ap => ap.TemplateKey);
    }
}
