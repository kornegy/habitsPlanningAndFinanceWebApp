using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalPlatform.Domain.Organization;
using PersonalPlatform.Domain.Tasks;

namespace PersonalPlatform.Infrastructure.Persistence.Configurations;

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> b)
    {
        b.ToTable("tasks", t =>
        {
            t.HasCheckConstraint("ck_tasks_due_time_requires_date", "due_time IS NULL OR due_date IS NOT NULL");
            t.HasCheckConstraint("ck_tasks_completed_at_matches_status", "(status = 2) = (completed_at IS NOT NULL)");
        });
        b.HasKey(t => t.Id);
        b.HasOwner();
        b.HasOwnerScopedKey();
        b.Property(t => t.Title).HasMaxLength(TaskItem.TitleMaxLength);
        b.Property(t => t.Description).HasMaxLength(TaskItem.DescriptionMaxLength);
        b.ReferencesOwned<TaskItem, Category>(nameof(TaskItem.CategoryId));
        b.ReferencesOwned<TaskItem, TaskSeries>(nameof(TaskItem.SeriesId));

        b.HasMany(t => t.Subtasks).WithOne().HasForeignKey(s => s.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(t => t.Subtasks).HasField("_subtasks");
        b.HasMany(t => t.Tags).WithOne().HasForeignKey(tt => tt.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(t => t.Tags).HasField("_tags");

        // List views: by owner + status + due date (today / upcoming / overdue / completed).
        b.HasIndex(t => new { t.OwnerId, t.Status, t.DueDate });
        b.HasIndex(t => new { t.OwnerId, t.ReminderAt }).HasFilter("reminder_at IS NOT NULL AND reminder_dismissed_at IS NULL");
        // A series occurrence is materialised at most once.
        b.HasIndex(t => new { t.SeriesId, t.OccurrenceDate }).IsUnique().HasFilter("series_id IS NOT NULL");
        b.HasXminVersion();
    }
}

internal sealed class SubtaskConfiguration : IEntityTypeConfiguration<Subtask>
{
    public void Configure(EntityTypeBuilder<Subtask> b)
    {
        b.ToTable("subtasks");
        b.HasKey(s => s.Id);
        b.HasOwner();
        b.Property(s => s.Title).HasMaxLength(Subtask.TitleMaxLength);
        b.HasIndex(s => new { s.TaskId, s.Position });
    }
}

internal sealed class TaskTagConfiguration : IEntityTypeConfiguration<TaskTag>
{
    public void Configure(EntityTypeBuilder<TaskTag> b)
    {
        b.ToTable("task_tags");
        b.HasKey(t => new { t.TaskId, t.TagId });
        b.HasOwner();
        b.ReferencesOwned<TaskTag, Tag>(nameof(TaskTag.TagId), DeleteBehavior.Cascade);
        b.HasIndex(t => t.TagId);
    }
}

internal sealed class TaskSeriesConfiguration : IEntityTypeConfiguration<TaskSeries>
{
    public void Configure(EntityTypeBuilder<TaskSeries> b)
    {
        b.ToTable("task_series", t => t.HasCheckConstraint("ck_task_series_interval", "\"interval\" BETWEEN 1 AND 365"));
        b.HasKey(s => s.Id);
        b.HasOwner();
        b.HasOwnerScopedKey();
        b.Property(s => s.Title).HasMaxLength(TaskItem.TitleMaxLength);
        b.Property(s => s.Description).HasMaxLength(TaskItem.DescriptionMaxLength);
        b.Ignore(s => s.Pattern);
        b.ReferencesOwned<TaskSeries, Category>(nameof(TaskSeries.CategoryId));
        b.HasIndex(s => s.OwnerId);
        b.HasXminVersion();
    }
}
