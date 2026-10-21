// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace Snap.Hutao.Remastered.Core.Shell;

internal static class UnpackagedStartupTask
{
    // Uses the same task name as HutaoNative's query/delete operations. Only
    // creation needs a separate implementation: the native API uses MSIX aliases.
    [SupportedOSPlatform("windows")]
    public static void Register(bool runElevated)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string userId = identity.User?.Value ?? throw new InvalidOperationException("The current user has no SID.");
        string executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The current executable is unavailable.");
        string userName = Environment.GetEnvironmentVariable("USERNAME") ?? Environment.UserName;
        Register($@"\Hutao\Autorun for {userName}", userId, executablePath, runElevated, RunRegistration);
    }

    public static void Register(string taskName, string userId, string executablePath, bool runElevated, Action<string, string> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        XDocument definition = CreateDefinition(userId, executablePath, runElevated);
        string temporaryPath = Path.GetTempFileName();
        try
        {
            // XML handles spaces, non-ASCII characters and XML metacharacters;
            // no shell command interpolates the executable or account name.
            File.WriteAllText(temporaryPath, definition.ToString(), Encoding.Unicode);
            register(taskName, temporaryPath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static XDocument CreateDefinition(string userId, string executablePath, bool runElevated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("A startup task requires an absolute executable path.", nameof(executablePath));
        }

        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        return new(new XElement(ns + "Task", new XAttribute("version", "1.2"),
            new XElement(ns + "Triggers",
                new XElement(ns + "LogonTrigger", new XElement(ns + "Enabled", true), new XElement(ns + "UserId", userId))),
            new XElement(ns + "Principals",
                new XElement(ns + "Principal", new XAttribute("id", "CurrentUser"),
                    new XElement(ns + "UserId", userId),
                    new XElement(ns + "LogonType", "InteractiveToken"),
                    new XElement(ns + "RunLevel", runElevated ? "HighestAvailable" : "LeastPrivilege"))),
            new XElement(ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", false),
                new XElement(ns + "StopIfGoingOnBatteries", false),
                new XElement(ns + "Enabled", true),
                new XElement(ns + "ExecutionTimeLimit", "PT0S")),
            new XElement(ns + "Actions", new XAttribute("Context", "CurrentUser"),
                new XElement(ns + "Exec",
                    new XElement(ns + "Command", executablePath),
                    new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(executablePath))))));
    }

    public static ProcessStartInfo CreateRegistrationStartInfo(string taskName, string xmlPath)
    {
        ProcessStartInfo startInfo = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "/Create", "/TN", taskName, "/XML", xmlPath, "/F", "/HRESULT" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void RunRegistration(string taskName, string xmlPath)
    {
        ProcessStartInfo startInfo = CreateRegistrationStartInfo(taskName, xmlPath);
        // Called on the settings operation's background thread, never the UI.
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Task registration did not start.");
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000))
        {
            process.Kill();
            throw new TimeoutException("Task Scheduler did not complete startup registration within ten seconds.");
        }

        string details = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new System.ComponentModel.Win32Exception(process.ExitCode, details);
        }
    }
}
