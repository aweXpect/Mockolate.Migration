namespace Mockolate.Migration.Tests;

public sealed class DummyTests
{
	[Fact]
	public async Task WhenPathIsAbsolute_ShouldSucceed()
	{
		string path = "/foo";

		async Task Act()
			=> await That(path).IsNotEmpty();

		await That(Act).DoesNotThrow();
	}
}
