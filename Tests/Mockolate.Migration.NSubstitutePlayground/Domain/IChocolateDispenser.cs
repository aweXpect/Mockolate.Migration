namespace Mockolate.Migration.NSubstitutePlayground.Domain;

/// <summary>
///     Surface intended to exercise as much of Moq/NSubstitute as possible:
///     indexer, properties (read/write), method overloads, async, ref, out, custom event, standard event.
/// </summary>
public interface IChocolateDispenser
{
	int this[string type] { get; set; }
	int TotalDispensed { get; set; }
	string Name { get; set; }

	bool Dispense(string type, int amount);
	bool Dispense(string type);
	Task<bool> DispenseAsync(string type, int amount);

	/// <summary>Tries to reserve some stock for the given type, returning the reserved amount.</summary>
	bool TryReserve(string type, out int reserved);

	/// <summary>Refills stock; the caller passes a desired amount and gets the actual amount back via ref.</summary>
	bool Refill(string type, ref int amount);

	int CountByType(string type);

	/// <summary>Void method — used to exercise NSubstitute <c>When/Do</c>.</summary>
	void Notify(string type, int amount);

	event ChocolateDispensedDelegate ChocolateDispensed;
	event EventHandler<int> StockLow;
}
