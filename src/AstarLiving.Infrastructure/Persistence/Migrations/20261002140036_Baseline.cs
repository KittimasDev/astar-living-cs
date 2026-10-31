using System;
using AstarLiving.Domain.Enums;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AstarLiving.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:gv_collection_notices_notices_type_enum", "FINE,COLLECTION")
                .Annotation("Npgsql:Enum:gv_collection_notices_status_enum", "PENDING,PAID,WAITING,CANCELLED")
                .Annotation("Npgsql:Enum:gv_payments_status_enum", "PAID,NOT_PAY,PARTIAL_PAY,CANCELLED")
                .Annotation("Npgsql:Enum:gv_users_role_enum", "SUPERADMIN,ADMIN,MANAGER,USER");

            migrationBuilder.CreateTable(
                name: "gv_activity_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    project_id = table.Column<int>(type: "integer", nullable: true),
                    action = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    target_table = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    target_id = table.Column<int>(type: "integer", nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_5aabfbe1395ec63ea9e2cb1f95a", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gv_payment_methods",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_d15a6475d9f1be58a50939e4614", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gv_projects",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    address = table.Column<string>(type: "text", nullable: true),
                    contact_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    contact_fax_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    project_logo = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_0b11139334c1e8df3dd446cb20a", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gv_notice_template",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    topic_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    introduction_detail = table.Column<string>(type: "text", nullable: true),
                    additional_detail = table.Column<string>(type: "text", nullable: true),
                    closure_text = table.Column<string>(type: "text", nullable: true),
                    closing_word = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    remark = table.Column<string>(type: "text", nullable: true),
                    project_id = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_949711edd0d5144c891180b423f", x => x.id);
                    table.ForeignKey(
                        name: "FK_ac0706a08a505b9e18a2c3aa87f",
                        column: x => x.project_id,
                        principalTable: "gv_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gv_residents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<int>(type: "integer", nullable: false),
                    house_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ownership_ratio = table.Column<decimal>(type: "numeric", nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gv_residents", x => x.id);
                    table.ForeignKey(
                        name: "FK_gv_residents_gv_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "gv_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gv_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<int>(type: "integer", nullable: false),
                    maintenance_fee_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    fine_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    collection_fee = table.Column<decimal>(type: "numeric", nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    invoice_prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    receipt_prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    invoice_note = table.Column<string>(type: "text", nullable: true),
                    receipt_note = table.Column<string>(type: "text", nullable: true),
                    discount_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gv_settings", x => x.id);
                    table.ForeignKey(
                        name: "FK_gv_settings_gv_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "gv_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gv_users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    role = table.Column<UserRole>(type: "gv_users_role_enum", nullable: false),
                    project_id = table.Column<int>(type: "integer", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gv_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_gv_users_gv_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "gv_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gv_receipts_revenues",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<int>(type: "integer", nullable: false),
                    receipt_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    resident_id = table.Column<int>(type: "integer", nullable: false),
                    payment_detail = table.Column<string>(type: "jsonb", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(15,2)", nullable: false),
                    payment_method_id = table.Column<int>(type: "integer", nullable: false),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cheque_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    rejected_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp", nullable: true),
                    updated_by = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp", nullable: true),
                    deleted_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gv_receipts_revenues", x => x.id);
                    table.ForeignKey(
                        name: "FK_gv_receipts_revenues_gv_payment_methods_payment_method_id",
                        column: x => x.payment_method_id,
                        principalTable: "gv_payment_methods",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_gv_receipts_revenues_gv_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "gv_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_gv_receipts_revenues_gv_residents_resident_id",
                        column: x => x.resident_id,
                        principalTable: "gv_residents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gv_receipts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<int>(type: "integer", nullable: false),
                    receipt_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    resident_id = table.Column<int>(type: "integer", nullable: false),
                    payment_ids = table.Column<string>(type: "json", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    payment_method_id = table.Column<int>(type: "integer", nullable: true),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cheque_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    rejected_reason = table.Column<string>(type: "text", nullable: true),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    have_discount = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    discount_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_59faac42efbb23216c4e855208a", x => x.id);
                    table.ForeignKey(
                        name: "FK_08721186aebed5c9224ca2995ba",
                        column: x => x.resident_id,
                        principalTable: "gv_residents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_3e5e24db0102026afdcef027f71",
                        column: x => x.created_by,
                        principalTable: "gv_users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_7311768c5bd78488d838330b485",
                        column: x => x.payment_method_id,
                        principalTable: "gv_payment_methods",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_7c0dd3529ae2256401560f9e2ac",
                        column: x => x.project_id,
                        principalTable: "gv_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gv_payments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    resident_id = table.Column<int>(type: "integer", nullable: false),
                    inv_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<short>(type: "smallint", nullable: false),
                    amount_due = table.Column<decimal>(type: "numeric", nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    fine = table.Column<decimal>(type: "numeric", nullable: true),
                    collection_fee = table.Column<decimal>(type: "numeric", nullable: true),
                    total_due = table.Column<decimal>(type: "numeric", nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: true),
                    payment_method_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<PaymentStatus>(type: "gv_payments_status_enum", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    collection_notice_id = table.Column<int>(type: "integer", nullable: true),
                    has_collection_notice = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    collection_paid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    advance = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    receipts_id = table.Column<int>(type: "integer", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_491bdc3fe4ae243631727806072", x => x.id);
                    table.ForeignKey(
                        name: "FK_6e12b601a48c4fe9cbfaab2a40e",
                        column: x => x.resident_id,
                        principalTable: "gv_residents",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_e5806facf7832f022d4da9700e9",
                        column: x => x.receipts_id,
                        principalTable: "gv_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_f2ec69212baf11990423f0b69ca",
                        column: x => x.created_by,
                        principalTable: "gv_users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "gv_collection_notices",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    resident_id = table.Column<int>(type: "integer", nullable: false),
                    payment_id = table.Column<int>(type: "integer", nullable: true),
                    notices_type = table.Column<NoticeType>(type: "gv_collection_notices_notices_type_enum", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<short>(type: "smallint", nullable: false),
                    notice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fee_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    status = table.Column<NoticeStatus>(type: "gv_collection_notices_status_enum", nullable: false),
                    receipt_id = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    deducted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deducted_by = table.Column<int>(type: "integer", nullable: true),
                    deducted_reason = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<int>(type: "integer", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_335e4f143a5db6456fbd885d0f1", x => x.id);
                    table.ForeignKey(
                        name: "FK_3bb3c9b9b4d6b3fafa48b1fda81",
                        column: x => x.payment_id,
                        principalTable: "gv_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_a098d82b900cec4c8244b68e28a",
                        column: x => x.receipt_id,
                        principalTable: "gv_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_b2176c4fe016bb1828816df12de",
                        column: x => x.resident_id,
                        principalTable: "gv_residents",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_gv_collection_notices_payment_id",
                table: "gv_collection_notices",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_collection_notices_receipt_id",
                table: "gv_collection_notices",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_collection_notices_resident_id",
                table: "gv_collection_notices",
                column: "resident_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_notice_template_project_id",
                table: "gv_notice_template",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "UQ_c8a214ddba9e88113ce624c5d79",
                table: "gv_payment_methods",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gv_payments_created_by",
                table: "gv_payments",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_gv_payments_receipts_id",
                table: "gv_payments",
                column: "receipts_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_payments_resident_id",
                table: "gv_payments",
                column: "resident_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_receipts_created_by",
                table: "gv_receipts",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_gv_receipts_payment_method_id",
                table: "gv_receipts",
                column: "payment_method_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_receipts_project_id",
                table: "gv_receipts",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_receipts_resident_id",
                table: "gv_receipts",
                column: "resident_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_receipts_revenues_payment_method_id",
                table: "gv_receipts_revenues",
                column: "payment_method_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_receipts_revenues_project_id",
                table: "gv_receipts_revenues",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_receipts_revenues_resident_id",
                table: "gv_receipts_revenues",
                column: "resident_id");

            migrationBuilder.CreateIndex(
                name: "uq_gv_receipts_revenues_receipt_no",
                table: "gv_receipts_revenues",
                column: "receipt_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gv_residents_project_id",
                table: "gv_residents",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_settings_project_id",
                table: "gv_settings",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_users_project_id",
                table: "gv_users",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_gv_users_username",
                table: "gv_users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gv_activity_logs");

            migrationBuilder.DropTable(
                name: "gv_collection_notices");

            migrationBuilder.DropTable(
                name: "gv_notice_template");

            migrationBuilder.DropTable(
                name: "gv_receipts_revenues");

            migrationBuilder.DropTable(
                name: "gv_settings");

            migrationBuilder.DropTable(
                name: "gv_payments");

            migrationBuilder.DropTable(
                name: "gv_receipts");

            migrationBuilder.DropTable(
                name: "gv_residents");

            migrationBuilder.DropTable(
                name: "gv_users");

            migrationBuilder.DropTable(
                name: "gv_payment_methods");

            migrationBuilder.DropTable(
                name: "gv_projects");
        }
    }
}
