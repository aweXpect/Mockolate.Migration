namespace Mockolate.Migration.NSubstitutePlayground.Domain;

/// <summary>Concrete forwarding target — used for <c>Substitute.ForTypeForwardingTo</c>.</summary>
public class StaticAuditor : IChocolateAuditor
{
	public int AuditCount { get; private set; }

	public void RecordSale(string type, int amount, decimal total) => AuditCount++;
}
