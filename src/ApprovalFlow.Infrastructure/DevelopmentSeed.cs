using ApprovalFlow.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApprovalFlow.Infrastructure;

public static class DevelopmentSeed
{
    public static async Task SeedAsync(ApprovalFlowDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        if (await dbContext.PurchaseRequests.AnyAsync(cancellationToken))
            return;

        var sample = PurchaseRequest.Create(
            "Contoso Office Supply",
            "ENG-100",
            "OfficeSupplies",
            "Non-sensitive sample request for local development.",
            new DateOnly(2030, 1, 15),
            "employee.demo",
            [new PurchaseRequestLineItem("Ergonomic keyboard", 2, 89.50m)],
            DateTimeOffset.UtcNow);
        await dbContext.PurchaseRequests.AddAsync(sample, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
