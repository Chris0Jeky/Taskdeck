using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class StoredBlobConfiguration : IEntityTypeConfiguration<StoredBlob>
{
    public void Configure(EntityTypeBuilder<StoredBlob> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ContentHash).HasMaxLength(64);
        builder.HasIndex(x => new { x.OwnerUserId, x.ContentHash }).IsUnique();
        builder.HasAlternateKey(x => new { x.Id, x.OwnerUserId });
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class StoredBlobChunkConfiguration : IEntityTypeConfiguration<StoredBlobChunk>
{
    public void Configure(EntityTypeBuilder<StoredBlobChunk> builder)
    {
        builder.HasKey(x => new { x.BlobId, x.Ordinal });
        builder.Property(x => x.Content).IsRequired();
        builder.HasOne<StoredBlob>().WithMany().HasForeignKey(x => x.BlobId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class StoredBlobReferenceConfiguration : IEntityTypeConfiguration<StoredBlobReference>
{
    public void Configure(EntityTypeBuilder<StoredBlobReference> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReferrerKind).HasMaxLength(100);
        builder.HasIndex(x => new { x.OwnerUserId, x.Modality });
        builder.HasOne<StoredBlob>().WithMany().HasForeignKey(x => new { x.BlobId, x.OwnerUserId })
            .HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Cascade);
    }
}
