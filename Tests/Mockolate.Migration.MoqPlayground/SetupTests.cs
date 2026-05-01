using Mockolate.Migration.MoqPlayground.Domain;
using Moq;

namespace Mockolate.Migration.MoqPlayground;

using It = Moq.It;

/// <summary>Method/property setup patterns: Returns / Throws / Callback / Sequence / Async.</summary>
public class SetupTests
{
	[Fact]
	public async Task Callback_ObservesArgumentsBeforeReturning()
	{
		Mock<IChocolateDispenser> dispenser = new();
		string? observedType = null;
		int observedAmount = 0;
		dispenser
			.Setup(d => d.Dispense(It.IsAny<string>(), It.IsAny<int>()))
			.Callback<string, int>((t, a) =>
			{
				observedType = t;
				observedAmount = a;
			})
			.Returns(true);

		dispenser.Object.Dispense("Milk", 4);

		await That(observedType).IsEqualTo("Milk");
		await That(observedAmount).IsEqualTo(4);
	}

	[Fact]
	public async Task NestedMockSetup_Recursive_ChainsThroughChildMock()
	{
		// Auto-mocking hierarchy: Moq creates child mocks for properties on demand.
		Mock<INested> outer = new()
		{
			DefaultValue = DefaultValue.Mock,
		};
		outer.Setup(o => o.Inner.Inner.Name).Returns("deep");

		await That(outer.Object.Inner.Inner.Name).IsEqualTo("deep");
	}

	[Fact]
	public async Task Returns_ArgumentBased_EvaluatesFromArgument()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense(It.IsAny<string>())).Returns((string s) => s.Length > 0);

		await That(dispenser.Object.Dispense("Dark")).IsTrue();
		await That(dispenser.Object.Dispense("")).IsFalse();
	}

	[Fact]
	public async Task Returns_DirectValue_DispensesAndShopRecordsTotal()
	{
		Mock<IChocolateDispenser> dispenser = new();
		Mock<IChocolateFactory> factory = new();
		dispenser.Setup(d => d.Dispense("Dark", 3)).Returns(true);
		ChocolateShop shop = new(dispenser.Object, factory.Object);

		bool sold = shop.Sell("Dark", 3);
		dispenser.Raise(d => d.ChocolateDispensed += null, "Dark", 3);

		await That(sold).IsTrue();
		await That(shop.TotalSold).IsEqualTo(3);
	}

	[Fact]
	public async Task Returns_LazyFactory_EvaluatedOnEachCall()
	{
		Mock<IChocolateDispenser> dispenser = new();
		int count = 1;
		dispenser.Setup(d => d.CountByType("Dark")).Returns(() => count);

		int first = dispenser.Object.CountByType("Dark");
		count = 5;
		int second = dispenser.Object.CountByType("Dark");

		await That(first).IsEqualTo(1);
		await That(second).IsEqualTo(5);
	}

	[Fact]
	public async Task ReturnsAsync_CompletesWithValue()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.DispenseAsync("Dark", 1)).ReturnsAsync(true);

		await That(await dispenser.Object.DispenseAsync("Dark", 1)).IsTrue();
	}

	[Fact]
	public async Task ReturnsAsync_Factory_EvaluatesPerCall()
	{
		Mock<IChocolateFactory> factory = new();
		factory.Setup(f => f.BakeAsync(It.IsAny<string>(), It.IsAny<int>()))
			.ReturnsAsync((string r, int c) => new ChocolateBar(r, c, c * 0.05m));

		ChocolateBar bar = await factory.Object.BakeAsync("Dark", 80);

		await That(bar.Type).IsEqualTo("Dark");
		await That(bar.Cocoa).IsEqualTo(80);
		await That(bar.Price).IsEqualTo(4.0m);
	}

	[Fact]
	public async Task SetupGet_Property_ReturnsConfiguredValue()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.SetupGet(d => d.Name).Returns("Choco-9000");

		await That(dispenser.Object.Name).IsEqualTo("Choco-9000");
	}

	[Fact]
	public async Task SetupProperty_TracksAssignmentsAndReturnsLastValue()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.SetupProperty(d => d.Name);

		dispenser.Object.Name = "ChocoMatic";

		await That(dispenser.Object.Name).IsEqualTo("ChocoMatic");
	}

	[Fact]
	public async Task SetupProperty_WithInitialValue_ReturnsThatBeforeAssignment()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.SetupProperty(d => d.Name, "Default");

		await That(dispenser.Object.Name).IsEqualTo("Default");
	}

	[Fact]
	public async Task SetupSequence_ReturnsValuesInOrder_ThenDefault()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.SetupSequence(d => d.Dispense(It.IsAny<string>(), It.IsAny<int>()))
			.Returns(true)
			.Throws(new InvalidChocolateException("temporarily out"))
			.Returns(false);

		await That(dispenser.Object.Dispense("Dark", 1)).IsTrue();
		await That(() => dispenser.Object.Dispense("Dark", 1))
			.Throws<InvalidChocolateException>();
		await That(dispenser.Object.Dispense("Dark", 1)).IsFalse();
	}

	[Fact]
	public async Task Throws_GenericException_IsRaisedOnInvocation()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense("reset", 0)).Throws<InvalidOperationException>();

		await That(() => dispenser.Object.Dispense("reset", 0))
			.Throws<InvalidOperationException>();
	}

	[Fact]
	public async Task Throws_SpecificInstance_IsRaisedOnInvocation()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense("", 0)).Throws(new InvalidChocolateException("empty type"));

		await That(() => dispenser.Object.Dispense("", 0))
			.Throws<InvalidChocolateException>()
			.WithMessage("empty type");
	}

	[Fact]
	public async Task ThrowsAsync_CompletesWithException()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.DispenseAsync("Dark", 1)).ThrowsAsync(new TimeoutException());

		Task<bool> Act()
		{
			return dispenser.Object.DispenseAsync("Dark", 1);
		}

		await That((Func<Task<bool>>)Act).Throws<TimeoutException>();
	}

	public interface INested
	{
		INested Inner { get; }
		string Name { get; }
	}
}
