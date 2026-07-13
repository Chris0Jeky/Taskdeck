using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class SourceArtefactConfiguration : IEntityTypeConfiguration<SourceArtefact>
{
    public void Configure(EntityTypeBuilder<SourceArtefact> builder)
    {
        builder.ToTable("SourceArtefacts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.BoardId);
        builder.Property(a => a.Kind).HasConversion<int>().IsRequired();
        builder.Property(a => a.MimeType).HasMaxLength(SourceArtefact.MaxMimeTypeLength).IsRequired();
        builder.Property(a => a.FileName).HasMaxLength(SourceArtefact.MaxFileNameLength).IsRequired();
        builder.Property(a => a.ByteSize).IsRequired();
        builder.Property(a => a.Sha256).HasMaxLength(SourceArtefact.Sha256HexLength).IsRequired();
        builder.Property(a => a.CaptureSource).HasConversion<int>().IsRequired();
        builder.Property(a => a.OriginReference).HasMaxLength(SourceArtefact.MaxOriginReferenceLength);
        builder.Property(a => a.CreatedFromCaptureId);
        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();

        builder.HasIndex(a => new { a.UserId, a.CreatedAt });
        builder.HasIndex(a => a.BoardId);
        builder.HasIndex(a => new { a.UserId, a.Sha256 });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Board>()
            .WithMany()
            .HasForeignKey(a => a.BoardId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
