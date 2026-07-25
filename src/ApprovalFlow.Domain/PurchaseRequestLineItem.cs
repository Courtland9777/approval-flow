namespace ApprovalFlow.Domain;

public sealed class PurchaseRequestLineItem
{
    private PurchaseRequestLineItem() { }

    public PurchaseRequestLineItem(string description, int quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainValidationException("Line item description is required.");
        if (quantity <= 0)
            throw new DomainValidationException("Line item quantity must be greater than zero.");
        if (unitPrice <= 0)
            throw new DomainValidationException("Line item unit price must be greater than zero.");

        Id = Guid.NewGuid();
        Description = description.Trim();
        Quantity = quantity;
        UnitPrice = decimal.Round(unitPrice, 2);
    }

    public Guid Id { get; private set; }
    public Guid PurchaseRequestId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal => Quantity * UnitPrice;
}
