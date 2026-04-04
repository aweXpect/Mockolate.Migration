using Moq;

namespace Mockolate.Migration.Example.Tests;

public class Examples
{
	[Fact]
	public async Task MoqCreation()
	{
		Mock<IChocolateDispenser> sut = new();

		sut.Setup(m => m.Dispense(It.IsAny<string>(), It.Is<int>(x => x > 0)))
			.Returns(true);

		IChocolateDispenser x = sut.Object;

		bool result = x.Dispense("Dark", 1);

		await That(result).IsTrue();
	}
}
