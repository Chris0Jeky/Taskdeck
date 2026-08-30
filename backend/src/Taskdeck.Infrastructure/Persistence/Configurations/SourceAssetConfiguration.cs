using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class SourceAssetConfiguration : IEntityTypeConfiguration<SourceAsset>
{
    public void Configure(EntityTypeBuilder<SourceAsset> builder)
    {
        builder.ToTable("SourceAssets");
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.Id).ValueGeneratedNever();
        builder.Property(asset => asset.CaptureId).IsRequired();
        builder.Property(asset => asset.Ordinal).IsRequired();
        builder.Property(asset => asset.Modality).HasConversion<int>().IsRequired();
        builder.Property(asset => asset.MediaType).HasMaxLength(SourceAsset.MaxMediaTypeLength).IsRequired();
        builder.Property(asset => asset.ContentHash).HasMaxLength(SourceAsset.Sha256HexLength).IsRequired();
        builder.Property(asset => asset.ByteSize).IsRequired();
        builder.Property(asset => asset.StorageKind).HasConversion<int>().IsRequired();
        builder.Property(asset => asset.BlobReferenceId);
        builder.Property(asset => asset.LegacyArtefactId);
        builder.Property(asset => asset.ExternalReference).HasMaxLength(SourceAsset.MaxExternalReferenceLength);
        builder.Property(asset => asset.OriginalName).HasMaxLength(SourceAsset.MaxOriginalNameLength);
        builder.Property(asset => asset.SupersedesAssetId);
        builder.Property(asset => asset.SupersededByAssetId);
        builder.Ignore(asset => asset.IsActive);
        builder.Property(asset => asset.CreatedAt).IsRequired();
        builder.Property(asset => asset.UpdatedAt).IsRequired();

        builder.HasIndex(asset => new { asset.CaptureId, asset.Ordinal }).IsUnique();
        builder.HasIndex(asset => asset.ContentHash);
        builder.HasIndex(asset => asset.LegacyArtefactId);
        // The supersession chain is walked from the replacement back to what it replaced when a
        // representation names the exact asset it was derived from (CF-06).
        builder.HasIndex(asset => asset.SupersedesAssetId);

        builder.HasOne(asset => asset.TextPayload)
            .WithOne()
            .HasForeignKey<SourceAssetTextPayload>(payload => payload.SourceAssetId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(asset => asset.TextPayload).AutoInclude(false);
    }
}
