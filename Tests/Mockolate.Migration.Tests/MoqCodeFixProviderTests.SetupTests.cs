using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.MoqAnalyzer,
	Mockolate.Migration.Analyzers.MoqCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class MoqCodeFixProviderTests
{
	public sealed class SetupTests
	{
		[Fact]
		public async Task AsyncMethod_WithReturnsAsync_MigratesSetup()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;
				using System.Threading.Tasks;

				public interface IFoo { Task<bool> DoSomethingAsync(); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(foo => foo.DoSomethingAsync()).ReturnsAsync(true);
					}
				}
				""",
				"""
				using Moq;
				using System.Threading.Tasks;
				using Mockolate;

				public interface IFoo { Task<bool> DoSomethingAsync(); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.DoSomethingAsync().ReturnsAsync(true);
					}
				}
				""");

		[Fact]
		public async Task Method_WithItIsIn_MigratedToIsOneOf()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.IsIn<string>(new[] { "A", "B" }))).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.IsOneOf<string>(new[] { "A", "B" })).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task Method_WithItIsInRangeExclusive_ChainsExclusive()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.IsInRange<int>(1, 10, Range.Exclusive))).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.IsInRange<int>(1, 10).Exclusive()).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task Method_WithItIsInRangeInclusive_DropsRangeArg()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(Moq.It.IsInRange<int>(1, 10, Range.Inclusive))).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar(int x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.IsInRange<int>(1, 10)).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task Method_WithItIsLambda_MigratedToSatisfies()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.Is<string>(s => s.StartsWith("A")))).Returns(true);
						mock.Setup(m => m.Bar(It.Is<string>((x) => x.StartsWith("B"))))
							.Returns(false);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.Satisfies<string>(s => s.StartsWith("A"))).Returns(true);
						mock.Mock.Setup.Bar(It.Satisfies<string>((x) => x.StartsWith("B")))
							.Returns(false);
					}
				}
				""");

		[Fact]
		public async Task Method_WithItIsRegex_MigratedToMatchesAsRegex()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.IsRegex("^A"))).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.Matches("^A").AsRegex()).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task Method_WithItIsTwoArgs_MigratedToIs()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;
				using System;
				using System.Collections.Generic;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(m => m.Bar(It.Is<string>("hello", StringComparer.OrdinalIgnoreCase))).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using System;
				using System.Collections.Generic;
				using Mockolate;

				public interface IFoo { bool Bar(string x); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Bar(It.Is<string>("hello").Using(StringComparer.OrdinalIgnoreCase)).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task Method_WithOutParameter_MigratedToIsOut()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { bool TryParse(string value, out string outputValue); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						string outString = "ack";
						mock.Setup(foo => foo.TryParse("ping", out outString)).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { bool TryParse(string value, out string outputValue); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						string outString = "ack";
						mock.Mock.Setup.TryParse("ping", It.IsOut(() => outString)).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task Method_WithRefItRefIsAny_MigratedToIsAnyRef()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public class Bar { }

				public interface IFoo { bool Submit(ref Bar bar); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(foo => foo.Submit(ref Moq.It.Ref<Bar>.IsAny)).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public class Bar { }

				public interface IFoo { bool Submit(ref Bar bar); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Submit(It.IsAnyRef<Bar>()).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task Method_WithRefParameter_MigratedToIsRef()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public class Bar { }

				public interface IFoo { bool Submit(ref Bar bar); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						Bar instance = new();
						mock.Setup(foo => foo.Submit(ref instance)).Returns(true);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public class Bar { }

				public interface IFoo { bool Submit(ref Bar bar); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						Bar instance = new();
						mock.Mock.Setup.Submit(It.IsRef<Bar>(_ => instance)).Returns(true);
					}
				}
				""");

		[Fact]
		public async Task Property_MigratesSetup()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(foo => foo.Name).Returns("bar");
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Name.Returns("bar");
					}
				}
				""");

		[Fact]
		public async Task Property_Nested_MigratesSetup()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public class Baz { public virtual string Name { get; set; } = ""; }
				public class Bar { public virtual Baz Baz { get; set; } }

				public interface IFoo { Bar Bar { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.Setup(foo => foo.Bar.Baz.Name).Returns("baz");
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public class Baz { public virtual string Name { get; set; } = ""; }
				public class Bar { public virtual Baz Baz { get; set; } }

				public interface IFoo { Bar Bar { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Bar.Baz.Mock.Setup.Name.Returns("baz");
					}
				}
				""");

		[Fact]
		public async Task SetupProperty_WithDefault_MigratesInitializeWith()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.SetupProperty(f => f.Name, "foo");
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Name.InitializeWith("foo");
					}
				}
				""");

		[Fact]
		public async Task SetupProperty_WithoutDefault_MigratesRegister()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.SetupProperty(f => f.Name);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Name.Register();
					}
				}
				""");

		[Fact]
		public async Task SetupSequence_Method_WithReturnsAndThrowsChain_MigratesToSetup()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using System;
				using Moq;

				public interface IFoo { bool Dispense(string value, int count); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.SetupSequence(f => f.Dispense(It.IsAny<string>(), It.IsAny<int>()))
							.Returns(true)
							.Throws(new Exception("Error"))
							.Returns(false);
					}
				}
				""",
				"""
				using System;
				using Moq;
				using Mockolate;

				public interface IFoo { bool Dispense(string value, int count); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Dispense(It.IsAny<string>(), It.IsAny<int>())
							.Returns(true)
							.Throws(new Exception("Error"))
							.Returns(false);
					}
				}
				""");

		[Fact]
		public async Task SetupSequence_Method_WithReturnsChain_MigratesToSetup()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { int GetCount(); }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.SetupSequence(f => f.GetCount()).Returns(1).Returns(2);
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { int GetCount(); }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.GetCount().Returns(1).Returns(2);
					}
				}
				""");

		[Fact]
		public async Task SetupSequence_NestedMethod_UsesNavigationChain()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using System;
				using Moq;

				public interface IBar { int GetCount(); }
				public interface IFoo { IBar Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.SetupSequence(f => f.Child.GetCount())
							.Returns(1)
							.Throws<InvalidOperationException>();
					}
				}
				""",
				"""
				using System;
				using Moq;
				using Mockolate;

				public interface IBar { int GetCount(); }
				public interface IFoo { IBar Child { get; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Child.Mock.GetCount()
							.Returns(1)
							.Throws<InvalidOperationException>();
					}
				}
				""");

		[Fact]
		public async Task SetupSequence_Property_WithReturnsChain_MigratesToSetup()
			=> await Verifier.VerifyCodeFixAsync(
				"""
				using Moq;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = [|new Mock<IFoo>()|];
						mock.SetupSequence(f => f.Name).Returns("a").Returns("b");
					}
				}
				""",
				"""
				using Moq;
				using Mockolate;

				public interface IFoo { string Name { get; set; } }

				public class Tests
				{
					public void Test()
					{
						var mock = IFoo.CreateMock();
						mock.Mock.Setup.Name.Returns("a").Returns("b");
					}
				}
				""");
	}
}
