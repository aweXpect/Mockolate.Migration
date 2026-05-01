using Mockolate.Migration.NSubstitutePlayground.Domain;
using NSubstitute;

namespace Mockolate.Migration.NSubstitutePlayground;

/// <summary>Substitute creation patterns.</summary>
public class CreationTests
{
	[Fact]
	public async Task SubstituteFor_Class_CanConfigureVirtualMemberReturnValue()
	{
		// Configure a class substitute's virtual member to return a specific value.
		ChocolateRecipe recipe = Substitute.For<ChocolateRecipe>();
		recipe.Name.Returns("Pralines");

		await That(recipe.Name).IsEqualTo("Pralines");
	}

	[Fact]
	public async Task SubstituteFor_MultipleInterfaces_ImplementsBoth()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser, IChocolateAuditor>();

		// The same proxy implements both, so a cast yields the secondary face.
		IChocolateAuditor auditor = (IChocolateAuditor)dispenser;
		auditor.RecordSale("Dark", 2, 3.0m);

		await That(auditor.AuditCount).IsEqualTo(0);
	}

	[Fact]
	public async Task SubstituteFor_SingleInterface_CreatesLooseSubstitute()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();

		// Loose substitute returns default (false) for unconfigured calls.
		await That(dispenser.Dispense("Dark", 1)).IsFalse();
	}

	[Fact]
	public async Task SubstituteForPartsOf_CallsRealVirtualMembers()
	{
		ChocolateRecipe recipe = Substitute.ForPartsOf<ChocolateRecipe>();

		// Virtual members fall through to the real implementation unless configured.
		await That(recipe.Validate()).IsTrue();
		await That(recipe.Name).IsEqualTo("Truffle");
	}

	[Fact]
	public async Task SubstituteForTypeForwardingTo_ForwardsCallsToConcreteImpl()
	{
		IChocolateAuditor auditor = Substitute.ForTypeForwardingTo<IChocolateAuditor, StaticAuditor>();

		auditor.RecordSale("Dark", 1, 1.5m);

		// The forwarded StaticAuditor increments its counter.
		await That(auditor.AuditCount).IsEqualTo(1);
	}
}
