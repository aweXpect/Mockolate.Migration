# Mockolate.Migration

[![Nuget](https://img.shields.io/nuget/v/Mockolate.Migration)](https://www.nuget.org/packages/Mockolate.Migration)
[![Build](https://github.com/aweXpect/Mockolate.Migration/actions/workflows/build.yml/badge.svg)](https://github.com/aweXpect/Mockolate.Migration/actions/workflows/build.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=aweXpect_Mockolate.Migration&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=aweXpect_Mockolate.Migration)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=aweXpect_Mockolate.Migration&metric=coverage)](https://sonarcloud.io/summary/overall?id=aweXpect_Mockolate.Migration)

Template for extension projects for [aweXpect](https://github.com/aweXpect/aweXpect).

## Steps after creating a new project from this Template:

- Enable Sonarcloud analysis
	- Create the project at [sonarcloud](https://sonarcloud.io/projects/create)
	- Set "New Code" definition to "Previous version"
	- Add the `SONAR_TOKEN` as repository secret
	- Set the long-lived branch pattern to `(main|release/.*)`
	- Change the Quality Profile for C# and the Quality Gate to "aweXpect way"
- Take over settings from T6e project
	- General Settings
	- Protect the `main` branch
	- Create a "production" environment and add the `NUGET_API_KEY` secret
- Support [StrongName signing](https://learn.microsoft.com/en-us/dotnet/standard/assembly/sign-strong-name):
	- Create a
	  new [Public/Private Key-Pair](https://learn.microsoft.com/en-us/dotnet/standard/assembly/create-public-private-key-pair):
		- Open
		  the [developer command prompt](https://learn.microsoft.com/en-us/visualstudio/ide/reference/command-prompt-powershell?view=vs-2022#start-from-windows-menu)
		- Go to the project directory
		- Type `sn -k strongname.snk` to create a new key pair
		- Type `sn -p strongname.snk publicKey.snk` to extract the public key
		- Type `sn -tp publicKey.snk` to display the public key, put it in "Directory.Build.props" and enable the
		  corresponding `PropertyGroup`
		- Delete the "publicKey.snk" file
- Replace "T6e" with your suffix both in file names and in contents.
- Adapt the copyright and project information in Source/Directory.Build.props
- Adapt the README.md

