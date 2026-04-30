using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.NSubstituteAnalyzer,
	Mockolate.Migration.Analyzers.NSubstituteCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class NSubstituteCodeFixProviderTests
{
	public sealed class RaiseTests
	{
		[Fact]
		public async Task RaiseEvent_DelegateType_ForwardsArgsWithoutType()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using System;
				using NSubstitute;

				public interface IFoo { event Action<int> MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.MyEvent += Raise.Event<Action<int>>(123);
					}
				}
				""",
				"""
				using System;
				using NSubstitute;
				using Mockolate;

				public interface IFoo { event Action<int> MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Raise.MyEvent(123);
					}
				}
				""");

		[Fact]
		public async Task RaiseEvent_NoArgs_RewritesToNullAndEmpty()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using System;
				using NSubstitute;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.MyEvent += Raise.Event();
					}
				}
				""",
				"""
				using System;
				using NSubstitute;
				using Mockolate;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Raise.MyEvent(null, EventArgs.Empty);
					}
				}
				""");

		[Fact]
		public async Task RaiseEventWith_ArgsOnly_PrependsNullSender()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using System;
				using NSubstitute;

				public class MyArgs : EventArgs { }
				public interface IFoo { event EventHandler<MyArgs> MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.MyEvent += Raise.EventWith(new MyArgs());
					}
				}
				""",
				"""
				using System;
				using NSubstitute;
				using Mockolate;

				public class MyArgs : EventArgs { }
				public interface IFoo { event EventHandler<MyArgs> MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Raise.MyEvent(null, new MyArgs());
					}
				}
				""");

		[Fact]
		public async Task RaiseEventWith_SenderAndArgs_PassesThrough()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using System;
				using NSubstitute;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var sub = [|Substitute.For<IFoo>()|];
						sub.MyEvent += Raise.EventWith(this, EventArgs.Empty);
					}
				}
				""",
				"""
				using System;
				using NSubstitute;
				using Mockolate;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var sub = IFoo.CreateMock();
						sub.Mock.Raise.MyEvent(this, EventArgs.Empty);
					}
				}
				""");
	}
}
