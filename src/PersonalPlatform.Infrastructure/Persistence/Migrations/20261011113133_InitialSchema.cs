using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace PersonalPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_account_email_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_password_reset_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "app",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    opening_balance = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    opening_date = table.Column<DateOnly>(type: "date", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.UniqueConstraint("ak_accounts_id_owner_id", x => new { x.id, x.owner_id });
                    table.UniqueConstraint("ak_accounts_id_owner_id_currency_code", x => new { x.id, x.owner_id, x.currency_code });
                    table.ForeignKey(
                        name: "fk_accounts_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    color = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.UniqueConstraint("ak_categories_id_owner_id", x => new { x.id, x.owner_id });
                    table.ForeignKey(
                        name: "fk_categories_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_categories",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finance_categories", x => x.id);
                    table.UniqueConstraint("ak_finance_categories_id_owner_id", x => new { x.id, x.owner_id });
                    table.CheckConstraint("ck_finance_categories_not_self_parent", "parent_id IS NULL OR parent_id <> id");
                    table.ForeignKey(
                        name: "fk_finance_categories_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_finance_categories_finance_categories_parent_id_owner_id",
                        columns: x => new { x.parent_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "finance_categories",
                        principalColumns: new[] { "id", "owner_id" });
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    content = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    mood = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple'::regconfig, coalesce(content, ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                    table.CheckConstraint("ck_journal_entries_mood", "mood IS NULL OR mood BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_journal_entries_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notes",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple'::regconfig, coalesce(title, '') || ' ' || coalesce(content, ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notes", x => x.id);
                    table.UniqueConstraint("ak_notes_id_owner_id", x => new { x.id, x.owner_id });
                    table.ForeignKey(
                        name: "fk_notes_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                    table.UniqueConstraint("ak_tags_id_owner_id", x => new { x.id, x.owner_id });
                    table.ForeignKey(
                        name: "fk_tags_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "app",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                schema: "app",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    date_format = table.Column<int>(type: "integer", nullable: false),
                    first_day_of_week = table.Column<int>(type: "integer", nullable: false),
                    allow_overlapping_time_blocks = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profiles", x => x.owner_id);
                    table.ForeignKey(
                        name: "fk_user_profiles_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "app",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<int>(type: "integer", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "habits",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    color = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    schedule_type = table.Column<int>(type: "integer", nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    interval_days = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    target_streak = table.Column<int>(type: "integer", nullable: true),
                    reminder_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_habits", x => x.id);
                    table.UniqueConstraint("ak_habits_id_owner_id", x => new { x.id, x.owner_id });
                    table.CheckConstraint("ck_habits_interval_days", "interval_days BETWEEN 1 AND 365");
                    table.CheckConstraint("ck_habits_target_streak", "target_streak IS NULL OR target_streak BETWEEN 1 AND 3650");
                    table.ForeignKey(
                        name: "fk_habits_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_habits_categories_category_id_owner_id",
                        columns: x => new { x.category_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "categories",
                        principalColumns: new[] { "id", "owner_id" });
                });

            migrationBuilder.CreateTable(
                name: "task_series",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    frequency = table.Column<int>(type: "integer", nullable: false),
                    interval = table.Column<int>(type: "integer", nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_series", x => x.id);
                    table.UniqueConstraint("ak_task_series_id_owner_id", x => new { x.id, x.owner_id });
                    table.CheckConstraint("ck_task_series_interval", "\"interval\" BETWEEN 1 AND 365");
                    table.ForeignKey(
                        name: "fk_task_series_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_series_categories_category_id_owner_id",
                        columns: x => new { x.category_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "categories",
                        principalColumns: new[] { "id", "owner_id" });
                });

            migrationBuilder.CreateTable(
                name: "budgets",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    monthly_limit = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    warning_threshold_percent = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budgets", x => x.id);
                    table.CheckConstraint("ck_budgets_limit_positive", "monthly_limit > 0");
                    table.CheckConstraint("ck_budgets_threshold", "warning_threshold_percent BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "fk_budgets_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_budgets_finance_categories_category_id_owner_id",
                        columns: x => new { x.category_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "finance_categories",
                        principalColumns: new[] { "id", "owner_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recurring_transactions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    frequency = table.Column<int>(type: "integer", nullable: false),
                    interval = table.Column<int>(type: "integer", nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    next_occurrence = table.Column<DateOnly>(type: "date", nullable: true),
                    is_paused = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_transactions", x => x.id);
                    table.UniqueConstraint("ak_recurring_transactions_id_owner_id", x => new { x.id, x.owner_id });
                    table.CheckConstraint("ck_recurring_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_recurring_interval", "\"interval\" BETWEEN 1 AND 365");
                    table.ForeignKey(
                        name: "fk_recurring_transactions_accounts_account_id_owner_id",
                        columns: x => new { x.account_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "accounts",
                        principalColumns: new[] { "id", "owner_id" });
                    table.ForeignKey(
                        name: "fk_recurring_transactions_accounts_destination_account_id_owne",
                        columns: x => new { x.destination_account_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "accounts",
                        principalColumns: new[] { "id", "owner_id" });
                    table.ForeignKey(
                        name: "fk_recurring_transactions_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recurring_transactions_finance_categories_category_id_owner",
                        columns: x => new { x.category_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "finance_categories",
                        principalColumns: new[] { "id", "owner_id" });
                });

            migrationBuilder.CreateTable(
                name: "note_revisions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_note_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_note_revisions_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_note_revisions_notes_note_id_owner_id",
                        columns: x => new { x.note_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "notes",
                        principalColumns: new[] { "id", "owner_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "note_tags",
                schema: "app",
                columns: table => new
                {
                    note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_note_tags", x => new { x.note_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_note_tags_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_note_tags_notes_note_id",
                        column: x => x.note_id,
                        principalSchema: "app",
                        principalTable: "notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_note_tags_tags_tag_id_owner_id",
                        columns: x => new { x.tag_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "tags",
                        principalColumns: new[] { "id", "owner_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_user_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "app",
                        principalTable: "user_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "habit_completions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    habit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_habit_completions", x => x.id);
                    table.ForeignKey(
                        name: "fk_habit_completions_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_habit_completions_habits_habit_id_owner_id",
                        columns: x => new { x.habit_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "habits",
                        principalColumns: new[] { "id", "owner_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    reminder_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminder_dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurrence_date = table.Column<DateOnly>(type: "date", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.UniqueConstraint("ak_tasks_id_owner_id", x => new { x.id, x.owner_id });
                    table.CheckConstraint("ck_tasks_completed_at_matches_status", "(status = 2) = (completed_at IS NOT NULL)");
                    table.CheckConstraint("ck_tasks_due_time_requires_date", "due_time IS NULL OR due_date IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_tasks_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tasks_categories_category_id_owner_id",
                        columns: x => new { x.category_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "categories",
                        principalColumns: new[] { "id", "owner_id" });
                    table.ForeignKey(
                        name: "fk_tasks_task_series_series_id_owner_id",
                        columns: x => new { x.series_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "task_series",
                        principalColumns: new[] { "id", "owner_id" });
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    destination_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    destination_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    recurring_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recurrence_date = table.Column<DateOnly>(type: "date", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                    table.UniqueConstraint("ak_transactions_id_owner_id", x => new { x.id, x.owner_id });
                    table.CheckConstraint("ck_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_transactions_destination_amount_positive", "destination_amount IS NULL OR destination_amount > 0");
                    table.CheckConstraint("ck_transactions_transfer_shape", "(kind = 2 AND destination_account_id IS NOT NULL AND destination_amount IS NOT NULL AND category_id IS NULL AND destination_account_id <> account_id) OR (kind <> 2 AND destination_account_id IS NULL AND destination_amount IS NULL)");
                    table.ForeignKey(
                        name: "fk_transactions_accounts_account_id_owner_id",
                        columns: x => new { x.account_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "accounts",
                        principalColumns: new[] { "id", "owner_id" });
                    table.ForeignKey(
                        name: "fk_transactions_accounts_destination_account_id_owner_id",
                        columns: x => new { x.destination_account_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "accounts",
                        principalColumns: new[] { "id", "owner_id" });
                    table.ForeignKey(
                        name: "fk_transactions_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transactions_finance_categories_category_id_owner_id",
                        columns: x => new { x.category_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "finance_categories",
                        principalColumns: new[] { "id", "owner_id" });
                    table.ForeignKey(
                        name: "fk_transactions_recurring_transactions_recurring_transaction_i",
                        columns: x => new { x.recurring_transaction_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "recurring_transactions",
                        principalColumns: new[] { "id", "owner_id" });
                });

            migrationBuilder.CreateTable(
                name: "subtasks",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subtasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_subtasks_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subtasks_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "app",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_tags",
                schema: "app",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_tags", x => new { x.task_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_task_tags_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_tags_tags_tag_id_owner_id",
                        columns: x => new { x.tag_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "tags",
                        principalColumns: new[] { "id", "owner_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_tags_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "app",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "time_blocks",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_blocks", x => x.id);
                    table.CheckConstraint("ck_time_blocks_interval", "ends_at > starts_at AND ends_at - starts_at <= interval '24 hours'");
                    table.ForeignKey(
                        name: "fk_time_blocks_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_time_blocks_tasks_task_id_owner_id",
                        columns: x => new { x.task_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "tasks",
                        principalColumns: new[] { "id", "owner_id" });
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount_nonzero", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_ledger_entries_accounts_account_id_owner_id_currency_code",
                        columns: x => new { x.account_id, x.owner_id, x.currency_code },
                        principalSchema: "app",
                        principalTable: "accounts",
                        principalColumns: new[] { "id", "owner_id", "currency_code" });
                    table.ForeignKey(
                        name: "fk_ledger_entries_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ledger_entries_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalSchema: "app",
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "app",
                table: "roles",
                columns: new[] { "id", "concurrency_stamp", "name", "normalized_name" },
                values: new object[] { new Guid("0195f3a0-0000-7000-8000-000000000001"), "4c1d7b4e-6f0e-4b8a-9a36-3d4a7f9a2c11", "admin", "ADMIN" });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_owner_id_name",
                schema: "app",
                table: "accounts",
                columns: new[] { "owner_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_budgets_category_id_owner_id",
                schema: "app",
                table: "budgets",
                columns: new[] { "category_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_budgets_owner_id_category_id_currency_code",
                schema: "app",
                table: "budgets",
                columns: new[] { "owner_id", "category_id", "currency_code" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_categories_owner_id_scope_name",
                schema: "app",
                table: "categories",
                columns: new[] { "owner_id", "scope", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_finance_categories_owner_id_kind_parent_id_name",
                schema: "app",
                table: "finance_categories",
                columns: new[] { "owner_id", "kind", "parent_id", "name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_finance_categories_parent_id_owner_id",
                schema: "app",
                table: "finance_categories",
                columns: new[] { "parent_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_habit_completions_habit_id_date",
                schema: "app",
                table: "habit_completions",
                columns: new[] { "habit_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_habit_completions_habit_id_owner_id",
                schema: "app",
                table: "habit_completions",
                columns: new[] { "habit_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_habit_completions_owner_id_date",
                schema: "app",
                table: "habit_completions",
                columns: new[] { "owner_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_habits_category_id_owner_id",
                schema: "app",
                table: "habits",
                columns: new[] { "category_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_habits_owner_id_archived_at",
                schema: "app",
                table: "habits",
                columns: new[] { "owner_id", "archived_at" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_owner_id_entry_date",
                schema: "app",
                table: "journal_entries",
                columns: new[] { "owner_id", "entry_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_search_vector",
                schema: "app",
                table: "journal_entries",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account_id_occurred_on",
                schema: "app",
                table: "ledger_entries",
                columns: new[] { "account_id", "occurred_on" })
                .Annotation("Npgsql:IndexInclude", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account_id_owner_id_currency_code",
                schema: "app",
                table: "ledger_entries",
                columns: new[] { "account_id", "owner_id", "currency_code" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_owner_id_occurred_on",
                schema: "app",
                table: "ledger_entries",
                columns: new[] { "owner_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_transaction_id",
                schema: "app",
                table: "ledger_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_note_revisions_note_id_created_at",
                schema: "app",
                table: "note_revisions",
                columns: new[] { "note_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_note_revisions_note_id_owner_id",
                schema: "app",
                table: "note_revisions",
                columns: new[] { "note_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_note_revisions_owner_id",
                schema: "app",
                table: "note_revisions",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_note_tags_owner_id",
                schema: "app",
                table: "note_tags",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_note_tags_tag_id",
                schema: "app",
                table: "note_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_note_tags_tag_id_owner_id",
                schema: "app",
                table: "note_tags",
                columns: new[] { "tag_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notes_owner_id_archived_at_is_pinned_updated_at",
                schema: "app",
                table: "notes",
                columns: new[] { "owner_id", "archived_at", "is_pinned", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notes_search_vector",
                schema: "app",
                table: "notes",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_account_id_owner_id",
                schema: "app",
                table: "recurring_transactions",
                columns: new[] { "account_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_category_id_owner_id",
                schema: "app",
                table: "recurring_transactions",
                columns: new[] { "category_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_destination_account_id_owner_id",
                schema: "app",
                table: "recurring_transactions",
                columns: new[] { "destination_account_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_transactions_owner_id_next_occurrence",
                schema: "app",
                table: "recurring_transactions",
                columns: new[] { "owner_id", "next_occurrence" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expires_at",
                schema: "app",
                table: "refresh_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_session_id",
                schema: "app",
                table: "refresh_tokens",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "app",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                schema: "app",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "app",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subtasks_owner_id",
                schema: "app",
                table: "subtasks",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_subtasks_task_id_position",
                schema: "app",
                table: "subtasks",
                columns: new[] { "task_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_tags_owner_id_normalized_name",
                schema: "app",
                table: "tags",
                columns: new[] { "owner_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_series_category_id_owner_id",
                schema: "app",
                table: "task_series",
                columns: new[] { "category_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_series_owner_id",
                schema: "app",
                table: "task_series",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_tags_owner_id",
                schema: "app",
                table: "task_tags",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_tags_tag_id",
                schema: "app",
                table: "task_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_tags_tag_id_owner_id",
                schema: "app",
                table: "task_tags",
                columns: new[] { "tag_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_category_id_owner_id",
                schema: "app",
                table: "tasks",
                columns: new[] { "category_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_owner_id_reminder_at",
                schema: "app",
                table: "tasks",
                columns: new[] { "owner_id", "reminder_at" },
                filter: "reminder_at IS NOT NULL AND reminder_dismissed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_owner_id_status_due_date",
                schema: "app",
                table: "tasks",
                columns: new[] { "owner_id", "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_series_id_occurrence_date",
                schema: "app",
                table: "tasks",
                columns: new[] { "series_id", "occurrence_date" },
                unique: true,
                filter: "series_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_series_id_owner_id",
                schema: "app",
                table: "tasks",
                columns: new[] { "series_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_time_blocks_owner_id_starts_at",
                schema: "app",
                table: "time_blocks",
                columns: new[] { "owner_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_time_blocks_task_id",
                schema: "app",
                table: "time_blocks",
                column: "task_id",
                filter: "task_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_time_blocks_task_id_owner_id",
                schema: "app",
                table: "time_blocks",
                columns: new[] { "task_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_account_id_owner_id",
                schema: "app",
                table: "transactions",
                columns: new[] { "account_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_category_id_owner_id",
                schema: "app",
                table: "transactions",
                columns: new[] { "category_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_destination_account_id_owner_id",
                schema: "app",
                table: "transactions",
                columns: new[] { "destination_account_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_owner_id_category_id",
                schema: "app",
                table: "transactions",
                columns: new[] { "owner_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_owner_id_idempotency_key",
                schema: "app",
                table: "transactions",
                columns: new[] { "owner_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_owner_id_occurred_on",
                schema: "app",
                table: "transactions",
                columns: new[] { "owner_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_recurring_transaction_id_owner_id",
                schema: "app",
                table: "transactions",
                columns: new[] { "recurring_transaction_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_recurring_transaction_id_recurrence_date",
                schema: "app",
                table: "transactions",
                columns: new[] { "recurring_transaction_id", "recurrence_date" },
                unique: true,
                filter: "recurring_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                schema: "app",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                schema: "app",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                schema: "app",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_expires_at",
                schema: "app",
                table: "user_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id_revoked_at_expires_at",
                schema: "app",
                table: "user_sessions",
                columns: new[] { "user_id", "revoked_at", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "app",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "app",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budgets",
                schema: "app");

            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "app");

            migrationBuilder.DropTable(
                name: "habit_completions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "journal_entries",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "app");

            migrationBuilder.DropTable(
                name: "note_revisions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "note_tags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "app");

            migrationBuilder.DropTable(
                name: "role_claims",
                schema: "app");

            migrationBuilder.DropTable(
                name: "subtasks",
                schema: "app");

            migrationBuilder.DropTable(
                name: "task_tags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "time_blocks",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_profiles",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "app");

            migrationBuilder.DropTable(
                name: "habits",
                schema: "app");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "notes",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_sessions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "tasks",
                schema: "app");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "app");

            migrationBuilder.DropTable(
                name: "recurring_transactions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "task_series",
                schema: "app");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "app");

            migrationBuilder.DropTable(
                name: "finance_categories",
                schema: "app");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "app");

            migrationBuilder.DropTable(
                name: "users",
                schema: "app");
        }
    }
}
