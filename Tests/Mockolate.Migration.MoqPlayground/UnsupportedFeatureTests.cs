using Mockolate.Migration.MoqPlayground.Domain;
using Moq;
using Moq.Protected;

namespace Mockolate.Migration.MoqPlayground;

using It = Moq.It;
using MockBehavior = Moq.MockBehavior;

/// <summary>
///     Moq features the migration does NOT yet handle. They still pass against Moq;
///     run the migration to see what is/isn't transformed and where manual rewrites are needed.
/// </summary>
public class UnsupportedFeatureTests
{
	// NOT YET MIGRATED: CallBase = true (delegate to base implementation)
	[Fact]
	public async Task CallBase_invokesBaseClassImplementation()
	{
		Mock<ChocolateRecipe> recipe = new()
		{
			CallBase = true,
		};

		// Validate() falls through to base, which checks Name is non-empty
		bool ok = recipe.Object.Validate();

		await That(ok).IsTrue();
	}

	// NOT YET MIGRATED: Custom DefaultValueProvider
	[Fact]
	public async Task DefaultValueProviderMock_returnsAutoMockedReferenceTypes()
	{
		Mock<IChocolateFactory> factory = new()
		{
			DefaultValue = DefaultValue.Mock,
		};

		// Calling an unconfigured Task-returning member yields a Task that completes
		// (with an auto-mocked default), rather than null/NullReferenceException.
		IReadOnlyList<ChocolateBar> bars = await factory.Object.BatchBakeAsync(["auto"]);

		await That(bars).IsNotNull();
	}

	// NOT YET MIGRATED: It.IsNotIn / It.IsIn (set membership matchers)
	[Fact]
	public async Task ItIsIn_acceptsAnyValueFromTheSet()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense(It.IsIn("Dark", "Milk"), 1)).Returns(true);

		await That(dispenser.Object.Dispense("Dark", 1)).IsTrue();
		await That(dispenser.Object.Dispense("Milk", 1)).IsTrue();
		await That(dispenser.Object.Dispense("White", 1)).IsFalse();
	}

	// NOT YET MIGRATED: Mock.As<T>() to add a secondary interface
	[Fact]
	public async Task MockAs_castToAdditionalInterface()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.As<IChocolateAuditor>()
			.Setup(a => a.AuditCount).Returns(7);

		IChocolateAuditor auditor = (IChocolateAuditor)dispenser.Object;
		await That(auditor.AuditCount).IsEqualTo(7);
	}

	// NOT YET MIGRATED: Mock.Of<T>() (LINQ to Mocks)
	[Fact]
	public async Task MockOf_setsUpAllReturnsImplicitly()
	{
		IChocolateDispenser dispenser = Moq.Mock.Of<IChocolateDispenser>(d =>
			d.Name == "Quick" &&
			d.TotalDispensed == 99);

		await That(dispenser.Name).IsEqualTo("Quick");
		await That(dispenser.TotalDispensed).IsEqualTo(99);
	}

	// NOT YET MIGRATED: MockRepository for grouped Verifiable + VerifyAll
	[Fact]
	public async Task MockRepository_groupsAndVerifiesAllInOneShot()
	{
		MockRepository repo = new(MockBehavior.Strict);
		Mock<IChocolateDispenser> dispenser = repo.Create<IChocolateDispenser>();
		Mock<IChocolateFactory> factory = repo.Create<IChocolateFactory>();
		dispenser.Setup(d => d.Dispense("Dark", 1)).Returns(true);
		factory.Setup(f => f.RegisterRecipe("Truffle")).Returns(true);

		_ = dispenser.Object.Dispense("Dark", 1);
		_ = factory.Object.RegisterRecipe("Truffle");

		repo.VerifyAll();
	}

	// NOT YET MIGRATED: Protected() to set up protected virtual members
	[Fact]
	public async Task Protected_setupOfProtectedMethod()
	{
		Mock<ChocolateRecipe> recipe = new();
		recipe.Protected().Setup<int>("InternalSecret").Returns(123);

		int secret = recipe.Object.CallInternalSecret();

		await That(secret).IsEqualTo(123);
	}

	// NOT YET MIGRATED: Returns overload that takes the Mock itself (mock.Object self-reference)
	[Fact]
	public async Task Returns_factoryWithCapturedMock_canReadOtherSetups()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.SetupGet(d => d.TotalDispensed).Returns(42);
		dispenser
			.Setup(d => d.CountByType(It.IsAny<string>()))
			.Returns(() => dispenser.Object.TotalDispensed / 2);

		await That(dispenser.Object.CountByType("Dark")).IsEqualTo(21);
	}

	// NOT YET MIGRATED: SetupAllProperties stubs every readable+writable property
	[Fact]
	public async Task SetupAllProperties_makesAllPropertiesStateful()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.SetupAllProperties();

		dispenser.Object.Name = "All-Stub";
		dispenser.Object.TotalDispensed = 12;

		await That(dispenser.Object.Name).IsEqualTo("All-Stub");
		await That(dispenser.Object.TotalDispensed).IsEqualTo(12);
	}

	// NOT YET MIGRATED: Strict mock with Verifiable() chain to check that every verifiable setup ran
	[Fact]
	public async Task Strict_withVerifiableSetups_passesWhenAllAreInvoked()
	{
		Mock<IChocolateDispenser> dispenser = new(MockBehavior.Strict);
		dispenser.Setup(d => d.Dispense("Dark", 1)).Returns(true).Verifiable();
		dispenser.Setup(d => d.Dispense("Milk", 1)).Returns(true).Verifiable();

		_ = dispenser.Object.Dispense("Dark", 1);
		_ = dispenser.Object.Dispense("Milk", 1);

		dispenser.Verify();
	}

	// NOT YET MIGRATED: Verifiable() + mock.Verify()
	[Fact]
	public async Task VerifiableSetup_andMockVerify()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense("Dark", 1)).Returns(true).Verifiable();

		_ = dispenser.Object.Dispense("Dark", 1);

		dispenser.Verify();
	}

	// NOT YET MIGRATED: VerifyAll() / VerifyNoOtherCalls()
	[Fact]
	public async Task VerifyAll_andVerifyNoOtherCalls()
	{
		Mock<IChocolateDispenser> dispenser = new();
		dispenser.Setup(d => d.Dispense("Dark", 1)).Returns(true);

		_ = dispenser.Object.Dispense("Dark", 1);

		dispenser.VerifyAll();
		dispenser.VerifyNoOtherCalls();
	}
}
