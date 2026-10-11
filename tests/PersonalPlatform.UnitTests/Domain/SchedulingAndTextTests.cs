using CsCheck;
using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Notes;
using PersonalPlatform.Domain.Organization;
using PersonalPlatform.Domain.Planner;
using PersonalPlatform.Domain.Tasks;
using PersonalPlatform.Domain.Users;

namespace PersonalPlatform.UnitTests.Domain;

public sealed class TimeZoneTests
{
    private static readonly TimeZoneInfo Berlin = TimeZones.Find("Europe/Berlin");
    private static readonly TimeZoneInfo NewYork = TimeZones.Find("America/New_York");

    [Theory]
    [InlineData("Europe/Moscow", true)]
    [InlineData("America/New_York", true)]
    [InlineData("UTC", true)]
    [InlineData("Not/AZone", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("../../etc/passwd", false)]
    public void Only_known_iana_zones_are_supported(string? id, bool expected) =>
        Assert.Equal(expected, TimeZones.IsSupported(id));

    [Fact]
    public void Time_inside_a_spring_forward_gap_moves_to_the_first_valid_instant()
    {
        // 2026-03-29 02:30 does not exist in Berlin (clocks jump from 02:00 to 03:00 CEST = 01:00Z).
        var instant = TimeZones.ToUtc(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), Berlin);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), instant);
    }

    [Fact]
    public void Ambiguous_fall_back_time_resolves_to_the_earlier_instant()
    {
        // 2026-10-25 02:30 happens twice in Berlin; the first one is still CEST (+02:00).
        var instant = TimeZones.ToUtc(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), Berlin);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), instant);
    }

    [Fact]
    public void Day_boundaries_follow_the_user_zone_not_utc()
    {
        var lateEvening = new DateTimeOffset(2026, 7, 1, 2, 0, 0, TimeSpan.Zero); // 22:00 on June 30 in New York

        Assert.Equal(new DateOnly(2026, 6, 30), TimeZones.LocalDate(lateEvening, NewYork));
        Assert.Equal(new DateOnly(2026, 7, 1), TimeZones.LocalDate(lateEvening, TimeZoneInfo.Utc));
        Assert.Equal(new DateTimeOffset(2026, 6, 30, 4, 0, 0, TimeSpan.Zero), TimeZones.StartOfDayUtc(new DateOnly(2026, 6, 30), NewYork));
    }

    [Fact]
    public void Property_local_date_round_trips_through_start_of_day()
    {
        var zones = new[] { Berlin, NewYork, TimeZones.Find("Asia/Kolkata"), TimeZones.Find("Pacific/Chatham"), TimeZoneInfo.Utc };

        Gen.Select(Gen.Int[0, zones.Length - 1], Gen.Int[0, 3650]).Sample((zoneIndex, offset) =>
        {
            var zone = zones[zoneIndex];
            var date = new DateOnly(2024, 1, 1).AddDays(offset);

            Assert.Equal(date, TimeZones.LocalDate(TimeZones.StartOfDayUtc(date, zone), zone));
        }, iter: 500);
    }

    [Fact]
    public void Profile_rejects_unsupported_settings()
    {
        var profile = new UserProfile(Guid.NewGuid(), "Test", "UTC", "ru-RU", "RUB", DateTimeOffset.UtcNow);

        Assert.Equal("invalid_time_zone", Code(() => Update(profile, zone: "Mars/Olympus")));
        Assert.Equal("invalid_locale", Code(() => Update(profile, locale: "xx-XX")));
        Assert.Equal("invalid_currency", Code(() => Update(profile, currency: "BTC")));
        Assert.Equal("invalid_first_day", Code(() => Update(profile, firstDay: DayOfWeek.Wednesday)));
        Assert.Equal("invalid_date_format", Code(() => Update(profile, format: (DateFormatPreference)99)));

        Update(profile, zone: "Asia/Tokyo", locale: "en-US", currency: "JPY", firstDay: DayOfWeek.Sunday);
        Assert.Equal("Asia/Tokyo", profile.TimeZoneId);
        Assert.Equal("JPY", profile.CurrencyCode);
    }

    private static void Update(UserProfile profile, string zone = "UTC", string locale = "ru-RU", string currency = "RUB",
        DayOfWeek firstDay = DayOfWeek.Monday, DateFormatPreference format = default) =>
        profile.Update("Test", zone, locale, currency, format, firstDay, false, DateTimeOffset.UtcNow);

    private static string Code(Action action) => Assert.Throws<DomainRuleException>(action).Code;
}

public sealed class PlannerAndTextTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 60, 60, 120, false)] // touching edges do not overlap (half-open intervals)
    [InlineData(0, 61, 60, 120, true)]
    [InlineData(10, 20, 0, 120, true)] // containment
    [InlineData(0, 30, 31, 40, false)]
    public void Overlap_uses_half_open_intervals(int aStart, int aEnd, int bStart, int bEnd, bool expected)
    {
        Assert.Equal(expected, TimeBlock.Overlap(T0.AddMinutes(aStart), T0.AddMinutes(aEnd), T0.AddMinutes(bStart), T0.AddMinutes(bEnd)));
        Assert.Equal(expected, TimeBlock.Overlap(T0.AddMinutes(bStart), T0.AddMinutes(bEnd), T0.AddMinutes(aStart), T0.AddMinutes(aEnd)));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(24 * 60 + 1)]
    public void Blocks_shorter_than_five_minutes_or_longer_than_a_day_are_rejected(int minutes)
    {
        var block = new TimeBlock(Guid.NewGuid(), T0);

        Assert.Equal("invalid_interval", Assert.Throws<DomainRuleException>(() => block.Update("Focus", null, T0, T0.AddMinutes(minutes), null, T0)).Code);
    }

    [Fact]
    public void Blocks_are_stored_in_utc()
    {
        var block = new TimeBlock(Guid.NewGuid(), T0);
        var local = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.FromHours(3));

        block.Update("  Focus  ", null, local, local.AddHours(1), null, T0);

        Assert.Equal(TimeSpan.Zero, block.StartsAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero), block.StartsAt);
        Assert.Equal("Focus", block.Title);
    }

    [Fact]
    public void Note_edits_create_revisions_only_for_real_changes()
    {
        var note = new Note(Guid.NewGuid(), T0);

        Assert.Null(note.Edit("Title", "first", T0)); // nothing to keep yet
        Assert.Null(note.Edit("Title", "first", T0)); // unchanged
        var revision = note.Edit("Title", "second", T0.AddMinutes(1));

        Assert.NotNull(revision);
        Assert.Equal("first", revision.Content);
        Assert.Equal("second", note.Content);
        Assert.Equal("content_too_long", Assert.Throws<DomainRuleException>(() => note.Edit(null, new string('x', Note.ContentMaxLength + 1), T0)).Code);
    }

    [Fact]
    public void Archiving_a_note_unpins_it()
    {
        var note = new Note(Guid.NewGuid(), T0);
        note.SetPinned(true, T0);

        note.Archive(T0);

        Assert.True(note.IsArchived);
        Assert.False(note.IsPinned);
    }

    [Fact]
    public void Tags_from_another_owner_cannot_be_attached()
    {
        var owner = Guid.NewGuid();
        var note = new Note(owner, T0);
        var task = new TaskItem(owner, T0);
        var foreignTag = new Tag(Guid.NewGuid(), "work", T0);

        Assert.Equal("tag_not_found", Assert.Throws<DomainRuleException>(() => note.ReplaceTags([foreignTag])).Code);
        Assert.Equal("tag_not_found", Assert.Throws<DomainRuleException>(() => task.ReplaceTags([foreignTag])).Code);
    }

    [Fact]
    public void Tag_names_are_trimmed_and_compared_case_insensitively()
    {
        var tag = new Tag(Guid.NewGuid(), "  Работа ", T0);

        Assert.Equal("Работа", tag.Name);
        Assert.Equal("работа", tag.NormalizedName);
        Assert.Throws<DomainRuleException>(() => new Tag(Guid.NewGuid(), "   ", T0));
        Assert.Throws<DomainRuleException>(() => new Tag(Guid.NewGuid(), new string('a', Tag.NameMaxLength + 1), T0));
    }

    [Theory]
    [InlineData("#a1b2c3", true)]
    [InlineData("#A1B2C3", true)]
    [InlineData("#a1b2c", false)]
    [InlineData("a1b2c3f", false)]
    [InlineData("#g1b2c3", false)]
    [InlineData("red", false)]
    [InlineData("#fff;}", false)]
    [InlineData("url(x)", false)]
    public void Colors_must_be_strict_hex(string value, bool valid)
    {
        Assert.Equal(valid, ColorHex.IsValid(value));
        if (valid)
        {
            Assert.Equal(value.ToUpperInvariant(), ColorHex.Normalize(value));
        }
        else
        {
            Assert.Throws<DomainRuleException>(() => ColorHex.Normalize(value));
        }
    }

    [Fact]
    public void Task_status_tracks_completion_time_and_due_time_needs_a_date()
    {
        var task = new TaskItem(Guid.NewGuid(), T0);
        task.SetDetails("Pay bills", null, TaskPriority.High, null, T0);

        task.SetStatus(TaskItemStatus.Done, T0.AddHours(1));
        Assert.Equal(T0.AddHours(1), task.CompletedAt);
        task.SetStatus(TaskItemStatus.Open, T0.AddHours(2));
        Assert.Null(task.CompletedAt);

        Assert.Equal("due_time_without_date", Assert.Throws<DomainRuleException>(() => task.Schedule(null, new TimeOnly(9, 0), null, T0)).Code);
        Assert.Equal("too_many_subtasks", Assert.Throws<DomainRuleException>(() =>
            task.ReplaceSubtasks(Enumerable.Range(0, TaskItem.MaxSubtasks + 1).Select(i => ($"s{i}", false)))).Code);
    }
}
