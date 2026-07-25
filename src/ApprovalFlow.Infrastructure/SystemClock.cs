using ApprovalFlow.Application;

namespace ApprovalFlow.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
