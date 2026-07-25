using ApprovalFlow.Application;
using ApprovalFlow.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalFlow.Infrastructure;

public static class DevelopmentSeed
{
    public const string EmployeeUserName = "employee.demo@local.test";
    public const string SecondEmployeeUserName = "employee2.demo@local.test";
    public const string ManagerUserName = "manager.demo@local.test";
    public const string FinanceUserName = "finance.demo@local.test";
    public const string DemoPassword = "LocalOnly!2026";

    public static async Task SeedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var dbContext = services.GetRequiredService<ApprovalFlowDbContext>();

        foreach (var role in new[]
                 {
                     ApprovalFlowRoles.Employee,
                     ApprovalFlowRoles.Manager,
                     ApprovalFlowRoles.FinanceAdministrator
                 })
        {
            if (!await roleManager.RoleExistsAsync(role))
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)));
        }

        await EnsureUserAsync(userManager, EmployeeUserName, [ApprovalFlowRoles.Employee]);
        await EnsureUserAsync(userManager, SecondEmployeeUserName, [ApprovalFlowRoles.Employee]);
        await EnsureUserAsync(
            userManager,
            ManagerUserName,
            [ApprovalFlowRoles.Employee, ApprovalFlowRoles.Manager]);
        await EnsureUserAsync(userManager, FinanceUserName, [ApprovalFlowRoles.FinanceAdministrator]);

        if (await dbContext.PurchaseRequests.AnyAsync(cancellationToken))
            return;

        var sample = PurchaseRequest.Create(
            "Contoso Office Supply",
            "ENG-100",
            "OfficeSupplies",
            "Non-sensitive sample request for local development.",
            new DateOnly(2030, 1, 15),
            EmployeeUserName,
            [new PurchaseRequestLineItem("Ergonomic keyboard", 2, 89.50m)],
            DateTimeOffset.UtcNow);
        await dbContext.PurchaseRequests.AddAsync(sample, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureUserAsync(
        UserManager<IdentityUser> userManager,
        string userName,
        string[] roles)
    {
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            user = new IdentityUser
            {
                UserName = userName,
                Email = userName,
                EmailConfirmed = true
            };
            EnsureSucceeded(await userManager.CreateAsync(user, DemoPassword));
        }

        foreach (var role in roles)
        {
            if (!await userManager.IsInRoleAsync(user, role))
                EnsureSucceeded(await userManager.AddToRoleAsync(user, role));
        }
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
