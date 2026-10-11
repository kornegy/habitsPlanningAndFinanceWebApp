using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalPlatform.Domain.Habits;
using PersonalPlatform.Domain.Organization;
using PersonalPlatform.Domain.Planner;
using PersonalPlatform.Domain.Tasks;

namespace PersonalPlatform.Infrastructure.Persistence.Configurations;

internal sealed class HabitConfiguration : IEntityTypeConfiguration<Habit>
{
    public void Configure(EntityTypeBuilder<Habit> b)
    {
        b.ToTable("habits", t =>
        {
            t.HasCheckConstraint("ck_habits_interval_days", "interval_days BETWEEN 1 AND 365");
            t.HasCheckConstraint("ck_habits_target_streak", "target_streak IS NULL OR target_streak BETWEEN 1 AND 3650");
        });
        b.HasKey(h => h.Id);
        b.HasOwner();
        b.HasOwnerScopedKey();
        b.Property(h => h.Name).HasMaxLength(Habit.NameMaxLength);
        b.Property(h => h.Description).HasMaxLength(Habit.DescriptionMaxLength);
        b.Property(h => h.Color).HasMaxLength(7).IsFixedLength();
        b.Ignore(h => h.Schedule);
        b.ReferencesOwned<Habit, Category>(nameof(Habit.CategoryId));
        b.HasIndex(h => new { h.OwnerId, h.ArchivedAt });
        b.HasXminVersion();
    }
}

internal sealed class HabitCompletionConfiguration : IEntityTypeConfiguration<HabitCompletion>
{
    public void Configure(EntityTypeBuilder<HabitCompletion> b)
    {
        b.ToTable("habit_completions");
        b.HasKey(c => c.Id);
        b.HasOwner();
        b.ReferencesOwned<HabitCompletion, Habit>(nameof(HabitCompletion.HabitId), DeleteBehavior.Cascade);
        // One completion per habit per local date: double check-offs are impossible even under races.
        b.HasIndex(c => new { c.HabitId, c.Date }).IsUnique();
        b.HasIndex(c => new { c.OwnerId, c.Date });
    }
}

internal sealed class TimeBlockConfiguration : IEntityTypeConfiguration<TimeBlock>
{
    public void Configure(EntityTypeBuilder<TimeBlock> b)
    {
        b.ToTable("time_blocks", t =>
        {
            t.HasCheckConstraint("ck_time_blocks_interval", "ends_at > starts_at AND ends_at - starts_at <= interval '24 hours'");
        });
        b.HasKey(t => t.Id);
        b.HasOwner();
        b.Property(t => t.Title).HasMaxLength(TimeBlock.TitleMaxLength);
        b.Property(t => t.Notes).HasMaxLength(TimeBlock.NotesMaxLength);
        b.ReferencesOwned<TimeBlock, TaskItem>(nameof(TimeBlock.TaskId));
        // Range and overlap queries: owner_id = @u AND starts_at < @to AND ends_at > @from.
        b.HasIndex(t => new { t.OwnerId, t.StartsAt });
        b.HasIndex(t => t.TaskId).HasFilter("task_id IS NOT NULL");
        b.HasXminVersion();
    }
}
