#load "../cake/helpers.cake"
#load "../cake/dotnet.cake"

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

string TARGET = Argument("target", "Test");

const string defaultVersion = "10.0.19041";
const string dotnetVersion = "net7.0";

// required
FilePath PROJECT = Argument("project", EnvironmentVariable("WINDOWS_TEST_PROJECT") ?? "");
string TEST_DEVICE = Argument("device", EnvironmentVariable("WINDOWS_TEST_DEVICE") ?? $"");

// optional
var DOTNET_PATH = Argument("dotnet-path", EnvironmentVariable("DOTNET_PATH"));
var TARGET_FRAMEWORK = Argument("tfm", EnvironmentVariable("TARGET_FRAMEWORK") ?? $"{dotnetVersion}-windows{defaultVersion}");
var BINLOG_ARG = Argument("binlog", EnvironmentVariable("WINDOWS_TEST_BINLOG") ?? "");
DirectoryPath BINLOG_DIR = string.IsNullOrEmpty(BINLOG_ARG) && !string.IsNullOrEmpty(PROJECT.FullPath) ? PROJECT.GetDirectory() : BINLOG_ARG;
var TEST_APP = Argument("app", EnvironmentVariable("WINDOWS_TEST_APP") ?? "");
FilePath TEST_APP_PROJECT = Argument("appproject", EnvironmentVariable("WINDOWS_TEST_APP_PROJECT") ?? "");
var TEST_RESULTS = Argument("results", EnvironmentVariable("MAC_TEST_RESULTS") ?? "");
string CONFIGURATION = Argument("configuration", "Debug");

var windowsVersion = Argument("apiversion", EnvironmentVariable("WINDOWS_PLATFORM_VERSION") ?? defaultVersion);

// other
string PLATFORM = "windows";
string DOTNET_PLATFORM = $"win10-x64";
bool DEVICE_CLEANUP = Argument("cleanup", true);

// Package ID of the WinUI Application
var packageId = Argument("packageid", "com.microsoft.maui.sample");

// Certificate Common Name to use/generate (eg: CN=DotNetMauiTests)
var certCN = Argument("commonname", "DotNetMAUITests");

Information("Project File: {0}", PROJECT);
Information("Build Binary Log (binlog): {0}", BINLOG_DIR);
Information("Build Platform: {0}", PLATFORM);
Information("Build Configuration: {0}", CONFIGURATION);

Information("Windows version: {0}", windowsVersion);

Setup(context =>
{
	Cleanup();
});

Teardown(context =>
{
	Cleanup();
});

void Cleanup()
{
	if (!DEVICE_CLEANUP)
		return;
}

Task("Cleanup");

Task("Build")
	.WithCriteria(!string.IsNullOrEmpty(PROJECT.FullPath))
	.Does(() =>
{
	// We need the key to be in LocalMachine -> TrustedPeople to install the msix signed with the key
	var localTrustedPeopleStore = new X509Store("TrustedPeople", StoreLocation.LocalMachine);
	localTrustedPeopleStore.Open(OpenFlags.ReadWrite);

	// We need to have the key also in CurrentUser -> My so that the msix can be built and signed
	// with the key by passing the key's thumbprint to the build
	var currentUserMyStore = new X509Store("My", StoreLocation.CurrentUser);
	currentUserMyStore.Open(OpenFlags.ReadWrite);
	var certificateThumbprint = localTrustedPeopleStore.Certificates.FirstOrDefault(c => c.Subject.Contains(certCN))?.Thumbprint;
	Information("Cert thumbprint: " + certificateThumbprint ?? "null");

	if (string.IsNullOrEmpty(certificateThumbprint))
	{
		Information("Generating cert");
		var rsa = RSA.Create();
		var req = new CertificateRequest("CN=" + certCN, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
		{
			new Oid
			{
				Value = "1.3.6.1.5.5.7.3.3",
				FriendlyName = "Code Signing"
			}
		}, false));
		req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
		req.CertificateExtensions.Add(
			new X509KeyUsageExtension(
				X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
				false));
		req.CertificateExtensions.Add(
						new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
		var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(5));
		if (OperatingSystem.IsWindows())
			cert.FriendlyName = certCN;
		var tmpCert = new X509Certificate2(cert.Export(X509ContentType.Pfx), "", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
		certificateThumbprint = tmpCert.Thumbprint;
		localTrustedPeopleStore.Add(tmpCert);
		currentUserMyStore.Add(tmpCert);
	}
	localTrustedPeopleStore.Close();
	currentUserMyStore.Close();

	var name = System.IO.Path.GetFileNameWithoutExtension(PROJECT.FullPath);
	var binlog = $"{BINLOG_DIR}/{name}-{CONFIGURATION}-windows.binlog";

	SetDotNetEnvironmentVariables(DOTNET_PATH);

	// Build the app in publish mode
	// Using the certificate thumbprint for the cert we just created
	var s = new DotNetPublishSettings();
	s.Configuration = "Release";
	s.Framework = TARGET_FRAMEWORK;
	s.MSBuildSettings = new DotNetMSBuildSettings();
	s.MSBuildSettings.Properties.Add("RuntimeIdentifierOverride", new List<string> { "win10-x64" });
	s.MSBuildSettings.Properties.Add("PackageCertificateThumbprint", new List<string> { certificateThumbprint });
	s.MSBuildSettings.Properties.Add("AppxPackageSigningEnabled", new List<string> { "True" });
	
	DotNetPublish(PROJECT.FullPath, s);
});

Task("Test")
	.IsDependentOn("Build")
	.Does(() =>
{
	var uninstallPS = new Action(() => {
		try {
			StartProcess("powershell",
				"$app = Get-AppxPackage -Name " + packageId + "; if ($app) { Remove-AppxPackage -Package $app.PackageFullName }");
		} catch { }
	});

	// Try to uninstall the app if it exists from before
	uninstallPS();

	var projectDir = PROJECT.GetDirectory();
	Information(projectDir.ToString());
	var cerPath = GetFiles(projectDir.FullPath + "/**/AppPackages/*/*.cer").First();
	var msixPath = GetFiles(projectDir.FullPath + "/**/AppPackages/*/*.msix").First();

	// Install the appx
	StartProcess("powershell", "Add-AppxPackage -Path \"" + MakeAbsolute(msixPath).FullPath + "\"");

	// Start the app with 'headless' arg			
	StartProcess("powershell", "start shell:AppsFolder\\$((Get-AppxPackage -Name \"" + packageId + "\").PackageFamilyName)!App headless");
	
	// TODO: Wait until xml results file exists

	// Uninstall the app(this will terminate it too)
	uninstallPS();
});

Task("uitest")
	.Does(() =>
{
	if (string.IsNullOrEmpty(TEST_APP) ) {
		if (string.IsNullOrEmpty(TEST_APP_PROJECT.FullPath))
			throw new Exception("If no app was specified, an app must be provided.");
		var binDir = TEST_APP_PROJECT.GetDirectory().Combine("bin").Combine(CONFIGURATION + "/" + $"{dotnetVersion}-windows{windowsVersion}").Combine(DOTNET_PLATFORM).FullPath;
		Information("BinDir: {0}", binDir);
		var apps = GetFiles(binDir + "/*.exe").Where(c => !c.FullPath.EndsWith("createdump.exe"));
		TEST_APP = apps.First().FullPath;
	}
	if (string.IsNullOrEmpty(TEST_RESULTS)) {
		TEST_RESULTS = TEST_APP + "-results";
	}

	Information("Test Device: {0}", TEST_DEVICE);
	Information("Test App: {0}", TEST_APP);
	Information("Test Results Directory: {0}", TEST_RESULTS);

	CleanDirectories(TEST_RESULTS);

	Information("Build UITests project {0}",PROJECT.FullPath);
	var name = System.IO.Path.GetFileNameWithoutExtension(PROJECT.FullPath);
	var binlog = $"{BINLOG_DIR}/{name}-{CONFIGURATION}-windows.binlog";

	var dd = MakeAbsolute(Directory("../../bin/dotnet/"));
	Information("DOTNET_PATH: {0}", dd);

	var toolPath = $"{dd}/dotnet.exe";

	Information("toolPath: {0}", toolPath);

	SetDotNetEnvironmentVariables(dd.FullPath);

	DotNetBuild(PROJECT.FullPath, new DotNetBuildSettings {
			Configuration = CONFIGURATION,
			ToolPath = toolPath,
			ArgumentCustomization = args => args
				.Append("/p:ExtraDefineConstants=WINTEST")
				.Append("/bl:" + binlog)
				.Append("/maxcpucount:1")
				//.Append("/tl")	
	});

	SetEnvironmentVariable("WINDOWS_APP_PATH", TEST_APP);
	SetEnvironmentVariable("APPIUM_LOG_FILE", $"{BINLOG_DIR}/appium_windows.log");

	Information("Run UITests project {0}",PROJECT.FullPath);
	RunTestWithLocalDotNet(PROJECT.FullPath, CONFIGURATION, toolPath, noBuild: true);
});

RunTarget(TARGET);
