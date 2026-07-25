namespace ApprovalFlow.Domain;

public sealed class DomainAuthorizationException(string message) : Exception(message);
