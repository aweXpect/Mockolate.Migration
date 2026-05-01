using Mockolate.Migration.MoqPlayground.Domain;
using Moq;

namespace Mockolate.Migration.MoqPlayground;

using It = Moq.It;
using MockBehavior = Moq.MockBehavior;

/// <summary>MockSequence + InSequence call ordering (Moq pattern).</summary>
public class SequenceTests
{
	[Fact]
	public async Task MockSequence_strictOrdering_isHonored()
	{
		Mock<IChocolateDispenser> dispenser = new(MockBehavior.Strict);
		MockSequence sequence = new();

		dispenser.InSequence(sequence).Setup(d => d.Dispense("Dark", 1)).Returns(true);
		dispenser.InSequence(sequence).Setup(d => d.Dispense("Milk", 2)).Returns(true);
		dispenser.InSequence(sequence).Setup(d => d.Dispense("White", 3)).Returns(true);

		await That(dispenser.Object.Dispense("Dark", 1)).IsTrue();
		await That(dispenser.Object.Dispense("Milk", 2)).IsTrue();
		await That(dispenser.Object.Dispense("White", 3)).IsTrue();
	}

	[Fact]
	public async Task SetupSequence_returnsValuesInOrder()
	{
		Mock<IChocolateFactory> factory = new();
		factory.SetupSequence(f => f.RegisterRecipe(It.IsAny<string>()))
			.Returns(true)
			.Returns(false)
			.Throws(new InvalidChocolateException("registry full"));

		await That(factory.Object.RegisterRecipe("a")).IsTrue();
		await That(factory.Object.RegisterRecipe("b")).IsFalse();
		await That(() => factory.Object.RegisterRecipe("c"))
			.Throws<InvalidChocolateException>();
	}
}
