using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class ArtefactBlobConfiguration : IEntityTypeConfiguration<ArtefactBlob>
{
    public void Configure(EntityTypeBuilder<ArtefactBlob> builder)
    {
        builder.ToTable("ArtefactBlobs");
        builder.Ignore(b => b.Id);
        builder.HasKey(b => b.SourceArtefactId);
        builder.Property(b => b.SourceArtefactId).ValueGeneratedNever();
        builder.Property(b => b.Content).IsRequired();
        builder.Ignore(b => b.CreatedAt);
        builder.Ignore(b => b.UpdatedAt);

        builder.HasOne<SourceArtefact>()
            .WithOne()
            .HasForeignKey<ArtefactBlob>(b => b.SourceArtefactId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
