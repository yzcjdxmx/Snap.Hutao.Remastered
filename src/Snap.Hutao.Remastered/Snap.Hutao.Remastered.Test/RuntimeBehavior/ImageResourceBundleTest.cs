using Snap.Hutao.Remastered.Core.Caching;
using Snap.Hutao.Remastered.ViewModel.Guide;
using System.IO;
using System.Security.Cryptography;

namespace Snap.Hutao.Remastered.Test.RuntimeBehavior;

[TestClass]
public sealed class ImageResourceBundleTest
{
    private string directory = string.Empty;

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateTempSubdirectory("hutao-bundle-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, true);

    [TestMethod]
    public void MissingBundleIsOptional()
    {
        ImageResourceBundle bundle = new(directory);
        Assert.AreEqual(0, bundle.Count);
        Assert.IsNull(bundle.Find("ItemIcon/example.png"));
    }

    [TestMethod]
    public void SnapshotVersionDoesNotPreventNewerGameResourcesFromFallingBack()
    {
        ImageResourceBundle bundle = CreateBundle([1, 2, 3]);
        Assert.AreEqual("7.1", bundle.Version);
        Assert.AreEqual(Path.Combine(directory, "ItemIcon", "example.png"), bundle.Find("ItemIcon/example.png"));
        Assert.IsNull(bundle.Find("ItemIcon/new-in-7.2.png"));
    }

    [TestMethod]
    public async Task OnlyIdenticalBytesAreDuplicates()
    {
        ImageResourceBundle bundle = CreateBundle([1, 2, 3]);
        string cache = Path.Combine(directory, "legacy-url-hash");
        await File.WriteAllBytesAsync(cache, [1, 2, 3]);
        Assert.IsNotNull(await bundle.FindIdenticalAsync(cache, "ItemIcon/example.png"));
        await File.WriteAllBytesAsync(cache, [4, 5, 6]);
        Assert.IsNull(await bundle.FindIdenticalAsync(cache, "ItemIcon/example.png"));
        CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(cache));
    }

    [TestMethod]
    public async Task SameContentAtAnotherResourceNameCannotReplaceMissingResource()
    {
        ImageResourceBundle bundle = CreateBundle([1, 2, 3]);
        string cache = Path.Combine(directory, "new-resource");
        await File.WriteAllBytesAsync(cache, [1, 2, 3]);
        Assert.IsNull(await bundle.FindIdenticalAsync(cache, "ItemIcon/new-in-7.2.png"));
    }

    [TestMethod]
    public async Task DamagedInstalledImageCannotJustifyDeletingCache()
    {
        ImageResourceBundle bundle = CreateBundle([1, 2, 3]);
        string cache = Path.Combine(directory, "cache");
        await File.WriteAllBytesAsync(cache, [1, 2, 3]);
        await File.WriteAllBytesAsync(Path.Combine(directory, "ItemIcon", "example.png"), [4, 5, 6]);
        Assert.IsNull(await bundle.FindIdenticalAsync(cache, "ItemIcon/example.png"));
    }

    [TestMethod]
    public void MissingOrEmptyInstalledImageFallsBack()
    {
        ImageResourceBundle bundle = CreateBundle([1, 2, 3]);
        string image = Path.Combine(directory, "ItemIcon", "example.png");
        File.WriteAllBytes(image, []);
        Assert.IsNull(bundle.Find("ItemIcon/example.png"));
        File.Delete(image);
        Assert.IsNull(bundle.Find("ItemIcon/example.png"));
    }

    [TestMethod]
    [DataRow("../example.png")]
    [DataRow("ItemIcon/../../outside.png")]
    [DataRow("C:/outside.png")]
    [DataRow("ItemIcon\\example.png")]
    [DataRow("ItemIcon/example.png:stream")]
    public void ManifestPathsStayInsideSnapshot(string path)
    {
        Assert.IsFalse(ImageResourceBundle.IsResourcePath(path));
        WriteManifest(path, [1, 2, 3]);
        Assert.ThrowsExactly<InvalidDataException>(() => new ImageResourceBundle(directory));
    }

    [TestMethod]
    public void OnlyInstallerWithoutSnapshotPreloadsArchives()
    {
        Assert.IsTrue(GuideNavigation.ShouldPreloadResources(false, false));
        Assert.IsFalse(GuideNavigation.ShouldPreloadResources(false, true));
        Assert.IsFalse(GuideNavigation.ShouldPreloadResources(true, false));
        Assert.IsFalse(GuideNavigation.ShouldPreloadResources(true, true));
    }

    [TestMethod]
    public void ResourceStepsAreSkippedWithoutChangingOtherGuideSteps()
    {
        Assert.AreEqual((uint)GuideState.Material, GuideNavigation.NormalizeResourceStep((uint)GuideState.StaticResourceSetting, false));
        Assert.AreEqual((uint)GuideState.Material, GuideNavigation.NormalizeResourceStep((uint)GuideState.StaticResourceBegin, false));
        Assert.AreEqual((uint)GuideState.StaticResourceSetting, GuideNavigation.NormalizeResourceStep((uint)GuideState.StaticResourceSetting, true));
        Assert.AreEqual((uint)GuideState.Document, GuideNavigation.NormalizeResourceStep((uint)GuideState.Document, false));
        Assert.AreEqual((uint)GuideState.Completed, GuideNavigation.NormalizeResourceStep((uint)GuideState.Completed, false));
    }

    private ImageResourceBundle CreateBundle(byte[] content)
    {
        Directory.CreateDirectory(Path.Combine(directory, "ItemIcon"));
        File.WriteAllBytes(Path.Combine(directory, "ItemIcon", "example.png"), content);
        WriteManifest("ItemIcon/example.png", content);
        return new(directory);
    }

    private void WriteManifest(string path, byte[] content)
    {
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            resourceVersion = "7.1",
            files = new[] { new { path, length = content.Length, sha256 = Convert.ToHexString(SHA256.HashData(content)) } },
        }));
    }
}
