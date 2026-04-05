using System.Text.RegularExpressions;
using Moq;
using Range = Moq.Range;

namespace Mockolate.Migration.Example.Tests;

public class MoqMigrationExamples
{
	[Fact]
	public async Task ExpectedMigrationResult()
	{
		IFoo mock = IFoo.CreateMock();
		mock.Mock.Setup.Bar.InitializeWith(Bar.CreateMock());
		mock.Bar.Mock.Setup.Baz.InitializeWith(Baz.CreateMock());
		IFoo mock2 = IFoo.CreateMock(MockBehavior.Default.ThrowingWhenNotSetup());

		mock2.Mock.Setup.DoSomething("ping").Returns(true);

		// out arguments
		string outString = "ack";
		// TryParse will return true, and the out argument will return "ack", lazy evaluated
		mock.Mock.Setup.TryParse("ping", It.IsOut(() => outString)).Returns(true);

		// ref arguments
		Bar instance = new();
		// Only matches if the ref argument to the invocation is the same instance
		mock.Mock.Setup.Submit(It.IsRef<Bar>(_ => instance)).Returns(true);

		// access invocation arguments when returning a value
		mock.Mock.Setup.DoSomethingStringy(It.IsAny<string>())
			.Returns(s => s.ToLower());
		// Multiple parameters overloads available

		// throwing when invoked with specific parameters
		mock.Mock.Setup.DoSomething("reset").Throws<InvalidOperationException>();
		mock.Mock.Setup.DoSomething("").Throws(new ArgumentException("command"));

		// lazy evaluating return value
		int count = 1;
		mock.Mock.Setup.GetCount().Returns(() => count);

		/* ------ Async Methods ------ */

		mock.Mock.Setup.DoSomethingAsync().ReturnsAsync(true);

		/* ------ Matching Argument ------ */
		// any value
		mock.Mock.Setup.DoSomething(It.IsAny<string>()).Returns(true);

		// any value passed in a `ref` parameter (requires Moq 4.8 or later):
		mock.Mock.Setup.Submit(It.IsAnyRef<Bar>()).Returns(true);

		// matching Func<int>, lazy evaluated
		mock.Mock.Setup.Add(It.Satisfies<int>(i => i % 2 == 0)).Returns(true);

		// matching ranges
		mock.Mock.Setup.Add(It.IsInRange(0, 10)).Returns(true);

		// matching regex
		mock.Mock.Setup.DoSomethingStringy(It.Matches("[a-d]+").AsRegex(RegexOptions.IgnoreCase)).Returns("foo");

		/* ------ Properties ------ */
		mock.Mock.Setup.Name.Returns("bar");

		// auto-mocking hierarchies (a.k.a. recursive mocks)
		mock.Bar.Baz.Mock.Setup.Name.Returns("baz");

		// start "tracking" sets/gets to this property
		mock.Mock.Setup.Name.Register();
		// alternatively, provide a default value for the stubbed property
		mock.Mock.Setup.Name.InitializeWith("foo");

		await That(true).IsTrue();
	}

	/// <summary>
	///     <see href="https://github.com/devlooped/moq/wiki/Quickstart" />
	/// </summary>
	[Fact]
	public async Task MoqCreation()
	{
#pragma warning disable MockolateM001
		Mock<IFoo> mock = new();
		Mock<IFoo> mock2 = new(Moq.MockBehavior.Strict);
#pragma warning restore MockolateM001

		mock2.Setup(foo => foo.DoSomething("ping")).Returns(true);

		// out arguments
		string outString = "ack";
		// TryParse will return true, and the out argument will return "ack", lazy evaluated
		mock.Setup(foo => foo.TryParse("ping", out outString)).Returns(true);

		// ref arguments
		Bar instance = new();
		// Only matches if the ref argument to the invocation is the same instance
		mock.Setup(foo => foo.Submit(ref instance)).Returns(true);

		// access invocation arguments when returning a value
		mock.Setup(x => x.DoSomethingStringy(Moq.It.IsAny<string>()))
			.Returns((string s) => s.ToLower());
		// Multiple parameters overloads available

		// throwing when invoked with specific parameters
		mock.Setup(foo => foo.DoSomething("reset")).Throws<InvalidOperationException>();
		mock.Setup(foo => foo.DoSomething("")).Throws(new ArgumentException("command"));

		// lazy evaluating return value
		int count = 1;
		mock.Setup(foo => foo.GetCount()).Returns(() => count);

		/* ------ Async Methods ------ */

		mock.Setup(foo => foo.DoSomethingAsync()).ReturnsAsync(true);

		/* ------ Matching Argument ------ */
		// any value
		mock.Setup(foo => foo.DoSomething(Moq.It.IsAny<string>())).Returns(true);

		// any value passed in a `ref` parameter (requires Moq 4.8 or later):
		mock.Setup(foo => foo.Submit(ref Moq.It.Ref<Bar>.IsAny)).Returns(true);

		// matching Func<int>, lazy evaluated
		mock.Setup(foo => foo.Add(Moq.It.Is<int>(i => i % 2 == 0))).Returns(true);

		// matching ranges
		mock.Setup(foo => foo.Add(Moq.It.IsInRange(0, 10, Range.Inclusive))).Returns(true);

		// matching regex
		mock.Setup(x => x.DoSomethingStringy(Moq.It.IsRegex("[a-d]+", RegexOptions.IgnoreCase))).Returns("foo");

		/* ------ Properties ------ */
		mock.Setup(foo => foo.Name).Returns("bar");

		// auto-mocking hierarchies (a.k.a. recursive mocks)
		mock.Setup(foo => foo.Bar.Baz.Name).Returns("baz");

		// start "tracking" sets/gets to this property
		mock.SetupProperty(f => f.Name);
		// alternatively, provide a default value for the stubbed property
		mock.SetupProperty(f => f.Name, "foo");
		await That(true).IsTrue();
	}

	public interface IFoo
	{
		Bar Bar { get; set; }
		string Name { get; set; }
		int Value { get; set; }
		bool DoSomething(string value);
		bool DoSomething(int number, string value);
		Task<bool> DoSomethingAsync();
		string DoSomethingStringy(string value);
		bool TryParse(string value, out string outputValue);
		bool Submit(ref Bar bar);
		int GetCount();
		bool Add(int value);
	}

	public class Bar
	{
		public virtual Baz Baz { get; set; } = new();
		public virtual bool Submit() => false;
	}

	public class Baz
	{
		public virtual string Name { get; set; } = "";
	}
}
