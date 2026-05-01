using Mockolate.Migration.NSubstitutePlayground.Domain;
using NSubstitute;

namespace Mockolate.Migration.NSubstitutePlayground;

/// <summary>Event raising via the <c>Raise</c> helper.</summary>
public class EventTests
{
	[Fact]
	public async Task Raise_customDelegate_invokesSubscribedHandler()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		string? observedType = null;
		int observedAmount = 0;
		dispenser.ChocolateDispensed += (t, a) =>
		{
			observedType = t;
			observedAmount = a;
		};

		dispenser.ChocolateDispensed += Raise.Event<ChocolateDispensedDelegate>("Dark", 5);

		await That(observedType).IsEqualTo("Dark");
		await That(observedAmount).IsEqualTo(5);
	}

	[Fact]
	public async Task Raise_eventHandlerWithArgsOnly_passesNullSender()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		int? observedLow = null;
		dispenser.StockLow += (_, low) => observedLow = low;

		dispenser.StockLow += Raise.Event<EventHandler<int>>(null, 7);

		await That(observedLow).IsEqualTo(7);
	}

	[Fact]
	public async Task Raise_eventHandlerWithSenderAndArgs_passesBoth()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		object? observedSender = null;
		int? observedLow = null;
		dispenser.StockLow += (s, low) =>
		{
			observedSender = s;
			observedLow = low;
		};

		dispenser.StockLow += Raise.Event<EventHandler<int>>(dispenser, 2);

		await That(observedSender).IsSameAs(dispenser);
		await That(observedLow).IsEqualTo(2);
	}

	[Fact]
	public async Task ShopSubscribesOnConstruction_andTracksDispensedAmounts()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		IChocolateFactory factory = Substitute.For<IChocolateFactory>();
		dispenser.Dispense(Arg.Any<string>(), Arg.Any<int>()).Returns(true);
		ChocolateShop shop = new(dispenser, factory);

		shop.Sell("Dark", 2);
		dispenser.ChocolateDispensed += Raise.Event<ChocolateDispensedDelegate>("Dark", 2);
		shop.Sell("Milk", 5);
		dispenser.ChocolateDispensed += Raise.Event<ChocolateDispensedDelegate>("Milk", 5);

		await That(shop.TotalSold).IsEqualTo(7);
	}
}
