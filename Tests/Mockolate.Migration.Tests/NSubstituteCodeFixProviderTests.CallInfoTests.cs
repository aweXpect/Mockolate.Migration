using Verifier = Mockolate.Migration.Tests.Verifiers.CSharpCodeFixVerifier<Mockolate.Migration.Analyzers.NSubstituteAnalyzer,
	Mockolate.Migration.Analyzers.NSubstituteCodeFixProvider>;

namespace Mockolate.Migration.Tests;

public partial class NSubstituteCodeFixProviderTests
{
	public sealed class CallInfoTests
	{
		public sealed class AndDoesPath
		{
			[Fact]
			public async Task AmbiguousArgGeneric_FallsBackToTodo()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using System;
					using NSubstitute;

					public interface IFoo { int Bar(int x, int y); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Bar(0, 0).Returns(0).AndDoes(call => Console.WriteLine(call.Arg<int>()));
						}
					}
					""",
					"""
					using System;
					using NSubstitute;
					using Mockolate;

					public interface IFoo { int Bar(int x, int y); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							// TODO: review CallInfo usage manually — Mockolate's Do/Returns take typed parameters, not CallInfo
							sub.Mock.Setup.Bar(0, 0).Returns(0).Do(call => Console.WriteLine(call.Arg<int>()));
						}
					}
					""");

			[Fact]
			public async Task AssignThroughIndexer_FallsBackToTodo()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using NSubstitute;

					public interface IFoo { bool TryGet(string key, out int value); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.TryGet("k", out _).Returns(true).AndDoes(call => { call[1] = 42; });
						}
					}
					""",
					"""
					using NSubstitute;
					using Mockolate;

					public interface IFoo { bool TryGet(string key, out int value); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							// TODO: review CallInfo usage manually — Mockolate's Do/Returns take typed parameters, not CallInfo
							sub.Mock.Setup.TryGet("k", out _).Returns(true).Do(call => { call[1] = 42; });
						}
					}
					""");

			[Fact]
			public async Task BodyDoesNotReferenceCallInfo_DropsParameter()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using NSubstitute;

					public interface IFoo { int Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							int counter = 0;
							sub.Bar(1, "a").Returns(42).AndDoes(call => counter++);
						}
					}
					""",
					"""
					using NSubstitute;
					using Mockolate;

					public interface IFoo { int Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							int counter = 0;
							sub.Mock.Setup.Bar(1, "a").Returns(42).Do(() => counter++);
						}
					}
					""");

			[Fact]
			public async Task BodyUsesArgAtAndIndexer_RewritesToTypedParameters()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using System;
					using NSubstitute;

					public interface IFoo { int Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Bar(0, "").Returns(0).AndDoes(call => Console.WriteLine(call.ArgAt<int>(0) + ":" + call[1]));
						}
					}
					""",
					"""
					using System;
					using NSubstitute;
					using Mockolate;

					public interface IFoo { int Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							sub.Mock.Setup.Bar(0, "").Returns(0).Do((int x, string y) => Console.WriteLine(x + ":" + y));
						}
					}
					""");

			[Fact]
			public async Task BodyUsesArgGenericByType_RewritesWhenUnique()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using System;
					using NSubstitute;

					public interface IFoo { int Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Bar(0, "").Returns(0).AndDoes(call => Console.WriteLine(call.Arg<string>()));
						}
					}
					""",
					"""
					using System;
					using NSubstitute;
					using Mockolate;

					public interface IFoo { int Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							sub.Mock.Setup.Bar(0, "").Returns(0).Do((int x, string y) => Console.WriteLine(y));
						}
					}
					""");
		}

		public sealed class WhenDoPath
		{
			[Fact]
			public async Task BodyUsesArgAt_RewritesToTypedParameters()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using System;
					using NSubstitute;

					public interface IFoo { void Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.When(s => s.Bar(0, "")).Do(call => Console.WriteLine(call.ArgAt<int>(0)));
						}
					}
					""",
					"""
					using System;
					using NSubstitute;
					using Mockolate;

					public interface IFoo { void Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							sub.Mock.Setup.Bar(0, "").Do((int x, string y) => Console.WriteLine(x));
						}
					}
					""");

			[Fact]
			public async Task MultiArgMethod_BodyIgnoresCallInfo_DropsParameter()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using NSubstitute;

					public interface IFoo { void Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							int counter = 0;
							sub.When(x => x.Bar(1, "a")).Do(call => counter++);
						}
					}
					""",
					"""
					using NSubstitute;
					using Mockolate;

					public interface IFoo { void Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							int counter = 0;
							sub.Mock.Setup.Bar(1, "a").Do(() => counter++);
						}
					}
					""");
		}

		public sealed class ReturnsPath
		{
			[Fact]
			public async Task LambdaIgnoresCallInfo_DropsParameter()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using NSubstitute;

					public interface IFoo { int Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Bar(0, "").Returns(call => 42);
						}
					}
					""",
					"""
					using NSubstitute;
					using Mockolate;

					public interface IFoo { int Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							sub.Mock.Setup.Bar(0, "").Returns(() => 42);
						}
					}
					""");

			[Fact]
			public async Task LambdaUsesArgAt_OnReceiverWithKeywordParameterName_EscapesIdentifier()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using NSubstitute;

					public interface IFoo { string Trigger(string @event); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Trigger("x").Returns(call => call.ArgAt<string>(0));
						}
					}
					""",
					"""
					using NSubstitute;
					using Mockolate;

					public interface IFoo { string Trigger(string @event); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							sub.Mock.Setup.Trigger("x").Returns((string @event) => @event);
						}
					}
					""");

			[Fact]
			public async Task LambdaUsesArgAt_RewritesToTypedParameters()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using NSubstitute;

					public interface IFoo { int Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Bar(0, "").Returns(call => call.ArgAt<int>(0) * 2);
						}
					}
					""",
					"""
					using NSubstitute;
					using Mockolate;

					public interface IFoo { int Bar(int x, string y); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							sub.Mock.Setup.Bar(0, "").Returns((int x, string y) => x * 2);
						}
					}
					""");

			[Fact]
			public async Task LambdaWithBareCallInfo_FallsBackToTodo()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using NSubstitute;
					using NSubstitute.Core;

					public interface IFoo { int Bar(int x); }

					public class Tests
					{
						int Helper(CallInfo c) => 1;

						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Bar(0).Returns(call => Helper(call));
						}
					}
					""",
					"""
					using NSubstitute;
					using NSubstitute.Core;
					using Mockolate;

					public interface IFoo { int Bar(int x); }

					public class Tests
					{
						int Helper(CallInfo c) => 1;

						public void Test()
						{
							var sub = IFoo.CreateMock();
							// TODO: review CallInfo usage manually — Mockolate's Do/Returns take typed parameters, not CallInfo
							sub.Mock.Setup.Bar(0).Returns(call => Helper(call));
						}
					}
					""");

			[Fact]
			public async Task LambdaWithForeachVariableShadowingReceiverParameter_FallsBackToTodo()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using NSubstitute;

					public interface IFoo { int Bar(int x); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Bar(0).Returns(call =>
							{
								foreach (var x in new[] { 1, 2 }) { _ = x; }
								return call.ArgAt<int>(0);
							});
						}
					}
					""",
					"""
					using NSubstitute;
					using Mockolate;

					public interface IFoo { int Bar(int x); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							// TODO: review CallInfo usage manually — Mockolate's Do/Returns take typed parameters, not CallInfo
							sub.Mock.Setup.Bar(0).Returns(call =>
							{
								foreach (var x in new[] { 1, 2 }) { _ = x; }
								return call.ArgAt<int>(0);
							});
						}
					}
					""");

			[Fact]
			public async Task LambdaWithLocalShadowingParameterName_FallsBackToTodo()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using NSubstitute;

					public interface IFoo { bool Dispense(string type, int amount); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Dispense("Dark", 1).Returns(call =>
							{
								string type = call.Arg<string>();
								int amount = call.ArgAt<int>(1);
								return type == "Dark" && amount > 0;
							});
						}
					}
					""",
					"""
					using NSubstitute;
					using Mockolate;

					public interface IFoo { bool Dispense(string type, int amount); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							// TODO: review CallInfo usage manually — Mockolate's Do/Returns take typed parameters, not CallInfo
							sub.Mock.Setup.Dispense("Dark", 1).Returns(call =>
							{
								string type = call.Arg<string>();
								int amount = call.ArgAt<int>(1);
								return type == "Dark" && amount > 0;
							});
						}
					}
					""");

			[Fact]
			public async Task LambdaWithNestedLambdaParameterShadowingReceiverParameter_FallsBackToTodo()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using System;
					using NSubstitute;

					public interface IFoo { int Bar(int x); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Bar(0).Returns(call =>
							{
								Action<int> a = (x) => Console.WriteLine(x);
								return call.ArgAt<int>(0);
							});
						}
					}
					""",
					"""
					using System;
					using NSubstitute;
					using Mockolate;

					public interface IFoo { int Bar(int x); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							// TODO: review CallInfo usage manually — Mockolate's Do/Returns take typed parameters, not CallInfo
							sub.Mock.Setup.Bar(0).Returns(call =>
							{
								Action<int> a = (x) => Console.WriteLine(x);
								return call.ArgAt<int>(0);
							});
						}
					}
					""");

			[Fact]
			public async Task LambdaWithNestedLambdaShadowingCallInfoName_DropsParameter()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using System;
					using NSubstitute;

					public interface IFoo { int Bar(int x); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Bar(0).Returns(call => ((Func<int, int>)(call => call + 1))(7));
						}
					}
					""",
					"""
					using System;
					using NSubstitute;
					using Mockolate;

					public interface IFoo { int Bar(int x); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							sub.Mock.Setup.Bar(0).Returns(() => ((Func<int, int>)(call => call + 1))(7));
						}
					}
					""");

			[Fact]
			public async Task MixedSequence_PerArgRewritePreservesValuesAndRewritesLambdas()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using NSubstitute;

					public interface IFoo { int Bar(int x); }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Bar(0).Returns(1, call => call.ArgAt<int>(0), 3);
						}
					}
					""",
					"""
					using NSubstitute;
					using Mockolate;

					public interface IFoo { int Bar(int x); }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							sub.Mock.Setup.Bar(0).Returns(1).Returns((int x) => x).Returns(3);
						}
					}
					""");

			[Fact]
			public async Task OnProperty_LambdaIgnoresCallInfo_DropsParameter()
				=> await Verifier.VerifyCodeFixAsync(
					"""
					using NSubstitute;

					public interface IFoo { int Value { get; } }

					public class Tests
					{
						public void Test()
						{
							var sub = [|Substitute.For<IFoo>()|];
							sub.Value.Returns(call => 7);
						}
					}
					""",
					"""
					using NSubstitute;
					using Mockolate;

					public interface IFoo { int Value { get; } }

					public class Tests
					{
						public void Test()
						{
							var sub = IFoo.CreateMock();
							sub.Mock.Setup.Value.Returns(() => 7);
						}
					}
					""");
		}
	}
}
