using ApprovalFlow.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApprovalFlow.Infrastructure;

public sealed class ApprovalFlowDbContext(DbContextOptions<ApprovalFlowDbContext> options)
    : IdentityDbContext<IdentityUser, IdentityRole, string>(options)
{
    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<ActivityProjection> ActivityProjections => Set<ActivityProjection>();
    public DbSet<FailedBrokerMessage> FailedBrokerMessages => Set<FailedBrokerMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var request = modelBuilder.Entity<PurchaseRequest>();
        request.ToTable("PurchaseRequests");
        request.HasKey(x => x.Id);
        request.Property(x => x.Vendor).HasMaxLength(200).IsRequired();
        request.Property(x => x.CostCenter).HasMaxLength(50).IsRequired();
        request.Property(x => x.Category).HasMaxLength(100).IsRequired();
        request.Property(x => x.BusinessJustification).HasMaxLength(2000).IsRequired();
        request.Property(x => x.Requester).HasMaxLength(100).IsRequired();
        request.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        request.Property(x => x.RowVersion).IsRowVersion();
        request.Ignore(x => x.Total);
        request.HasMany(x => x.LineItems).WithOne().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Cascade);
        request.HasMany(x => x.AuditEntries).WithOne().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Cascade);
        request.Navigation(x => x.LineItems).UsePropertyAccessMode(PropertyAccessMode.Field);
        request.Navigation(x => x.AuditEntries).UsePropertyAccessMode(PropertyAccessMode.Field);

        var item = modelBuilder.Entity<PurchaseRequestLineItem>();
        item.ToTable("PurchaseRequestLineItems");
        item.HasKey(x => x.Id);
        item.Property(x => x.Description).HasMaxLength(500).IsRequired();
        item.Property(x => x.UnitPrice).HasPrecision(18, 2);
        item.Ignore(x => x.LineTotal);

        var audit = modelBuilder.Entity<PurchaseRequestAuditEntry>();
        audit.ToTable("PurchaseRequestAuditEntries");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        audit.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(40);
        audit.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(40);
        audit.Property(x => x.Reason).HasMaxLength(1000);
        audit.HasIndex(x => new { x.PurchaseRequestId, x.OccurredAt });

        var outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.ToTable("OutboxMessages");
        outbox.HasKey(x => x.Id);
        outbox.Property(x => x.EventType).HasMaxLength(200).IsRequired();
        outbox.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
        outbox.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        outbox.Property(x => x.LastError).HasMaxLength(2000);
        outbox.HasIndex(x => new { x.PublishedAt, x.FailedAt, x.NextAttemptAt });

        var processed = modelBuilder.Entity<ProcessedMessage>();
        processed.ToTable("ProcessedMessages");
        processed.HasKey(x => x.MessageId);

        var projection = modelBuilder.Entity<ActivityProjection>();
        projection.ToTable("ActivityProjections");
        projection.HasKey(x => x.Id);
        projection.Property(x => x.EventType).HasMaxLength(200).IsRequired();
        projection.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        projection.Property(x => x.Summary).HasMaxLength(1000).IsRequired();
        projection.HasIndex(x => x.MessageId).IsUnique();
        projection.HasIndex(x => new { x.PurchaseRequestId, x.RecordedAt });

        var failed = modelBuilder.Entity<FailedBrokerMessage>();
        failed.ToTable("FailedBrokerMessages");
        failed.HasKey(x => x.Id);
        failed.Property(x => x.BrokerMessageId).HasMaxLength(128).IsRequired();
        failed.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        failed.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        failed.HasIndex(x => x.BrokerMessageId);
    }
}
