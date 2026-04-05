using Mockolate.Verify;
using Moq;

namespace Mockolate.Migration.Example.Tests;

public class Examples
{
	[Fact]
	public async Task ExpectedMigrationResult()
	{
		IChocolateDispenser sut = IChocolateDispenser.CreateMock();

		sut.Mock.Setup.Dispense(It.IsAny<string>(), It.Satisfies<int>(a => a > 0))
			.Returns(true);

		IChocolateDispenser x = sut;

		bool result = x.Dispense("Dark", 1);
		sut.Mock.Verify.Dispense(It.Matches("foo").AsRegex(), It.Satisfies<int>(a => a > 2)).Never();

		await That(result).IsTrue();
	}

	[Fact]
	public async Task MoqCreation()
	{
#pragma warning disable MockolateM001
		Mock<IChocolateDispenser> sut = new();
#pragma warning restore MockolateM001

		sut.Setup(m => m.Dispense(Moq.It.IsAny<string>(), Moq.It.Is<int>(x => x > 0)))
			.Returns(true);

		IChocolateDispenser x = sut.Object;

		bool result = x.Dispense("Dark", 1);

		sut.Verify(m => m.Dispense(Moq.It.IsRegex("foo"), Moq.It.Is<int>(a => a > 2)), Times.Never);
		await That(result).IsTrue();
	}
}
