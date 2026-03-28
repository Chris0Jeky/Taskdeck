using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("KnowledgeDocuments");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .ValueGeneratedNever();

        builder.Property(d => d.UserId)
            .IsRequired();

        builder.Property(d => d.BoardId);

        builder.Property(d => d.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.Content)
            .IsRequired()
            .HasMaxLength(50000);

        builder.Property(d => d.SourceType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(d => d.SourceUrl)
            .HasMaxLength(2000);

        builder.Property(d => d.Tags)
            .HasMaxLength(2000);

        builder.Property(d => d.IsArchived)
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .IsRequired();

        builder.Property(d => d.UpdatedAt)
            .IsRequired();

        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.BoardId);
        builder.HasIndex(d => new { d.UserId, d.IsArchived });
    }
}
