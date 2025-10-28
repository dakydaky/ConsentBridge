using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantKeysAndConsentTokenMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TokenAlgorithm",
                table: "Consents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "Consents",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TokenIssuedAt",
                table: "Consents",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "TokenKeyId",
                table: "Consents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("UPDATE \"Consents\" SET \"TokenIssuedAt\" = \"IssuedAt\" WHERE \"TokenIssuedAt\" = '0001-01-01T00:00:00Z';");

            migrationBuilder.CreateTable(
                name: "ConsentTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    KeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentTokens_Consents_ConsentId",
                        column: x => x.ConsentId,
                        principalTable: "Consents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Algorithm = table.Column<string>(type: "text", nullable: false),
                    PublicJwk = table.Column<string>(type: "jsonb", nullable: false),
                    PrivateKeyProtected = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantKeys_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentTokens_ConsentId",
                table: "ConsentTokens",
                column: "ConsentId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentTokens_TokenHash",
                table: "ConsentTokens",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentTokens_TokenId",
                table: "ConsentTokens",
                column: "TokenId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantKeys_TenantId_Purpose_KeyId",
                table: "TenantKeys",
                columns: new[] { "TenantId", "Purpose", "KeyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsentTokens");

            migrationBuilder.DropTable(
                name: "TenantKeys");

            migrationBuilder.DropColumn(
                name: "TokenAlgorithm",
                table: "Consents");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "Consents");

            migrationBuilder.DropColumn(
                name: "TokenIssuedAt",
                table: "Consents");

            migrationBuilder.DropColumn(
                name: "TokenKeyId",
                table: "Consents");
        }
    }
}
