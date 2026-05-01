namespace Mockolate.Migration.MoqPlayground.Domain;

/// <summary>Used for multi-interface mocks (Moq <c>As&lt;T&gt;()</c>, NSubstitute <c>Substitute.For&lt;T1,T2&gt;()</c>).</summary>
public interface IChocolateAuditor
{
	int AuditCount { get; }
	void RecordSale(string type, int amount, decimal total);
}
