using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class SourceAssetTextPayloadConfiguration : IEntityTypeConfiguration<SourceAssetTextPayload>
{
    public void Configure(EntityTypeBuilder<SourceAssetTextPayload> builder)
    {
        builder.ToTable("SourceAssetTextPayloads");
        builder.Ignore(payload => payload.Id);
        builder.HasKey(payload => payload.SourceAssetId);
        builder.Property(payload => payload.SourceAssetId).ValueGeneratedNever();
        builder.Property(payload => payload.Text).HasMaxLength(SourceAsset.MaxInlineTextLength).IsRequired();
        builder.Ignore(payload => payload.CreatedAt);
        builder.Ignore(payload => payload.UpdatedAt);
    }
}
