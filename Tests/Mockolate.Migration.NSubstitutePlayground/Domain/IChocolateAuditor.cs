namespace Mockolate.Migration.NSubstitutePlayground.Domain;

/// <summary>Used for multi-interface mocks (NSubstitute <c>Substitute.For&lt;T1,T2&gt;()</c>).</summary>
public interface IChocolateAuditor
{
	int AuditCount { get; }
	void RecordSale(string type, int amount, decimal total);
}
