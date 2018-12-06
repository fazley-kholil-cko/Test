//////////////////////////////////////////////////////////////////////
// TOOLS
//////////////////////////////////////////////////////////////////////
#tool "nuget:?package=GitVersion.CommandLine"
#tool "nuget:?package=NUnit.Extension.NUnitV2Driver"
#tool "nuget:?package=NUnit.Extension.VSProjectLoader"
#tool "nuget:?package=NUnit.ConsoleRunner"
#tool "nuget:?package=NUnit.Extension.TeamCityEventListener"
#tool "nuget:?package=OctopusTools"


//////////////////////////////////////////////////////////////////////
// ADDINS

//////////////////////////////////////////////////////////////////////
#addin "Cake.Npm"
#addin "nuget:?package=Cake.NSpec&version=0.2.0"
//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////
var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARIABLES
///////////////////////////////////////////////////////////////////////////////
var isCIBuild			= !BuildSystem.IsLocalBuild;
var solutionPath        = "./Checkout.CardPayment.Visa.sln";
var testPath            = "./src/test/*/bin/" + configuration + "/*.UnitTests.exe";
var buildArtifacts      = Directory("./artifacts");
var libs                = Directory("./packages/_lib"); // NuGetManager, doesn't seem to get cleaned otherwise
// var gitVersionInfo = GitVersion(new GitVersionSettings {
//     OutputType = GitVersionOutput.Json
// });

// var nugetVersion = gitVersionInfo.NuGetVersion;
var nugetVersion = "0.0.1";

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////
Setup(context =>
{
    Information("Building Authorisation v{0} with configuration {1}", nugetVersion, configuration);
});

Teardown(context =>
{
    Information("Finished running tasks.");
});

//////////////////////////////////////////////////////////////////////
//  PRIVATE TASKS
//////////////////////////////////////////////////////////////////////

Task("__Clean")
	.Does(() =>
	{
		CleanDirectories(new DirectoryPath[] { buildArtifacts, libs });
		DotNetBuild(solutionPath, settings => settings
            .SetConfiguration(configuration)
            .WithTarget("Clean")
            .SetVerbosity(Verbosity.Minimal));
	});

Task("__Restore")
	.Does(() =>
	{
		NuGetRestore(solutionPath);
	});

Task("__UpdateAssemblyVersionInformation")
    .WithCriteria(isCIBuild)
    .Does(() =>
    {
        GitVersion(new GitVersionSettings {
            UpdateAssemblyInfo = true, // Don't update all assembly files
            UpdateAssemblyInfoFilePath = "./src/shared/AuthorisationAssemblyInfo.cs",
            OutputType = GitVersionOutput.BuildServer
        });

        // Information("AssemblyVersion -> {0}", gitVersionInfo.AssemblySemVer);
        // Information("AssemblyFileVersion -> {0}.0", gitVersionInfo.MajorMinorPatch);
        // Information("AssemblyInformationalVersion -> {0}", gitVersionInfo.InformationalVersion);
    });

Task("__Build")
    .Does(() =>
    {
		var packagePath = string.Concat("\"", MakeAbsolute(buildArtifacts).FullPath, "\"");
        var scheme = "visa";
             
        Information("Building for scheme -> {0}", scheme);
		DotNetBuild(solutionPath, settings => settings
        .SetConfiguration(configuration)
        .WithTarget("Rebuild")
        .SetVerbosity(Verbosity.Minimal)
        .WithProperty("WarningLevel", "0")
        .WithProperty("DefineConstants",scheme)
	    .WithProperty("RunOctoPack", "true")
        .WithProperty("OctoPackPackageVersion", nugetVersion)
        .WithProperty("OctoPackPublishPackageToFileShare", packagePath)
        .WithProperty("OctoPackProjectName", scheme) 
        .WithProperty("WarningLevel", "0"));
        
    });

Task("__Test")
    .Does(() =>
    {	
		Information("Running unit test");
		var nspecSettings = new ProcessSettings
		{
           Arguments = "./src/test/**/bin/" + configuration + "/*.exe",
           RedirectStandardOutput = true
		};
		StartProcess("./packages/NSpec.3.1.0/tools/net451/win7-x64/NSpecRunner.exe", nspecSettings);

    });

Task("__OctoPush")
    .WithCriteria(isCIBuild)
    .Does(() =>
    {
		var packages = GetFiles("./artifacts/*.nupkg");

		OctoPush(
			EnvironmentVariable("Octopus_Server"),
			EnvironmentVariable("Octopus_ApiKey"),
			packages,
			new OctopusPushSettings {
				ReplaceExisting = true
			}
		);
    });


//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Package")
    .IsDependentOn("__Clean")
    .IsDependentOn("__Restore")
    .IsDependentOn("__UpdateAssemblyVersionInformation")
    .IsDependentOn("__Build"); 
    // .IsDependentOn("__Test");

Task("Deploy")
    .IsDependentOn("Package")
	.IsDependentOn("__OctoPush");

Task("Default")
	.IsDependentOn("Package");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////
RunTarget(target);