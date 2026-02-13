using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class CommandRunLogConfiguration : IEntityTypeConfiguration<CommandRunLog>
{
    public void Configure(EntityTypeBuilder<CommandRunLog> builder)
    {
        builder.ToTable("CommandRunLogs");

        builder.HasKey(crl => crl.Id);

        builder.Property(crl => crl.Id)
            .ValueGeneratedNever();

        builder.Property(crl => crl.CommandRunId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(crl => crl.Timestamp)
            .IsRequired();

        builder.Property(crl => crl.Level)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(crl => crl.Source)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(crl => crl.Message)
            .IsRequired();

        builder.Property(crl => crl.Metadata);

        builder.Property(crl => crl.CreatedAt)
            .IsRequired();

        builder.Property(crl => crl.UpdatedAt)
            .IsRequired();

        builder.HasIndex(crl => crl.CommandRunId);
        builder.HasIndex(crl => crl.Timestamp);
        builder.HasIndex(crl => crl.Level);
    }
}
