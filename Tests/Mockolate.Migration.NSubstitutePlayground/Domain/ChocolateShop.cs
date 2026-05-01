namespace Mockolate.Migration.NSubstitutePlayground.Domain;

/// <summary>The system-under-test that orchestrates the dependencies.</summary>
public sealed class ChocolateShop
{
	private readonly IChocolateAuditor? _auditor;
	private readonly IChocolateDispenser _dispenser;
	private readonly IChocolateFactory _factory;

	public ChocolateShop(
		IChocolateDispenser dispenser,
		IChocolateFactory factory,
		IChocolateAuditor? auditor = null)
	{
		_dispenser = dispenser;
		_factory = factory;
		_auditor = auditor;
		_dispenser.ChocolateDispensed += OnDispensed;
	}

	public int TotalSold { get; private set; }

	public string DispenserName
	{
		get => _dispenser.Name;
		set => _dispenser.Name = value;
	}

	public bool Sell(string type, int amount, decimal pricePerUnit = 1.5m)
	{
		if (!_dispenser.Dispense(type, amount))
		{
			return false;
		}

		_auditor?.RecordSale(type, amount, amount * pricePerUnit);
		return true;
	}

	public Task<bool> SellAsync(string type, int amount)
		=> _dispenser.DispenseAsync(type, amount);

	public Task<ChocolateBar> RestockAsync(string recipe, int cocoa)
		=> _factory.BakeAsync(recipe, cocoa);

	public int CheckStock(string type) => _dispenser[type];

	public bool TryReserveStock(string type, out int reserved)
		=> _dispenser.TryReserve(type, out reserved);

	private void OnDispensed(string type, int amount) => TotalSold += amount;
}
