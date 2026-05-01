using Mockolate.Migration.NSubstitutePlayground.Domain;
using NSubstitute;

namespace Mockolate.Migration.NSubstitutePlayground;

/// <summary>When/Do — used to attach side effects to void members and to substitute for partial mocks.</summary>
public class WhenDoTests
{
	[Fact]
	public async Task When_DoNotCallBase_DisablesPartialBaseInvocation()
	{
		// ForPartsOf normally calls real virtual methods. DoNotCallBase suppresses that.
		ChocolateRecipe recipe = Substitute.ForPartsOf<ChocolateRecipe>();
		recipe.When(r => r.Reset())
			.DoNotCallBase();

		recipe.Name = "Praline";
		recipe.Reset(); // would normally reset Name to "Truffle"

		await That(recipe.Name).IsEqualTo("Praline");
	}

	[Fact]
	public async Task When_VoidMethod_RunsCallback()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		string? captured = null;
		dispenser.When(d => d.Notify("Dark", 1))
			.Do(_ => captured = "ran");

		dispenser.Notify("Dark", 1);

		await That(captured).IsEqualTo("ran");
	}

	[Fact]
	public async Task WhenForAnyArgs_VoidMethod_RunsCallbackForAnyInvocation()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		int count = 0;
		dispenser.WhenForAnyArgs(d => d.Notify(default!, default))
			.Do(_ => count++);

		dispenser.Notify("Dark", 1);
		dispenser.Notify("Milk", 9);

		await That(count).IsEqualTo(2);
	}
}
