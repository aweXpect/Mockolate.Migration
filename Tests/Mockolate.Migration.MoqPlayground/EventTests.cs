using Mockolate.Migration.MoqPlayground.Domain;
using Moq;

namespace Mockolate.Migration.MoqPlayground;

using It = Moq.It;

/// <summary>Event subscription / raising / verification.</summary>
public class EventTests
{
	[Fact]
	public async Task Raise_customDelegate_invokesSubscribedHandler()
	{
		Mock<IChocolateDispenser> dispenser = new();
		string? observedType = null;
		int observedAmount = 0;
		dispenser.Object.ChocolateDispensed += (t, a) =>
		{
			observedType = t;
			observedAmount = a;
		};

		dispenser.Raise(d => d.ChocolateDispensed += null, "Dark", 5);

		await That(observedType).IsEqualTo("Dark");
		await That(observedAmount).IsEqualTo(5);
	}

	[Fact]
	public async Task Raise_eventHandlerStandard_passesArgs()
	{
		Mock<IChocolateDispenser> dispenser = new();
		int? observed = null;
		dispenser.Object.StockLow += (_, low) => observed = low;

		dispenser.Raise(d => d.StockLow += null, dispenser.Object, 2);

		await That(observed).IsEqualTo(2);
	}

	[Fact]
	public async Task ShopSubscribesOnConstruction_andTracksDispensedAmounts()
	{
		Mock<IChocolateDispenser> dispenser = new();
		Mock<IChocolateFactory> factory = new();
		dispenser.Setup(d => d.Dispense(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
		ChocolateShop shop = new(dispenser.Object, factory.Object);

		shop.Sell("Dark", 2);
		dispenser.Raise(d => d.ChocolateDispensed += null, "Dark", 2);
		shop.Sell("Milk", 5);
		dispenser.Raise(d => d.ChocolateDispensed += null, "Milk", 5);

		await That(shop.TotalSold).IsEqualTo(7);
		dispenser.VerifyAdd(
			d => d.ChocolateDispensed += It.IsAny<ChocolateDispensedDelegate>(),
			Times.Once());
	}

	[Fact]
	public async Task VerifyAdd_recordsSubscription()
	{
		Mock<IChocolateDispenser> dispenser = new();
		ChocolateDispensedDelegate handler = (_, _) => { };
		dispenser.Object.ChocolateDispensed += handler;

		dispenser.VerifyAdd(d => d.ChocolateDispensed += It.IsAny<ChocolateDispensedDelegate>(), Times.Once());
	}

	[Fact]
	public async Task VerifyRemove_recordsUnsubscription()
	{
		Mock<IChocolateDispenser> dispenser = new();
		ChocolateDispensedDelegate handler = (_, _) => { };
		dispenser.Object.ChocolateDispensed += handler;
		dispenser.Object.ChocolateDispensed -= handler;

		dispenser.VerifyRemove(d => d.ChocolateDispensed -= It.IsAny<ChocolateDispensedDelegate>(), Times.Once());
	}
}
