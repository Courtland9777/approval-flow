namespace ApprovalFlow.Domain;

public sealed class DomainConflictException(string message) : Exception(message);
