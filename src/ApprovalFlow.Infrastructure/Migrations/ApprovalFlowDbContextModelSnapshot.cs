using ApprovalFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace ApprovalFlow.Infrastructure.Migrations;

[DbContext(typeof(ApprovalFlowDbContext))]
partial class ApprovalFlowDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.10");

        modelBuilder.Entity("ApprovalFlow.Domain.PurchaseRequest", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd();
            b.Property<string>("BusinessJustification").IsRequired().HasMaxLength(2000);
            b.Property<string>("Category").IsRequired().HasMaxLength(100);
            b.Property<string>("CostCenter").IsRequired().HasMaxLength(50);
            b.Property<DateTimeOffset>("CreatedAt");
            b.Property<DateOnly>("RequestedDeliveryDate");
            b.Property<string>("Requester").IsRequired().HasMaxLength(100);
            b.Property<PurchaseRequestStatus>("Status").HasConversion<string>().HasMaxLength(40);
            b.Property<string>("Vendor").IsRequired().HasMaxLength(200);
            b.HasKey("Id");
            b.ToTable("PurchaseRequests");
        });

        modelBuilder.Entity("ApprovalFlow.Domain.PurchaseRequestAuditEntry", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd();
            b.Property<string>("Actor").IsRequired().HasMaxLength(100);
            b.Property<PurchaseRequestStatus>("FromStatus").HasConversion<string>().HasMaxLength(40);
            b.Property<DateTimeOffset>("OccurredAt");
            b.Property<Guid>("PurchaseRequestId");
            b.Property<string>("Reason").HasMaxLength(1000);
            b.Property<PurchaseRequestStatus>("ToStatus").HasConversion<string>().HasMaxLength(40);
            b.HasKey("Id");
            b.HasIndex("PurchaseRequestId", "OccurredAt");
            b.ToTable("PurchaseRequestAuditEntries");
        });

        modelBuilder.Entity("ApprovalFlow.Domain.PurchaseRequestLineItem", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd();
            b.Property<string>("Description").IsRequired().HasMaxLength(500);
            b.Property<Guid>("PurchaseRequestId");
            b.Property<int>("Quantity");
            b.Property<decimal>("UnitPrice").HasPrecision(18, 2);
            b.HasKey("Id");
            b.HasIndex("PurchaseRequestId");
            b.ToTable("PurchaseRequestLineItems");
        });

        modelBuilder.Entity("ApprovalFlow.Domain.PurchaseRequestAuditEntry", b =>
        {
            b.HasOne("ApprovalFlow.Domain.PurchaseRequest", null)
                .WithMany("AuditEntries")
                .HasForeignKey("PurchaseRequestId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("ApprovalFlow.Domain.PurchaseRequestLineItem", b =>
        {
            b.HasOne("ApprovalFlow.Domain.PurchaseRequest", null)
                .WithMany("LineItems")
                .HasForeignKey("PurchaseRequestId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
    }
}
