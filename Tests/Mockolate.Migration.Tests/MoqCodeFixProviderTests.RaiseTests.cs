using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.MoqAnalyzer,
	Mockolate.Migration.Analyzers.MoqCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class MoqCodeFixProviderTests
{
	public sealed class RaiseTests
	{
		[Fact]
		public async Task WithEventArgs_PrependsNullSender()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;
				using System;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Raise(m => m.MyEvent += null, EventArgs.Empty);
					}
				}
				""",
				"""
				using Moq;
				using System;
				using Mockolate;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Raise.MyEvent(null, EventArgs.Empty);
					}
				}
				""");

		[Fact]
		public async Task WithMultipleArgs_MigratesRaise()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public delegate void FooDelegate(string type, int amount);
				public interface IFoo { event FooDelegate MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Raise(m => m.MyEvent += null, "foo", 3);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public delegate void FooDelegate(string type, int amount);
				public interface IFoo { event FooDelegate MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Raise.MyEvent("foo", 3);
					}
				}
				""");
	}
}
