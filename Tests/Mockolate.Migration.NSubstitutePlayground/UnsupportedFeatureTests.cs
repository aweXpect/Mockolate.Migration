using Mockolate.Migration.NSubstitutePlayground.Domain;
using NSubstitute;
using NSubstitute.ClearExtensions;
using NSubstitute.ExceptionExtensions;
using NSubstitute.Extensions;

namespace Mockolate.Migration.NSubstitutePlayground;

/// <summary>
///     NSubstitute features the migration does NOT yet handle. They still pass against NSubstitute;
///     run the migration to see what is/isn't transformed and where manual rewrites are needed.
/// </summary>
public class UnsupportedFeatureTests
{
	// NOT YET MIGRATED: Arg.Do<T>(action) — capture each invocation's argument
	[Fact]
	public async Task ArgDo_capturesEveryInvocationArgument()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		List<int> amounts = new();
		dispenser.Dispense("Dark", Arg.Do<int>(a => amounts.Add(a))).Returns(true);

		_ = dispenser.Dispense("Dark", 2);
		_ = dispenser.Dispense("Dark", 5);

		await That(amounts).IsEqualTo([2, 5,]).InAnyOrder();
	}

	// NOT YET MIGRATED: Arg.Invoke<...> to call back into a delegate parameter
	[Fact]
	public async Task ArgInvoke_callsThroughDelegateParameter()
	{
		// Use a mini-substitute with an Action parameter to exercise Arg.Invoke.
		IInvokeTarget target = Substitute.For<IInvokeTarget>();
		int called = 0;
		target.Run(Arg.Invoke()); // when target.Run(action) is called, NSubstitute invokes action()

		target.Run(() => called++);

		await That(called).IsEqualTo(1);
	}

	// NOT YET MIGRATED: CallInfo argument access via x => x.Arg<T>() / x.ArgAt<T>(index)
	[Fact]
	public async Task CallInfo_argumentAccessInReturns()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Dispense(Arg.Any<string>(), Arg.Any<int>())
			.Returns(call =>
			{
				string type = call.Arg<string>();
				int amount = call.ArgAt<int>(1);
				return type == "Dark" && amount > 0;
			});

		await That(dispenser.Dispense("Dark", 4)).IsTrue();
		await That(dispenser.Dispense("Milk", 4)).IsFalse();
		await That(dispenser.Dispense("Dark", 0)).IsFalse();
	}

	// NOT YET MIGRATED: ClearSubstitute — removes setups and call history together
	[Fact]
	public async Task ClearSubstitute_resetsBothCallsAndSetups()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Dispense("Dark", 1).Returns(true);
		_ = dispenser.Dispense("Dark", 1);

		dispenser.ClearSubstitute();

		// After clear: no calls remembered, and the previous Returns(true) is gone.
		dispenser.DidNotReceive().Dispense("Dark", 1);
		await That(dispenser.Dispense("Dark", 1)).IsFalse();
	}

	// NOT YET MIGRATED: Configure() — re-enter setup mode after calls have been recorded
	[Fact]
	public async Task Configure_changesReturnAfterUse()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.Dispense("Dark", 1).Returns(false);

		_ = dispenser.Dispense("Dark", 1);
		dispenser.Configure().Dispense("Dark", 1).Returns(true);

		await That(dispenser.Dispense("Dark", 1)).IsTrue();
	}

	// NOT YET MIGRATED: Out parameters via Arg.Any<T>() with discard pattern
	[Fact]
	public async Task OutParameter_isSetByCallback()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser
			.TryReserve(Arg.Any<string>(), out Arg.Any<int>())
			.Returns(call =>
			{
				call[1] = 7;
				return true;
			});

		bool ok = dispenser.TryReserve("Dark", out int reserved);

		await That(ok).IsTrue();
		await That(reserved).IsEqualTo(7);
	}

	// NOT YET MIGRATED: Received.InOrder for ordered cross-substitute verification
	[Fact]
	public async Task ReceivedInOrder_ordersAcrossSubstitutes()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		IChocolateAuditor auditor = Substitute.For<IChocolateAuditor>();
		dispenser.Dispense(Arg.Any<string>(), Arg.Any<int>()).Returns(true);

		_ = dispenser.Dispense("Dark", 1);
		auditor.RecordSale("Dark", 1, 1.5m);

		Received.InOrder(() =>
		{
			dispenser.Dispense("Dark", 1);
			auditor.RecordSale("Dark", 1, 1.5m);
		});
	}

	// NOT YET MIGRATED: ReturnsNull / ReturnsNullForAnyArgs (extension methods)
	[Fact]
	public async Task ReturnsNull_isShortcutForReturnsDefault()
	{
		IChocolateFactory factory = Substitute.For<IChocolateFactory>();
		factory.BatchBakeAsync(Arg.Any<IEnumerable<string>>()).Returns(Task.FromResult<IReadOnlyList<ChocolateBar>>(null!));

		IReadOnlyList<ChocolateBar> result = await factory.BatchBakeAsync(["Dark", "Milk",]);
		await That(result).IsNull();
	}

	// NOT YET MIGRATED: ThrowsAsync extension on async setups
	[Fact]
	public async Task ThrowsAsync_appliesToAwaitedTask()
	{
		IChocolateDispenser dispenser = Substitute.For<IChocolateDispenser>();
		dispenser.DispenseAsync("Dark", 1).ThrowsAsync(new TimeoutException());

		Task<bool> Act()
		{
			return dispenser.DispenseAsync("Dark", 1);
		}

		await That((Func<Task<bool>>)Act).Throws<TimeoutException>();
	}

	public interface IInvokeTarget
	{
		void Run(Action action);
	}
}
