# Mockolate.Migration

[![Nuget](https://img.shields.io/nuget/v/Mockolate.Migration)](https://www.nuget.org/packages/Mockolate.Migration)
[![Build](https://github.com/aweXpect/Mockolate.Migration/actions/workflows/build.yml/badge.svg)](https://github.com/aweXpect/Mockolate.Migration/actions/workflows/build.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=aweXpect_Mockolate.Migration&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=aweXpect_Mockolate.Migration)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=aweXpect_Mockolate.Migration&metric=coverage)](https://sonarcloud.io/summary/overall?id=aweXpect_Mockolate.Migration)

A Roslyn analyzer and code-fix provider that migrates [Moq](https://github.com/devlooped/moq) usage to
[Mockolate](https://github.com/aweXpect/Mockolate). Drop the package into a project that uses Moq and the
analyzer flags each mock it can migrate; the accompanying code fix rewrites the setup, verification, and
event APIs to their Mockolate equivalents.

## Installation

Install the NuGet package into the project you want to migrate:

```shell
dotnet add package Mockolate.Migration
```

The package only needs to be referenced while you are migrating — it ships the analyzer and code fixer,
not runtime code. Once Moq is gone from a project you can remove the reference again.

## Usage

After installing the package, every supported Moq construct (starting from `new Mock<T>()`) is reported
as a warning with diagnostic id **`MockolateM001`** — *"Moq should be migrated."*. Apply the code fix
*"Migrate Moq to Mockolate"* from your IDE (Visual Studio, Rider, VS Code with C# Dev Kit) or via
`dotnet format analyzers` to rewrite the call site.

The fixer rewrites the whole mock — the `new Mock<T>()` construction, all `Setup…` calls on that mock,
the corresponding `Verify…` calls, event wiring, and trailing `.Object` accesses — in a single step.

## Supported migrations

| Moq construct                                                            | Rewritten to                                                                |
|--------------------------------------------------------------------------|-----------------------------------------------------------------------------|
| `new Mock<IFoo>()`                                                       | `IFoo.CreateMock()`                                                         |
| `new Mock<IFoo>(MockBehavior.Strict)`                                    | `IFoo.CreateMock(MockBehavior.Default.ThrowingWhenNotSetup())`              |
| `mock.Object`                                                            | `mock` (the `.Object` access is dropped)                                    |
| `mock.Setup(m => m.Method(args))`                                        | `mock.Mock.Setup.Method(args)`                                              |
| `mock.Setup(m => m.Prop)` / `mock.SetupGet(...)`                         | `mock.Mock.Setup.Prop`                                                      |
| `mock.SetupProperty(m => m.Prop)`                                        | `mock.Mock.Setup.Prop.Register()`                                           |
| `mock.SetupProperty(m => m.Prop, value)`                                 | `mock.Mock.Setup.Prop.InitializeWith(value)`                                |
| `mock.SetupSequence(...).Returns(a).Returns(b)`                          | `mock.Mock.Setup.Method(...).Returns(a).Returns(b)`                         |
| `.Returns(...)` / `.ReturnsAsync(...)` / `.Throws(...)`                  | preserved on the Mockolate setup chain                                      |
| `.Callback(...)`                                                         | `.Do(...)` on the Mockolate setup                                           |
| `mock.InSequence(seq).Setup(...)` (and other setup forms)                | `.InSequence(...)` wrapper is stripped; use `Verify…Then(...)` for ordering |
| `mock.Verify(m => m.Method(args), times)`                                | `mock.Mock.Verify.Method(args).<times>()`                                   |
| `mock.VerifyGet` / `mock.VerifySet`                                      | `mock.Mock.Verify.Prop.Got(...)` / `.Set(...)`                              |
| `mock.Raise(m => m.Event += null, args)`                                 | `mock.Mock.Raise.Event(args)`                                               |
| `Mock.Verify(m => m.Event += ..., times)`                                | `mock.Mock.Verify.Event.Subscribed()` / `Unsubscribed()`                    |
| `It.IsAny<T>()` / `It.Is(...)` / `It.IsInRange(...)` / `It.IsRegex(...)` | preserved or mapped to the Mockolate matcher equivalents                    |
| `It.Ref<T>.IsAny` / `out` parameters                                     | `It.IsAnyRef<T>()` / `It.IsOut(() => value)`                                |
| Nested mocks (`mock.Setup(m => m.Child.Prop)`)                           | Navigation chain is preserved: `mock.Child.Mock.Setup.Prop`                 |
