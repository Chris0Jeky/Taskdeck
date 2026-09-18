using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class RepresentationConfiguration : IEntityTypeConfiguration<Representation>
{
    public void Configure(EntityTypeBuilder<Representation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ProcessorId).HasMaxLength(200);
        builder.Property(x => x.ProcessorVersion).HasMaxLength(100);
        builder.Property(x => x.ProcessorModel).HasMaxLength(200);
        builder.Property(x => x.ContentHash).HasMaxLength(64);
        builder.Property(x => x.ConfigurationHash).HasMaxLength(64);
        builder.Property(x => x.Language).HasMaxLength(100);
        builder.Property(x => x.Warnings).HasConversion(
            value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
            value => (IReadOnlyList<string>)(JsonSerializer.Deserialize<string[]>(value, (JsonSerializerOptions?)null) ?? Array.Empty<string>()))
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                value => value.ToArray()));
        builder.HasIndex(x => new { x.UserId, x.CaptureId });
        builder.HasOne<Capture>().WithMany().HasForeignKey(x => x.CaptureId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SourceAsset>().WithMany().HasForeignKey(x => x.ParentSourceAssetId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Representation>().WithMany().HasForeignKey(x => x.ParentRepresentationId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class RepresentationSupersessionConfiguration : IEntityTypeConfiguration<RepresentationSupersession>
{
    public void Configure(EntityTypeBuilder<RepresentationSupersession> builder)
    {
        builder.HasKey(x => x.RepresentationId);
        builder.HasOne<Representation>().WithMany().HasForeignKey(x => x.RepresentationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Representation>().WithMany().HasForeignKey(x => x.SupersededByRepresentationId).OnDelete(DeleteBehavior.NoAction);
    }
}
