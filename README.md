# Mockolate.Migration

[![Nuget](https://img.shields.io/nuget/v/Mockolate.Migration)](https://www.nuget.org/packages/Mockolate.Migration)
[![Build](https://github.com/aweXpect/Mockolate.Migration/actions/workflows/build.yml/badge.svg)](https://github.com/aweXpect/Mockolate.Migration/actions/workflows/build.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=aweXpect_Mockolate.Migration&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=aweXpect_Mockolate.Migration)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=aweXpect_Mockolate.Migration&metric=coverage)](https://sonarcloud.io/summary/overall?id=aweXpect_Mockolate.Migration)

A Roslyn analyzer and code-fix provider that migrates [Moq](https://github.com/devlooped/moq) and
[NSubstitute](https://nsubstitute.github.io) usage to
[Mockolate](https://github.com/aweXpect/Mockolate). Drop the package into a project that uses one of
those libraries and the analyzer flags each mock it can migrate; the accompanying code fix rewrites
the setup, verification, and event APIs to their Mockolate equivalents.

## Installation

Install the NuGet package into the project you want to migrate:

```shell
dotnet add package Mockolate.Migration
```

The package only needs to be referenced while you are migrating — it ships the analyzer and code fixer,
not runtime code. Once the source library is gone from a project you can remove the reference again.

## Usage

After installing the package, every supported construct is reported as a warning. Apply the relevant
code fix from your IDE (Visual Studio, Rider, VS Code with C# Dev Kit) or via
`dotnet format analyzers` to rewrite the call site.

| Diagnostic    | Source library | Code fix title                  |
|---------------|----------------|---------------------------------|
| `MockolateM001` | Moq            | *Migrate Moq to Mockolate*       |
| `MockolateM002` | NSubstitute    | *Migrate NSubstitute to Mockolate* |

The fixer rewrites the whole mock — the construction call, all setup calls on that mock, the
corresponding verification calls, event wiring, and trailing `.Object` accesses (Moq) — in a single
step.

## Supported Moq migrations

| Moq construct                                            | Rewritten to                                                                |
|----------------------------------------------------------|-----------------------------------------------------------------------------|
| `new Mock<IFoo>()`                                       | `IFoo.CreateMock()`                                                         |
| `new Mock<IFoo>(MockBehavior.Strict)`                    | `IFoo.CreateMock(MockBehavior.Default.ThrowingWhenNotSetup())`              |
| `sut.Object`                                             | `sut` (the `.Object` access is dropped)                                     |
| `sut.Setup(m => m.Method(args))`                         | `sut.Mock.Setup.Method(args)`                                               |
| `sut.Setup(m => m.Prop)` / `sut.SetupGet(...)`           | `sut.Mock.Setup.Prop`                                                       |
| `sut.SetupProperty(m => m.Prop)`                         | `sut.Mock.Setup.Prop.Register()`                                            |
| `sut.SetupProperty(m => m.Prop, value)`                  | `sut.Mock.Setup.Prop.InitializeWith(value)`                                 |
| `sut.SetupSequence(...).Returns(a).Returns(b)`           | `sut.Mock.Setup.Method(...).Returns(a).Returns(b)`                          |
| `.Returns(...)` / `.ReturnsAsync(...)` / `.Throws(...)`  | preserved on the Mockolate setup chain                                      |
| `.Callback(...)`                                         | `.Do(...)` on the Mockolate setup                                           |
| `sut.InSequence(seq).Setup(...)` (and other setup forms) | `.InSequence(...)` wrapper is stripped; use `Verify…Then(...)` for ordering |
| `sut.Verify(m => m.Method(args), Times.Once())`          | `sut.Mock.Verify.Method(args).Once()`                                       |
| `sut.VerifyGet` / `sut.VerifySet`                        | `sut.Mock.Verify.Prop.Got(...)` / `.Set(...)`                               |
| `sut.Raise(m => m.Event += null, args)`                  | `sut.Mock.Raise.Event(sender, args)`                                        |
| `sut.VerifyAdd(m => m.Event += ..., times)`              | `sut.Mock.Verify.Event.Subscribed().<times>()`                              |
| `sut.VerifyRemove(m => m.Event -= ..., times)`           | `sut.Mock.Verify.Event.Unsubscribed().<times>()`                            |
| `It.IsAny<T>()`                                          | preserved as `It.IsAny<T>()`                                                |
| `It.Is<T>(predicate)`                                    | `It.Satisfies<T>(predicate)`                                                |
| `It.IsInRange(lo, hi, Range.Inclusive)`                  | `It.IsInRange(lo, hi)` (the `Range` argument is dropped)                    |
| `It.IsRegex(pattern, options)`                           | `It.Matches(pattern).AsRegex(options)`                                      |
| `It.Ref<T>.IsAny` / `out` parameters                     | `It.IsAnyRef<T>()` / `It.IsOut(() => value)`                                |
| Nested mocks (`sut.Setup(m => m.Child.Prop)`)            | Navigation chain is preserved: `sut.Child.Mock.Setup.Prop`                  |

## Supported NSubstitute migrations

| NSubstitute construct                                              | Rewritten to                                                                          |
|--------------------------------------------------------------------|---------------------------------------------------------------------------------------|
| `Substitute.For<IFoo>()`                                           | `IFoo.CreateMock()`                                                                   |
| `Substitute.For<IFoo>(args)`                                       | `IFoo.CreateMock(args)`                                                               |
| `Substitute.For<IFoo, IBar>()`                                     | `IFoo.CreateMock().Implementing<IBar>()` (chains for additional types)                |
| `Substitute.ForPartsOf<MyClass>()`                                 | `MyClass.CreateMock()` (Mockolate calls base by default)                              |
| `Substitute.ForTypeForwardingTo<TInterface, TClass>(args)`         | `TInterface.CreateMock().Wrapping(new TClass(args))`                                  |
| `sub.Method(args).Returns(v)`                                      | `sub.Mock.Setup.Method(args).Returns(v)`                                              |
| `sub.Method(args).Returns(v1, v2, v3)`                             | `sub.Mock.Setup.Method(args).Returns(v1).Returns(v2).Returns(v3)`                     |
| `sub.Method(args).Throws<E>()` / `Throws(ex)`                      | `sub.Mock.Setup.Method(args).Throws<E>()` / `.Throws(ex)`                             |
| `sub.Method(args).ReturnsForAnyArgs(v)`                            | `sub.Mock.Setup.Method(args).AnyParameters().Returns(v)`                              |
| `sub.Method(args).ThrowsForAnyArgs<E>()`                           | `sub.Mock.Setup.Method(args).AnyParameters().Throws<E>()`                             |
| `sub.Method(args).Returns(v).AndDoes(cb)`                          | `sub.Mock.Setup.Method(args).Returns(v).Do(cb)`                                       |
| `sub.Property.Returns(v)`                                          | `sub.Mock.Setup.Property.Returns(v)`                                                  |
| `sub.Received().Method(args)`                                      | `sub.Mock.Verify.Method(args).AtLeastOnce()`                                          |
| `sub.Received(n).Method(args)`                                     | `sub.Mock.Verify.Method(args).Exactly(n)` (`Once()` for `n == 1`)                     |
| `sub.DidNotReceive().Method(args)`                                 | `sub.Mock.Verify.Method(args).Never()`                                                |
| `sub.ReceivedWithAnyArgs().Method(default, default)`               | `sub.Mock.Verify.Method(default, default).AnyParameters().AtLeastOnce()`              |
| `sub.DidNotReceiveWithAnyArgs().Method(default, default)`          | `sub.Mock.Verify.Method(default, default).AnyParameters().Never()`                    |
| `_ = sub.Received().Prop`                                          | `sub.Mock.Verify.Prop.Got().AtLeastOnce()`                                            |
| `sub.Received().Prop = v`                                          | `sub.Mock.Verify.Prop.Set(v).AtLeastOnce()`                                           |
| `sub.ClearReceivedCalls()`                                         | `sub.Mock.ClearAllInteractions()`                                                     |
| `sub.MyEvent += Raise.Event()`                                     | `sub.Mock.Raise.MyEvent(null, EventArgs.Empty)`                                       |
| `sub.MyEvent += Raise.EventWith(sender, args)`                     | `sub.Mock.Raise.MyEvent(sender, args)`                                                |
| `sub.MyEvent += Raise.EventWith(args)`                             | `sub.Mock.Raise.MyEvent(null, args)`                                                  |
| `sub.MyEvent += Raise.Event<TDelegate>(args...)`                   | `sub.Mock.Raise.MyEvent(args...)` (delegate type dropped)                             |
| `sub.When(x => x.M(args)).Do(cb)`                                  | `sub.Mock.Setup.M(args).Do(cb)`                                                       |
| `sub.When(x => x.M(args)).DoNotCallBase()`                         | `sub.Mock.Setup.M(args).SkippingBaseClass()`                                          |
| `Arg.Any<T>()`                                                     | `It.IsAny<T>()`                                                                       |
| `Arg.Is<T>(predicate)`                                             | `It.Satisfies<T>(predicate)`                                                          |
| `Arg.Is(value)` / `Arg.Is<T>(value)`                               | `It.Is(value)` / `It.Is<T>(value)`                                                    |
| `Arg.Compat.X<T>(...)`                                             | same as the corresponding `Arg.X<T>(...)`                                             |
| Nested mocks (`sub.Child.M(args).Returns(v)`)                      | `sub.Child.Mock.Setup.M(args).Returns(v)` plus a `// TODO` comment to register `Child` |
