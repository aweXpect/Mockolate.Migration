using Mockolate.Migration.NSubstitutePlayground.Domain;
using NSubstitute;

namespace Mockolate.Migration.NSubstitutePlayground;

/// <summary>Verification patterns: Received / DidNotReceive / Received(n) / WithAnyArgs.</summary>
public class VerifyTests
{
	[Fact]
	public void ClearReceivedCalls_resetsHistory()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Dispense(Arg.Any<string>(), Arg.Any<int>()).Returns(true);

		dispenser.Dispense("Dark", 1);
		dispenser.ClearReceivedCalls();
		dispenser.Dispense("Dark", 2);

		dispenser.Received(1).Dispense(Arg.Any<string>(), Arg.Any<int>());
	}

	[Fact]
	public void DidNotReceive_method_wasNeverCalled()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		IChocolateFactory factory = Substitute.For<IChocolateFactory>();
		dispenser.Dispense("Dark", 1).Returns(false);
		ChocolateShop shop = new(dispenser, factory);

		shop.Sell("Dark", 1);

		dispenser.DidNotReceive().Dispense("Milk", Arg.Any<int>());
	}

	[Fact]
	public void DidNotReceiveWithAnyArgs_matchesNoInvocation()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();

		dispenser.DidNotReceiveWithAnyArgs().Dispense(default!, default);
	}

	[Fact]
	public void Received_method_wasCalledOnce()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		IChocolateFactory factory = Substitute.For<IChocolateFactory>();
		dispenser.Dispense("Dark", 2).Returns(true);
		ChocolateShop shop = new(dispenser, factory);

		shop.Sell("Dark", 2);

		dispenser.Received().Dispense("Dark", 2);
	}

	[Fact]
	public void ReceivedExactCount_isHonored()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Dispense(Arg.Any<string>(), Arg.Any<int>()).Returns(true);

		dispenser.Dispense("Dark", 1);
		dispenser.Dispense("Dark", 2);
		dispenser.Dispense("Dark", 3);

		dispenser.Received(3).Dispense("Dark", Arg.Any<int>());
	}

	[Fact]
	public void ReceivedProperty_was_read()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Name.Returns("Choc-Box");

		_ = dispenser.Name;
		_ = dispenser.Name;

		_ = dispenser.Received(2).Name;
	}

	[Fact]
	public void ReceivedProperty_was_set()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();

		dispenser.Name = "Choco-2025";

		dispenser.Received().Name = "Choco-2025";
	}

	[Fact]
	public void ReceivedWithAnyArgs_matchesAllInvocations()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Dispense(Arg.Any<string>(), Arg.Any<int>()).Returns(true);

		dispenser.Dispense("Dark", 1);
		dispenser.Dispense("Milk", 5);

		// Arguments below are placeholders; ReceivedWithAnyArgs ignores them.
		dispenser.ReceivedWithAnyArgs(2).Dispense(default!, default);
	}
}
