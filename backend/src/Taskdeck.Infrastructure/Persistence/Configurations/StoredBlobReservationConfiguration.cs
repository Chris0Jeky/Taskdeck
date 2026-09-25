using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class StoredBlobReservationConfiguration : IEntityTypeConfiguration<StoredBlobReservation>
{
    public void Configure(EntityTypeBuilder<StoredBlobReservation> builder)
    {
        builder.ToTable("StoredBlobReservations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Modality).HasConversion<int>();
        builder.Property(x => x.ReferrerKind).HasMaxLength(100);
        builder.HasIndex(x => new { x.OwnerUserId, x.ExpiresAtUtc });
        builder.HasIndex(x => new { x.OwnerUserId, x.Modality, x.ExpiresAtUtc });
        builder.HasIndex(x => x.ExpiresAtUtc);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
