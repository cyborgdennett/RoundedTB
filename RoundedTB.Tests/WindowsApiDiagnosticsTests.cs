using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Xunit;

namespace RoundedTB.Tests;

public class WindowsApiDiagnosticsTests
{
    [Fact]
    public void AccessDeniedIsDistinguishedFromOtherErrors()
    {
        Assert.Equal("ACCESS DENIED", WindowsApiDiagnostics.Check("probe", () => throw new Win32Exception(5)).Status);
        Assert.Equal("ACCESS DENIED", WindowsApiDiagnostics.Check("probe", () => throw new COMException("denied", unchecked((int)0x80070005))).Status);
        var result = WindowsApiDiagnostics.Check("probe", () => throw new Win32Exception(1409));
        Assert.Equal("FAIL", result.Status);
        Assert.Contains("1409", result.Detail);
        Assert.Equal("PASS", WindowsApiDiagnostics.Check("probe", () => { }).Status);
    }

    [Fact]
    public void ReportDoesNotTreatUnverifiedFeaturesAsPassingOrRecommendElevation()
    {
        var report = WindowsApiDiagnostics.Format(new[]
        {
            new WindowsApiDiagnostics.Result("Explorer", "PASS", "OK"),
            new WindowsApiDiagnostics.Result("App", "ACCESS DENIED", "Windows error 5"),
            new WindowsApiDiagnostics.Result("Shell", "NOT VERIFIED", "No reliable result")
        });
        Assert.Contains("Passed: 1; access denied: 1; other failures: 0; not verified: 1", report);
        Assert.Contains("elevated app", report);
        Assert.Contains("Passing does not guarantee", report);
        Assert.Contains("[NOT VERIFIED]", report);
    }

    [Fact]
    public void UnsupportedPlatformDoesNotCallWindowsApis()
    {
        if (!OperatingSystem.IsWindows()) Assert.Contains("only on Windows", new WindowsApiDiagnostics().Run());
    }
}
