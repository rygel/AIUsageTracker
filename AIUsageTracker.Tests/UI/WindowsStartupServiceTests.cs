// <copyright file="WindowsStartupServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;
using Microsoft.Win32;

namespace AIUsageTracker.Tests.UI;

public sealed class WindowsStartupServiceTests : IDisposable
{
    private const string ValueName = "AI Usage Tracker";
    private readonly string _registryPath = @"Software\AIUsageTracker.Tests\Startup\" + Guid.NewGuid().ToString("N");
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AIUsageTracker-startup-tests", Guid.NewGuid().ToString("N"));
    private readonly string _variableName = "AI_TRACKER_STARTUP_TEST_" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _key;
    private readonly string _exePath;
    private readonly string _portableCommand;

    public WindowsStartupServiceTests()
    {
        Directory.CreateDirectory(this._directory);
        this._exePath = Path.Combine(this._directory, "AIUsageTracker.exe");
        File.WriteAllText(this._exePath, string.Empty);
        Environment.SetEnvironmentVariable(this._variableName, this._directory);
        this._portableCommand = $"\"%{this._variableName}%\\AIUsageTracker.exe\"";
        this._key = Registry.CurrentUser.CreateSubKey(this._registryPath);
    }

    public void Dispose()
    {
        this._key.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(this._registryPath, throwOnMissingSubKey: false);
        Environment.SetEnvironmentVariable(this._variableName, null);
        Directory.Delete(this._directory, recursive: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_Enable_PreservesMatchingExpandableCommand(bool changeCase)
    {
        var command = changeCase ? this._portableCommand.ToUpperInvariant() : this._portableCommand;
        this._key.SetValue(ValueName, command, RegistryValueKind.ExpandString);

        WindowsStartupService.Apply(true, this._key, this._exePath);

        Assert.Equal(command, this._key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        Assert.Equal(RegistryValueKind.ExpandString, this._key.GetValueKind(ValueName));
    }

    [Theory]
    [InlineData("\"%MISSING_AI_TRACKER_STARTUP_ROOT%\\AIUsageTracker.exe\"")]
    [InlineData("\"C:\\Old Install\\AIUsageTracker.exe\"")]
    public void Apply_Enable_ReplacesStaleExpandableCommand(string command)
    {
        this._key.SetValue(ValueName, command, RegistryValueKind.ExpandString);

        WindowsStartupService.Apply(true, this._key, this._exePath);

        Assert.Equal($"\"{this._exePath}\"", this._key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        Assert.Equal(RegistryValueKind.String, this._key.GetValueKind(ValueName));
    }

    [Fact]
    public void Apply_Enable_DoesNotPreserveUnexpectedArguments()
    {
        this._key.SetValue(ValueName, this._portableCommand + " --unexpected", RegistryValueKind.ExpandString);

        WindowsStartupService.Apply(true, this._key, this._exePath);

        Assert.Equal($"\"{this._exePath}\"", this._key.GetValue(ValueName));
        Assert.Equal(RegistryValueKind.String, this._key.GetValueKind(ValueName));
    }

    [Fact]
    public void Apply_Enable_ReplacesNonExpandableTokenString()
    {
        this._key.SetValue(ValueName, this._portableCommand, RegistryValueKind.String);

        WindowsStartupService.Apply(true, this._key, this._exePath);

        Assert.Equal($"\"{this._exePath}\"", this._key.GetValue(ValueName));
        Assert.Equal(RegistryValueKind.String, this._key.GetValueKind(ValueName));
    }

    [Fact]
    public void Apply_Enable_CreatesRegistrationWhenAbsent()
    {
        WindowsStartupService.Apply(true, this._key, this._exePath);

        Assert.Equal($"\"{this._exePath}\"", this._key.GetValue(ValueName));
        Assert.Equal(RegistryValueKind.String, this._key.GetValueKind(ValueName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_DisableOrMissingExecutable_RemovesOnlyAppRegistration(bool startUi)
    {
        this._key.SetValue(ValueName, this._portableCommand, RegistryValueKind.ExpandString);
        this._key.SetValue("Another App", "unchanged");
        if (startUi)
        {
            File.Delete(this._exePath);
        }

        WindowsStartupService.Apply(startUi, this._key, this._exePath);

        Assert.Null(this._key.GetValue(ValueName));
        Assert.Equal("unchanged", this._key.GetValue("Another App"));
    }
}
