using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NEXORA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsultationChatHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consultation_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consultation_conversations", x => x.id);
                    table.ForeignKey(
                        name: "FK_consultation_conversations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consultation_chat_messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    recommended_product_ids_json = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consultation_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_consultation_chat_messages_consultation_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "consultation_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consultation_chat_messages_conversation_id",
                table: "consultation_chat_messages",
                columns: new[] { "conversation_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_consultation_conversations_user_updated",
                table: "consultation_conversations",
                columns: new[] { "user_id", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consultation_chat_messages");

            migrationBuilder.DropTable(
                name: "consultation_conversations");
        }
    }
}
