using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.MoqAnalyzer,
	Mockolate.Migration.Analyzers.MoqCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class MoqCodeFixProviderTests
{
	public sealed class VerifyEventTests
	{
		[Fact]
		public async Task VerifyAdd_OnDelegateProperty_DoesNotMigrateToSubscribed()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;
				using System;

				public interface IFoo { Action MyHandler { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.VerifyAdd(m => m.MyHandler += It.IsAny<Action>(), Times.AtLeastOnce);
					}
				}
				""",
				"""
				using Moq;
				using System;
				using Mockolate;

				public interface IFoo { Action MyHandler { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.VerifyAdd(m => m.MyHandler += It.IsAny<Action>(), Times.AtLeastOnce);
					}
				}
				""");

		[Fact]
		public async Task VerifyAdd_WithCustomDelegate_MigratesSubscribed()
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
						mock.VerifyAdd(m => m.MyEvent += It.IsAny<FooDelegate>(), Times.AtLeastOnce);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;
				using Mockolate.Verify;

				public delegate void FooDelegate(string type, int amount);
				public interface IFoo { event FooDelegate MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.MyEvent.Subscribed().AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task VerifyAdd_WithNestedEvent_MigratesViaNavigationChain()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;
				using System;

				public interface IBar { event EventHandler MyEvent; }
				public interface IFoo { IBar Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.VerifyAdd(m => m.Child.MyEvent += It.IsAny<EventHandler>(), Times.Never);
					}
				}
				""",
				"""
				using Moq;
				using System;
				using Mockolate;
				using Mockolate.Verify;

				public interface IBar { event EventHandler MyEvent; }
				public interface IFoo { IBar Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Child.Mock.Verify.MyEvent.Subscribed().Never();
					}
				}
				""");

		[Fact]
		public async Task VerifyAdd_WithoutTimes_MigratesToSubscribedAtLeastOnce()
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
						mock.VerifyAdd(m => m.MyEvent += It.IsAny<EventHandler>());
					}
				}
				""",
				"""
				using Moq;
				using System;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.MyEvent.Subscribed().AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task VerifyAdd_WithTimesOnce_MigratesToSubscribedOnce()
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
						mock.VerifyAdd(m => m.MyEvent += It.IsAny<EventHandler>(), Times.Once());
					}
				}
				""",
				"""
				using Moq;
				using System;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.MyEvent.Subscribed().Once();
					}
				}
				""");

		[Fact]
		public async Task VerifyAdd_WithUntranslatableTimes_FallsBackToAtLeastOnce()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;
				using System;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var times = Times.Once();
						var mock = [|new Mock<IFoo>()|];
						mock.VerifyAdd(m => m.MyEvent += It.IsAny<EventHandler>(), times);
					}
				}
				""",
				"""
				using Moq;
				using System;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var times = Times.Once();
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.MyEvent.Subscribed().AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task VerifyRemove_WithoutTimes_MigratesToUnsubscribedAtLeastOnce()
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
						mock.VerifyRemove(m => m.MyEvent -= It.IsAny<EventHandler>());
					}
				}
				""",
				"""
				using Moq;
				using System;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.MyEvent.Unsubscribed().AtLeastOnce();
					}
				}
				""");

		[Fact]
		public async Task VerifyRemove_WithTimesExactly_MigratesToUnsubscribedExactly()
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
						mock.VerifyRemove(m => m.MyEvent -= It.IsAny<EventHandler>(), Times.Exactly(2));
					}
				}
				""",
				"""
				using Moq;
				using System;
				using Mockolate;
				using Mockolate.Verify;

				public interface IFoo { event EventHandler MyEvent; }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Verify.MyEvent.Unsubscribed().Exactly(2);
					}
				}
				""");
	}
}
