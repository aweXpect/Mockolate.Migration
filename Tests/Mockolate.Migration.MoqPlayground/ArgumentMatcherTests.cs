using System.Text.RegularExpressions;
using Mockolate.Migration.MoqPlayground.Domain;
using Moq;

namespace Mockolate.Migration.MoqPlayground;

using It = Moq.It;
using Range = Moq.Range;

/// <summary>Argument matchers: It.IsAny / It.Is / It.IsRegex / It.IsInRange / It.IsNotNull / It.Ref / out.</summary>
public class ArgumentMatcherTests
{
	[Fact]
	public async Task ItIs_predicate_matchesEvenAmounts()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense("Dark", It.Is<int>(i => i % 2 == 0))).Returns(true);

		await That(dispenser.Object.Dispense("Dark", 4)).IsTrue();
		await That(dispenser.Object.Dispense("Dark", 3)).IsFalse();
	}

	[Fact]
	public async Task ItIsAny_matchesAnyValue()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense(It.IsAny<string>(), It.IsAny<int>())).Returns(true);

		await That(dispenser.Object.Dispense("Dark", 1)).IsTrue();
		await That(dispenser.Object.Dispense("Milk", 99)).IsTrue();
	}

	[Fact]
	public async Task ItIsInRange_inclusive_matchesBoundaries()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense("Dark", It.IsInRange(1, 5, Range.Inclusive))).Returns(true);

		await That(dispenser.Object.Dispense("Dark", 1)).IsTrue();
		await That(dispenser.Object.Dispense("Dark", 5)).IsTrue();
		await That(dispenser.Object.Dispense("Dark", 6)).IsFalse();
	}

	[Fact]
	public async Task ItIsNotNull_rejectsNull()
	{
		Mock<IChocolateFactory> factory = new();
		factory.Setup(f => f.RegisterRecipe(It.IsNotNull<string>())).Returns(true);

		await That(factory.Object.RegisterRecipe("Pralines")).IsTrue();
		await That(factory.Object.RegisterRecipe(null!)).IsFalse();
	}

	[Fact]
	public async Task ItIsRegex_matchesPattern()
	{
		Mock<IChocolateFactory> factory = new();
		factory.Setup(f => f.RegisterRecipe(It.IsRegex("^Dark", RegexOptions.IgnoreCase))).Returns(true);

		await That(factory.Object.RegisterRecipe("DarkTruffle")).IsTrue();
		await That(factory.Object.RegisterRecipe("MilkTruffle")).IsFalse();
	}

	[Fact]
	public async Task OutParameter_isSetByItIsOut()
	{
		Mock<IChocolateDispenser> dispenser = new();
		int reserved = 7;
		dispenser.Setup(d => d.TryReserve("Dark", out reserved)).Returns(true);

		bool ok = dispenser.Object.TryReserve("Dark", out int actual);

		await That(ok).IsTrue();
		await That(actual).IsEqualTo(7);
	}

	[Fact]
	public async Task PlainValue_isUsedAsExactMatch()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense("Milk", 3)).Returns(true);

		await That(dispenser.Object.Dispense("Milk", 3)).IsTrue();
		await That(dispenser.Object.Dispense("Milk", 4)).IsFalse();
	}

	[Fact]
	public async Task RefParameter_anyMatch_acceptsAnyRef()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Refill("Dark", ref It.Ref<int>.IsAny)).Returns(true);

		int amount = 10;
		bool ok = dispenser.Object.Refill("Dark", ref amount);

		await That(ok).IsTrue();
	}
}
