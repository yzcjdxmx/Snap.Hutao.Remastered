using Snap.Hutao.Remastered.Service.Game.Island;
using System;
using System.IO;

namespace Snap.Hutao.Remastered.Test.IncomingFeature;

[TestClass]
public sealed class CraftHotkeyModuleCacheTest
{
    [TestMethod]
    public void StagesModuleOutsidePackageAndUpdatesWithoutReplacingLoadedModule()
    {
        string root = Path.Combine(Path.GetTempPath(), "CraftModuleTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            string package = Path.Combine(root, "package");
            string cache = Path.Combine(root, "data", "native");
            Directory.CreateDirectory(package);
            string source = Path.Combine(package, "CraftModifiersIsland.dll");
            File.WriteAllBytes(source, [1, 2, 3]);
            string first = CraftHotkeyModuleCache.Prepare(source, cache);
            Assert.AreEqual(Path.GetFullPath(cache), Path.GetDirectoryName(first));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(first));
            using (FileStream loaded = new(first, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.AreEqual(first, CraftHotkeyModuleCache.Prepare(source, cache));
                File.WriteAllBytes(source, [4, 5, 6]);
                string updated = CraftHotkeyModuleCache.Prepare(source, cache);
                Assert.AreNotEqual(first, updated);
                CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, File.ReadAllBytes(updated));
            }
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(first));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
