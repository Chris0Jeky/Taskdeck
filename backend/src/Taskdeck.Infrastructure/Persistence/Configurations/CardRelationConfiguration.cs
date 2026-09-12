using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;
namespace Taskdeck.Infrastructure.Persistence.Configurations;
public sealed class CardRelationConfiguration : IEntityTypeConfiguration<CardRelation>
{
    public void Configure(EntityTypeBuilder<CardRelation> builder)
    {
        builder.ToTable("CardRelations", table =>
        {
            table.HasCheckConstraint("CK_CardRelations_Kind", "RelationType IN ('relates-to', 'blocks', 'duplicates', 'spawned-from')");
            table.HasCheckConstraint("CK_CardRelations_Endpoints", "SourceCardId <> TargetCardId");
            table.HasCheckConstraint("CK_CardRelations_SymmetricOrder", "RelationType <> 'relates-to' OR SourceCardId < TargetCardId");
        });
        builder.HasKey(edge => new { edge.BoardId, edge.SourceCardId, edge.TargetCardId, edge.RelationType });
        builder.Property(edge => edge.RelationType).HasMaxLength(20).IsRequired();
        builder.HasOne<Card>().WithMany().HasForeignKey(edge => edge.SourceCardId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Card>().WithMany().HasForeignKey(edge => edge.TargetCardId).OnDelete(DeleteBehavior.Cascade);
    }
}
