using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using PersonalPlatform.Domain.Notes;
using PersonalPlatform.Domain.Organization;

namespace PersonalPlatform.Infrastructure.Persistence.Configurations;

internal static class SearchColumns
{
    /// <summary>Shadow tsvector column used for full-text search (language-neutral "simple" configuration).</summary>
    public const string SearchVector = "SearchVector";
}

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> b)
    {
        b.ToTable("notes");
        b.HasKey(n => n.Id);
        b.HasOwner();
        b.HasOwnerScopedKey();
        b.Property(n => n.Title).HasMaxLength(Note.TitleMaxLength);
        b.Property(n => n.Content).HasMaxLength(Note.ContentMaxLength);
        b.Property<NpgsqlTsVector>(SearchColumns.SearchVector)
            .HasComputedColumnSql("to_tsvector('simple'::regconfig, coalesce(title, '') || ' ' || coalesce(content, ''))", stored: true);
        b.HasIndex(SearchColumns.SearchVector).HasMethod("GIN");

        b.HasMany(n => n.Tags).WithOne().HasForeignKey(t => t.NoteId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(n => n.Tags).HasField("_tags");

        b.HasIndex(n => new { n.OwnerId, n.ArchivedAt, n.IsPinned, n.UpdatedAt });
        b.HasXminVersion();
    }
}

internal sealed class NoteTagConfiguration : IEntityTypeConfiguration<NoteTag>
{
    public void Configure(EntityTypeBuilder<NoteTag> b)
    {
        b.ToTable("note_tags");
        b.HasKey(t => new { t.NoteId, t.TagId });
        b.HasOwner();
        b.ReferencesOwned<NoteTag, Tag>(nameof(NoteTag.TagId), DeleteBehavior.Cascade);
        b.HasIndex(t => t.TagId);
    }
}

internal sealed class NoteRevisionConfiguration : IEntityTypeConfiguration<NoteRevision>
{
    public void Configure(EntityTypeBuilder<NoteRevision> b)
    {
        b.ToTable("note_revisions");
        b.HasKey(r => r.Id);
        b.HasOwner();
        b.Property(r => r.Title).HasMaxLength(Note.TitleMaxLength);
        b.Property(r => r.Content).HasMaxLength(Note.ContentMaxLength);
        b.ReferencesOwned<NoteRevision, Note>(nameof(NoteRevision.NoteId), DeleteBehavior.Cascade);
        b.HasIndex(r => new { r.NoteId, r.CreatedAt });
    }
}

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> b)
    {
        b.ToTable("journal_entries", t => t.HasCheckConstraint("ck_journal_entries_mood", "mood IS NULL OR mood BETWEEN 1 AND 5"));
        b.HasKey(j => j.Id);
        b.HasOwner();
        b.Property(j => j.Content).HasMaxLength(JournalEntry.ContentMaxLength);
        b.Property<NpgsqlTsVector>(SearchColumns.SearchVector)
            .HasComputedColumnSql("to_tsvector('simple'::regconfig, coalesce(content, ''))", stored: true);
        b.HasIndex(SearchColumns.SearchVector).HasMethod("GIN");
        b.HasIndex(j => new { j.OwnerId, j.EntryDate }).IsUnique();
        b.HasXminVersion();
    }
}
