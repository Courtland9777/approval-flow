using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace ApprovalFlow.Infrastructure.Migrations;

[DbContext(typeof(ApprovalFlowDbContext))]
[Migration("202607240001_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PurchaseRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Vendor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CostCenter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                BusinessJustification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: false),
                Requester = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PurchaseRequests", x => x.Id));

        migrationBuilder.CreateTable(
            name: "PurchaseRequestAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PurchaseRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Actor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                FromStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                ToStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseRequestAuditEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_PurchaseRequestAuditEntries_PurchaseRequests_PurchaseRequestId",
                    column: x => x.PurchaseRequestId,
                    principalTable: "PurchaseRequests",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PurchaseRequestLineItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PurchaseRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Quantity = table.Column<int>(type: "int", nullable: false),
                UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseRequestLineItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_PurchaseRequestLineItems_PurchaseRequests_PurchaseRequestId",
                    column: x => x.PurchaseRequestId,
                    principalTable: "PurchaseRequests",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseRequestAuditEntries_PurchaseRequestId_OccurredAt",
            table: "PurchaseRequestAuditEntries",
            columns: new[] { "PurchaseRequestId", "OccurredAt" });
        migrationBuilder.CreateIndex(
            name: "IX_PurchaseRequestLineItems_PurchaseRequestId",
            table: "PurchaseRequestLineItems",
            column: "PurchaseRequestId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PurchaseRequestAuditEntries");
        migrationBuilder.DropTable(name: "PurchaseRequestLineItems");
        migrationBuilder.DropTable(name: "PurchaseRequests");
    }
}
