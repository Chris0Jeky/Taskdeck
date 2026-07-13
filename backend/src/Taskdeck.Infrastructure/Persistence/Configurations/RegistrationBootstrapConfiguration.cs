using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class RegistrationBootstrapConfiguration : IEntityTypeConfiguration<RegistrationBootstrap>
{
    public void Configure(EntityTypeBuilder<RegistrationBootstrap> builder)
    {
        builder.ToTable("RegistrationBootstraps");
        builder.HasKey(bootstrap => bootstrap.Id);
        builder.Property(bootstrap => bootstrap.Id)
            .HasMaxLength(32)
            .ValueGeneratedNever();
        builder.Property(bootstrap => bootstrap.ClaimedAt)
            .IsRequired();
    }
}
