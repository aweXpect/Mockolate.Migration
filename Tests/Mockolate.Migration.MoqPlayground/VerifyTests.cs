using Mockolate.Migration.MoqPlayground.Domain;
using Moq;

namespace Mockolate.Migration.MoqPlayground;

using It = Moq.It;
using Range = Moq.Range;

/// <summary>Verification patterns: Verify, VerifyGet, VerifySet, Times.*.</summary>
public class VerifyTests
{
	[Fact]
	public void Verify_method_atLeast()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense(It.IsAny<string>(), It.IsAny<int>())).Returns(true);

		dispenser.Object.Dispense("Dark", 1);
		dispenser.Object.Dispense("Dark", 2);

		dispenser.Verify(d => d.Dispense("Dark", It.IsAny<int>()), Times.AtLeast(2));
		dispenser.Verify(d => d.Dispense("Dark", It.IsAny<int>()), Times.AtLeastOnce);
	}

	[Fact]
	public void Verify_method_atMost()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense(It.IsAny<string>(), It.IsAny<int>())).Returns(true);

		dispenser.Object.Dispense("Dark", 1);

		dispenser.Verify(d => d.Dispense("Dark", It.IsAny<int>()), Times.AtMost(2));
		dispenser.Verify(d => d.Dispense("Dark", It.IsAny<int>()), Times.AtMostOnce);
	}

	[Fact]
	public void Verify_method_between()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense(It.IsAny<string>(), It.IsAny<int>())).Returns(true);

		dispenser.Object.Dispense("Dark", 1);
		dispenser.Object.Dispense("Dark", 2);

		dispenser.Verify(
			d => d.Dispense("Dark", It.IsAny<int>()),
			Times.Between(1, 3, Range.Inclusive));
	}

	[Fact]
	public void Verify_method_exactCount()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense(It.IsAny<string>(), It.IsAny<int>())).Returns(true);

		dispenser.Object.Dispense("Dark", 1);
		dispenser.Object.Dispense("Dark", 2);
		dispenser.Object.Dispense("Dark", 3);

		dispenser.Verify(d => d.Dispense("Dark", It.IsAny<int>()), Times.Exactly(3));
	}

	[Fact]
	public void Verify_method_wasCalledOnce()
	{
		Mock<IChocolateDispenser> dispenser = new();
		Mock<IChocolateFactory> factory = new();
		dispenser.Setup(d => d.Dispense("Dark", 2)).Returns(true);
		ChocolateShop shop = new(dispenser.Object, factory.Object);

		shop.Sell("Dark", 2);

		dispenser.Verify(d => d.Dispense("Dark", 2), Times.Once());
	}

	[Fact]
	public void Verify_method_wasNeverCalled()
	{
		Mock<IChocolateDispenser> dispenser = new();
		Mock<IChocolateFactory> factory = new();
		dispenser.Setup(d => d.Dispense("Dark", 1)).Returns(false);
		ChocolateShop shop = new(dispenser.Object, factory.Object);

		shop.Sell("Dark", 1); // dispense returns false → no audit follow-up

		dispenser.Verify(d => d.Dispense("Milk", It.IsAny<int>()), Times.Never);
	}

	[Fact]
	public void VerifyGet_property_wasRead()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.SetupGet(d => d.Name).Returns("Choc-Box");

		_ = dispenser.Object.Name;
		_ = dispenser.Object.Name;

		dispenser.VerifyGet(d => d.Name, Times.Exactly(2));
	}

	[Fact]
	public void VerifySet_anyValue_isMatchedWithItIsAny()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.SetupProperty(d => d.TotalDispensed);

		dispenser.Object.TotalDispensed = 7;
		dispenser.Object.TotalDispensed = 9;

		dispenser.VerifySet(d => d.TotalDispensed = It.IsAny<int>(), Times.Exactly(2));
	}

	[Fact]
	public void VerifySet_property_wasAssigned()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.SetupProperty(d => d.Name);

		dispenser.Object.Name = "Choco-2025";

		dispenser.VerifySet(d => d.Name = "Choco-2025", Times.Once());
	}
}
