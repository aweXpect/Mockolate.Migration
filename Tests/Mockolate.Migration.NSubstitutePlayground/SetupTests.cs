using Mockolate.Migration.NSubstitutePlayground.Domain;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Mockolate.Migration.NSubstitutePlayground;

/// <summary>Setup patterns: Returns / Throws / sequence Returns / ReturnsForAnyArgs / ThrowsForAnyArgs / AndDoes.</summary>
public class SetupTests
{
	[Fact]
	public async Task NestedSubstituteSetup_chainedAcrossDependentSubstitutes()
	{
		// NSubstitute auto-creates child substitutes for properties returning interfaces / classes.
		// Migration emits a TODO comment because Mockolate needs the child registered explicitly.
		INested outer = Substitute.For<INested>();
		outer.Inner.Inner.Name.Returns("deep");

		await That(outer.Inner.Inner.Name).IsEqualTo("deep");
	}

	[Fact]
	public async Task PropertyAssignment_returnsLastSetValue()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();

		dispenser.Name = "ChocoMatic";

		await That(dispenser.Name).IsEqualTo("ChocoMatic");
	}

	[Fact]
	public async Task PropertyReturns_setsConfiguredValue()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Name.Returns("Choco-9000");

		await That(dispenser.Name).IsEqualTo("Choco-9000");
	}

	[Fact]
	public async Task Returns_directValue_dispensesAndShopRecordsTotal()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		IChocolateFactory factory = Substitute.For<IChocolateFactory>();
		dispenser.Dispense("Dark", 3).Returns(true);
		ChocolateShop shop = new(dispenser, factory);

		bool sold = shop.Sell("Dark", 3);
		dispenser.ChocolateDispensed += Raise.Event<ChocolateDispensedDelegate>("Dark", 3);

		await That(sold).IsTrue();
		await That(shop.TotalSold).IsEqualTo(3);
	}

	[Fact]
	public async Task Returns_factory_evaluatesPerCall()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		int counter = 1;
		dispenser.CountByType(Arg.Any<string>()).Returns(_ => counter);

		int first = dispenser.CountByType("Dark");
		counter = 5;
		int second = dispenser.CountByType("Dark");

		await That(first).IsEqualTo(1);
		await That(second).IsEqualTo(5);
	}

	[Fact]
	public async Task Returns_sequenceOfValues_returnsThemInOrder()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.CountByType("Dark").Returns(1, 2, 3);

		await That(dispenser.CountByType("Dark")).IsEqualTo(1);
		await That(dispenser.CountByType("Dark")).IsEqualTo(2);
		await That(dispenser.CountByType("Dark")).IsEqualTo(3);
	}

	[Fact]
	public async Task ReturnsAndDoes_combinesReturnAndSideEffect()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		int sideEffect = 0;
		dispenser.Dispense("Dark", 1).Returns(true).AndDoes(_ => sideEffect++);

		_ = dispenser.Dispense("Dark", 1);
		_ = dispenser.Dispense("Dark", 1);

		await That(sideEffect).IsEqualTo(2);
	}

	[Fact]
	public async Task ReturnsAsync_completesWithValue()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.DispenseAsync("Dark", 1).Returns(Task.FromResult(true));

		await That(await dispenser.DispenseAsync("Dark", 1)).IsTrue();
	}

	[Fact]
	public async Task ReturnsForAnyArgs_ignoresArgumentsAtSetup()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Dispense("Dark", 1).ReturnsForAnyArgs(true);

		await That(dispenser.Dispense("Milk", 99)).IsTrue();
		await That(dispenser.Dispense("White", 1)).IsTrue();
	}

	[Fact]
	public async Task Throws_genericException_isRaisedOnInvocation()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Dispense("reset", 0).Throws<InvalidOperationException>();

		await That(() => dispenser.Dispense("reset", 0))
			.Throws<InvalidOperationException>();
	}

	[Fact]
	public async Task Throws_specificInstance_isRaisedOnInvocation()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Dispense("", 0).Throws(new InvalidChocolateException("empty type"));

		await That(() => dispenser.Dispense("", 0))
			.Throws<InvalidChocolateException>()
			.WithMessage("empty type");
	}

	[Fact]
	public async Task ThrowsForAnyArgs_appliesEveryCall()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Dispense("ignored", 0).ThrowsForAnyArgs<InvalidChocolateException>();

		await That(() => dispenser.Dispense("Dark", 1))
			.Throws<InvalidChocolateException>();
	}

	public interface INested
	{
		INested Inner { get; }
		string Name { get; }
	}
}
