using Snap.Hutao.Remastered.Core.Shell;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace Snap.Hutao.Remastered.Test.RuntimeBehavior;

[TestClass]
public sealed class UnpackagedStartupTaskTest
{
    private const string UserSid = "S-1-5-21-111-222-333-1001";
    private const string TaskName = @"\Hutao\Autorun for Test User";
    private const string Executable = @"C:\Apps\胡桃 & Remastered\Snap.Hutao.Remastered.exe";
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [TestMethod]
    [DataRow(false, "LeastPrivilege")]
    [DataRow(true, "HighestAvailable")]
    public void DefinitionUsesTheCurrentExecutableAndInteractiveUser(bool elevated, string runLevel)
    {
        XDocument definition = UnpackagedStartupTask.CreateDefinition(UserSid, Executable, elevated);
        XElement root = definition.Root ?? throw new InvalidOperationException();
        XElement principal = root.Descendants(TaskNamespace + "Principal").Single();
        Assert.AreEqual(UserSid, principal.Element(TaskNamespace + "UserId")?.Value);
        Assert.AreEqual("InteractiveToken", principal.Element(TaskNamespace + "LogonType")?.Value);
        Assert.AreEqual(runLevel, principal.Element(TaskNamespace + "RunLevel")?.Value);
        Assert.AreEqual(UserSid, root.Descendants(TaskNamespace + "LogonTrigger").Single().Element(TaskNamespace + "UserId")?.Value);
        Assert.AreEqual(Executable, root.Descendants(TaskNamespace + "Command").Single().Value);
        Assert.AreEqual(Path.GetDirectoryName(Executable), root.Descendants(TaskNamespace + "WorkingDirectory").Single().Value);
        Assert.AreEqual("PT0S", root.Descendants(TaskNamespace + "ExecutionTimeLimit").Single().Value);
        Assert.AreEqual("IgnoreNew", root.Descendants(TaskNamespace + "MultipleInstancesPolicy").Single().Value);
        Assert.IsFalse(root.Descendants(TaskNamespace + "Arguments").Any());
    }

    [TestMethod]
    public void XmlRoundTripPreservesUnicodeSpacesAndAmpersands()
    {
        XDocument definition = UnpackagedStartupTask.CreateDefinition(UserSid, Executable, false);
        XDocument roundTrip = XDocument.Parse(definition.ToString());
        Assert.AreEqual(Executable, roundTrip.Descendants(TaskNamespace + "Command").Single().Value);
        Assert.IsTrue(definition.ToString().Contains("&amp;", StringComparison.Ordinal));
    }

    [TestMethod]
    public void LongExecutablePathsAreNotTruncatedToAWindowsAppsAlias()
    {
        string path = @"C:\" + string.Join('\\', Enumerable.Repeat("directory", 35)) + @"\Snap.Hutao.Remastered.exe";
        XDocument definition = UnpackagedStartupTask.CreateDefinition(UserSid, path, true);
        Assert.AreEqual(path, definition.Descendants(TaskNamespace + "Command").Single().Value);
    }

    [TestMethod]
    public void RelativeExecutablePathsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UnpackagedStartupTask.CreateDefinition(UserSid, "sh.exe", false));
    }

    [TestMethod]
    public void RegistrationUsesARealXmlFileAndRemovesItOnSuccess()
    {
        string? temporaryPath = null;
        UnpackagedStartupTask.Register(TaskName, UserSid, Executable, true, (taskName, xmlPath) =>
        {
            temporaryPath = xmlPath;
            Assert.AreEqual(TaskName, taskName);
            Assert.IsTrue(File.Exists(xmlPath));
            XDocument definition = XDocument.Load(xmlPath);
            Assert.AreEqual(Executable, definition.Descendants(TaskNamespace + "Command").Single().Value);
        });
        Assert.IsNotNull(temporaryPath);
        Assert.IsFalse(File.Exists(temporaryPath));
    }

    [TestMethod]
    public void RegistrationFailureIsPropagatedAndTheTemporaryFileIsRemoved()
    {
        string? temporaryPath = null;
        Win32Exception denied = new(5);
        Win32Exception actual = Assert.ThrowsExactly<Win32Exception>(() =>
            UnpackagedStartupTask.Register(TaskName, UserSid, Executable, false, (_, xmlPath) =>
            {
                temporaryPath = xmlPath;
                throw denied;
            }));
        Assert.AreSame(denied, actual);
        Assert.IsNotNull(temporaryPath);
        Assert.IsFalse(File.Exists(temporaryPath));
    }

    [TestMethod]
    public void RegistrationUpdatesAnExistingTaskWithoutShellInterpolationOrPasswords()
    {
        const string XmlPath = @"C:\Temp\Task Definition.xml";
        ProcessStartInfo info = UnpackagedStartupTask.CreateRegistrationStartInfo(TaskName, XmlPath);
        Assert.AreEqual("schtasks.exe", Path.GetFileName(info.FileName));
        CollectionAssert.AreEqual(new[] { "/Create", "/TN", TaskName, "/XML", XmlPath, "/F", "/HRESULT" }, info.ArgumentList.ToArray());
        Assert.IsFalse(info.UseShellExecute);
        Assert.IsTrue(info.CreateNoWindow);
        Assert.IsTrue(info.RedirectStandardError);
    }
}
