using Nuke.Common;
using Nuke.Common.Tools.SonarScanner;

// ReSharper disable AllUnderscoreLocalParameterName

namespace Build;

partial class Build
{
	[Parameter("The key to push to sonarcloud")] [Secret] readonly string SonarToken;

	Target CodeAnalysisBegin => _ => _
		.Unlisted()
		.Before(Compile)
		.Before(CodeCoverage)
		.Executes(() =>
		{
			SonarScannerTasks.SonarScannerBegin(s => s
				.SetOrganization("awexpect")
				.SetProjectKey("aweXpect_Mockolate.Migration")
				.AddVSTestReports(TestResultsDirectory / "*.trx")
				.AddOpenCoverPaths(TestResultsDirectory / "reports" / "OpenCover.xml")
				.SetPullRequestOrBranchName(GitHubActions, GitVersion)
				.SetVersion(GitVersion.SemVer)
				.AddAdditionalParameter("sonar.issue.ignore.multicriteria", "e1,e2")
				.AddAdditionalParameter("sonar.issue.ignore.multicriteria.e1.ruleKey", "external_roslyn:MockolateM001")
				.AddAdditionalParameter("sonar.issue.ignore.multicriteria.e1.resourceKey", "**/Mockolate.Migration.MoqPlayground/**/*")
				.AddAdditionalParameter("sonar.issue.ignore.multicriteria.e2.ruleKey", "external_roslyn:MockolateM002")
				.AddAdditionalParameter("sonar.issue.ignore.multicriteria.e2.resourceKey", "**/Mockolate.Migration.NSubstitutePlayground/**/*")
				.SetToken(SonarToken));
		});

	Target CodeAnalysisEnd => _ => _
		.Unlisted()
		.DependsOn(Compile)
		.DependsOn(CodeCoverage)
		.OnlyWhenDynamic(() => IsServerBuild)
		.Executes(() =>
		{
			SonarScannerTasks.SonarScannerEnd(s => s
				.SetToken(SonarToken));
		});

	Target CodeAnalysis => _ => _
		.DependsOn(CodeAnalysisBegin)
		.DependsOn(CodeAnalysisEnd);
}
