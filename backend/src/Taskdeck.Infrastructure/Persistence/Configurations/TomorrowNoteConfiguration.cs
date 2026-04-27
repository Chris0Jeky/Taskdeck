using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class TomorrowNoteConfiguration : IEntityTypeConfiguration<TomorrowNote>
{
    public void Configure(EntityTypeBuilder<TomorrowNote> builder)
    {
        builder.ToTable("TomorrowNotes");

        builder.HasKey(note => note.Id);

        builder.Property(note => note.Id)
            .ValueGeneratedNever();

        builder.Property(note => note.UserId)
            .IsRequired();

        builder.Property(note => note.Date)
            .IsRequired();

        builder.Property(note => note.Text)
            .HasMaxLength(TomorrowNote.MaxTextLength)
            .IsRequired();

        builder.Property(note => note.CreatedAt)
            .IsRequired();

        builder.Property(note => note.UpdatedAt)
            .IsRequired();

        builder.HasIndex(note => new { note.UserId, note.Date })
            .IsUnique();
    }
}
