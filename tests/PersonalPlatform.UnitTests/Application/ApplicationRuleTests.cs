using CsCheck;
using PersonalPlatform.Application.Auth;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Application.Export;
using PersonalPlatform.Application.Finance;
using PersonalPlatform.Contracts.Auth;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Contracts.Tasks;
using PersonalPlatform.Infrastructure.Persistence;
using C = PersonalPlatform.Contracts;
using D = PersonalPlatform.Domain;

namespace PersonalPlatform.UnitTests.Application;

public sealed class CsvTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("+1+1", "\"'+1+1\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\tcmd", "\"'\tcmd\"")]
    [InlineData("\r=1", "\"'\r=1\"")]
    [InlineData("|calc", "\"'|calc\"")]
    [InlineData("%0A", "\"'%0A\"")]
    [InlineData("Groceries", "\"Groceries\"")]
    [InlineData("a,b\nc", "\"a,b\nc\"")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Cells_are_quoted_and_formula_prefixes_neutralized(string? input, string expected) =>
        Assert.Equal(expected, Csv.Text(input));

    [Fact]
    public void Property_any_text_becomes_a_single_safe_quoted_field()
    {
        Gen.String.Sample(value =>
        {
            var cell = Csv.Text(value);
            if (string.IsNullOrEmpty(value))
            {
                Assert.Equal(string.Empty, cell);
                return;
            }

            Assert.StartsWith("\"", cell, StringComparison.Ordinal);
            Assert.EndsWith("\"", cell, StringComparison.Ordinal);
            var inner = cell[1..^1];
            Assert.DoesNotContain("\"", inner.Replace("\"\"", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.DoesNotMatch("^[=+\\-@\t\r|%]", inner);
        }, iter: 1000);
    }
}

public sealed class TextSearchTests
{
    [Theory]
    [InlineData("Купить молоко", "купить:* & молоко:*")]
    [InlineData("  report-2026 ", "report:* & 2026:*")]
    [InlineData("a & b | !c", "a:* & b:* & c:*")]
    [InlineData("'); DROP TABLE notes; --", "drop:* & table:* & notes:*")]
    [InlineData("::*:*", null)]
    [InlineData("", null)]
    public void User_input_is_reduced_to_safe_prefix_terms(string input, string? expected) =>
        Assert.Equal(expected, PostgresTextSearch.BuildPrefixQuery(input));

    [Fact]
    public void Property_query_contains_only_letters_digits_and_operators()
    {
        Gen.String[0, 200].Sample(input =>
        {
            var query = PostgresTextSearch.BuildPrefixQuery(input);
            if (query is null)
            {
                return;
            }

            foreach (var term in query.Split(" & "))
            {
                Assert.EndsWith(":*", term, StringComparison.Ordinal);
                Assert.All(term[..^2], c => Assert.True(char.IsLetterOrDigit(c)));
            }
        }, iter: 1000);
    }
}

public sealed class IdempotencyAndLockTests
{
    private static readonly CreateTransactionRequest Request = new(
        TransactionKind.Expense, new DateOnly(2026, 5, 1), "Coffee", Guid.Parse("0195f3a0-0000-7000-8000-00000000000a"), 3.50m, null, null, null);

    [Theory]
    [InlineData("0195f3a0-0000-7000-8000-000000000001", true)]
    [InlineData("abcDEF_-12", true)]
    [InlineData("short", false)]
    [InlineData("has space here", false)]
    [InlineData("ключ-идемпотентности", false)]
    [InlineData(null, false)]
    public void Idempotency_keys_are_bounded_ascii_tokens(string? key, bool valid) =>
        Assert.Equal(valid, IdempotencyKeys.IsValid(key));

    [Fact]
    public void Idempotency_keys_longer_than_64_characters_are_rejected() =>
        Assert.False(IdempotencyKeys.IsValid(new string('a', 65)));

    [Fact]
    public void Fingerprint_is_stable_and_sensitive_to_every_field()
    {
        var baseline = TransactionService.Fingerprint(Request);

        Assert.Equal(baseline, TransactionService.Fingerprint(Request with { }));
        Assert.NotEqual(baseline, TransactionService.Fingerprint(Request with { Amount = 3.51m }));
        Assert.NotEqual(baseline, TransactionService.Fingerprint(Request with { Description = "Tea" }));
        Assert.NotEqual(baseline, TransactionService.Fingerprint(Request with { OccurredOn = new DateOnly(2026, 5, 2) }));
        Assert.NotEqual(baseline, TransactionService.Fingerprint(Request with { AccountId = Guid.NewGuid() }));
        Assert.NotEqual(baseline, TransactionService.Fingerprint(Request with { Kind = TransactionKind.Income }));
    }

    [Fact]
    public void Advisory_lock_keys_are_scoped_per_user_and_purpose()
    {
        var user = Guid.NewGuid();

        Assert.Equal(AppDbContext.AdvisoryLockKey(user, "finance"), AppDbContext.AdvisoryLockKey(user, "finance"));
        Assert.NotEqual(AppDbContext.AdvisoryLockKey(user, "finance"), AppDbContext.AdvisoryLockKey(user, "recurring"));
        Assert.NotEqual(AppDbContext.AdvisoryLockKey(user, "finance"), AppDbContext.AdvisoryLockKey(Guid.NewGuid(), "finance"));
    }
}

public sealed class ValidatorTests
{
    private readonly CreateTransactionRequestValidator _transactions = new();
    private readonly RecurrenceDtoValidator _recurrence = new();
    private readonly RegisterRequestValidator _register = new();
    private readonly ChangePasswordRequestValidator _changePassword = new();

    [Fact]
    public void Transfer_requires_destination_and_forbids_category()
    {
        var transfer = new CreateTransactionRequest(TransactionKind.Transfer, new DateOnly(2026, 1, 1), null, Guid.NewGuid(), 10m, Guid.NewGuid(), null, null);

        var errors = _transactions.Validate(transfer).Errors.Select(e => e.PropertyName).ToList();

        Assert.Contains(nameof(CreateTransactionRequest.DestinationAccountId), errors);
        Assert.Contains(nameof(CreateTransactionRequest.CategoryId), errors);
    }

    [Fact]
    public void Expense_cannot_carry_transfer_fields_or_absurd_values()
    {
        var expense = new CreateTransactionRequest(TransactionKind.Expense, new DateOnly(1900, 1, 1), null, Guid.Empty, -5m, null, Guid.NewGuid(), 1m);

        var errors = _transactions.Validate(expense).Errors.Select(e => e.PropertyName).ToHashSet();

        Assert.Superset(new HashSet<string> { "OccurredOn", "AccountId", "Amount", "DestinationAccountId", "DestinationAmount" }, errors);
    }

    [Fact]
    public void Undefined_enum_values_are_rejected() =>
        Assert.False(_transactions.Validate(new CreateTransactionRequest((TransactionKind)42, new DateOnly(2026, 1, 1), null, Guid.NewGuid(), 1m, null, null, null)).IsValid);

    [Theory]
    [InlineData(0, 0, false)] // interval 0
    [InlineData(1, 0x80, false)] // unknown weekday bit
    [InlineData(1, 0x7F, true)]
    public void Recurrence_rules_are_validated(int interval, int days, bool valid) =>
        Assert.Equal(valid, _recurrence.Validate(new RecurrenceDto(RecurrenceFrequency.Weekly, interval, (Weekdays)days, new DateOnly(2026, 1, 1), null)).IsValid);

    [Fact]
    public void Recurrence_span_is_bounded_to_ten_years() =>
        Assert.False(_recurrence.Validate(new RecurrenceDto(RecurrenceFrequency.Daily, 1, Weekdays.None, new DateOnly(2026, 1, 1), new DateOnly(2040, 1, 1))).IsValid);

    [Theory]
    [InlineData("user@example.test", "correct horse battery", true)]
    [InlineData("not-an-email", "correct horse battery", false)]
    [InlineData("user@example.test", "short", false)]
    [InlineData("", "correct horse battery", false)]
    public void Registration_enforces_email_and_password_length(string email, string password, bool valid) =>
        Assert.Equal(valid, _register.Validate(new RegisterRequest(email, password, null, null, null)).IsValid);

    [Fact]
    public void Overlong_passwords_are_rejected_before_hashing() =>
        Assert.False(_register.Validate(new RegisterRequest("user@example.test", new string('p', 10_000), null, null, null)).IsValid);

    [Fact]
    public void New_password_must_differ_from_current() =>
        Assert.False(_changePassword.Validate(new ChangePasswordRequest("same password 123", "same password 123")).IsValid);
}

public sealed class EnumMappingTests
{
    public static TheoryData<Type, Type> EnumPairs => new()
    {
        { typeof(C.Tasks.TaskPriority), typeof(D.Tasks.TaskPriority) },
        { typeof(C.Tasks.TaskItemStatus), typeof(D.Tasks.TaskItemStatus) },
        { typeof(C.Tasks.RecurrenceFrequency), typeof(D.Scheduling.RecurrenceFrequency) },
        { typeof(C.Tasks.Weekdays), typeof(D.Scheduling.Weekdays) },
        { typeof(C.Organization.CategoryScope), typeof(D.Organization.CategoryScope) },
        { typeof(C.Habits.HabitScheduleType), typeof(D.Habits.HabitScheduleType) },
        { typeof(C.Finance.AccountType), typeof(D.Finance.AccountType) },
        { typeof(C.Finance.CategoryKind), typeof(D.Finance.CategoryKind) },
        { typeof(C.Finance.TransactionKind), typeof(D.Finance.TransactionKind) },
        { typeof(C.Finance.BudgetState), typeof(D.Finance.BudgetState) },
        { typeof(C.Profile.ThemePreference), typeof(D.Users.ThemePreference) },
        { typeof(C.Profile.DateFormatPreference), typeof(D.Users.DateFormatPreference) },
    };

    /// <summary>EnumMapping casts by value, so contract and domain enums must stay identical.</summary>
    [Theory]
    [MemberData(nameof(EnumPairs))]
    public void Contract_and_domain_enums_have_identical_names_and_values(Type contract, Type domain)
    {
        Assert.Equal(Describe(domain), Describe(contract));
    }

    /// <summary>For [Flags] enums only the atomic bits matter; composite helpers (e.g. WorkDays) are convenience aliases.</summary>
    private static List<(string Name, long Value)> Describe(Type enumType)
    {
        var isFlags = enumType.IsDefined(typeof(FlagsAttribute), false);
        return [.. Enum.GetNames(enumType)
            .Select(n => (Name: n, Value: Convert.ToInt64(Enum.Parse(enumType, n), System.Globalization.CultureInfo.InvariantCulture)))
            .Where(x => !isFlags || (x.Value & (x.Value - 1)) == 0)
            .OrderBy(x => x.Value)
            .ThenBy(x => x.Name, StringComparer.Ordinal)];
    }
}
