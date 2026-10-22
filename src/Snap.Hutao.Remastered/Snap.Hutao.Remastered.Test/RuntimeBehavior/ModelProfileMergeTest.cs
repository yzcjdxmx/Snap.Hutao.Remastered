using Snap.Hutao.Remastered.Service.AI;
using Snap.Hutao.Remastered.Service.AI.Models;
using Snap.Hutao.Remastered.Service.AI.Settings;

namespace Snap.Hutao.Remastered.Test.RuntimeBehavior;

[TestClass]
public sealed class ModelProfileMergeTest
{
    [TestMethod]
    public void RefreshPublishesOneFullyPopulatedSourceWithoutMutatingTheOldList()
    {
        ModelProviderProfile provider = new();
        ObservableSettingsCollection<ModelProfile, Guid> original = provider.ModelProfiles;
        original.Add(new());
        int sourceChanges = 0;
        int oldMembershipChanges = 0;
        original.CollectionChanged += (_, _) => oldMembershipChanges++;
        provider.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(provider.ModelProfiles)) { return; }
            sourceChanges++;
            Assert.HasCount(178, provider.ModelProfiles);
            Assert.IsTrue(provider.ModelProfiles.All(model => !string.IsNullOrWhiteSpace(model.ModelId) && !string.IsNullOrWhiteSpace(model.DisplayName)));
            Assert.AreSame(provider.ModelProfiles[0], provider.ModelProfiles.CurrentItem);
        };

        ModelProfile[] fetched = Enumerable.Range(0, 178).Select(index => new ModelProfile { ModelId = $"model-{index}" }).ToArray();
        provider.ModelProfiles = ModelProfileMerger.CreateMerged(original, fetched);

        Assert.AreEqual(1, sourceChanges);
        Assert.AreEqual(0, oldMembershipChanges);
        Assert.HasCount(1, original);
        Assert.AreNotSame(original, provider.ModelProfiles);
    }

    [TestMethod]
    public void RefreshKeepsIdentityAliasesAndOptionsWhileDeduplicatingIds()
    {
        ModelProfile existing = new() { ModelId = "qwen3.8-flash", Name = "My fast model" };
        existing.AgentOptions.MaxOutputTokens = 2048;
        ObservableSettingsCollection<ModelProfile, Guid> original = [existing];
        original.CurrentItem = existing;
        Guid id = existing.Id;

        ObservableSettingsCollection<ModelProfile, Guid> result = ModelProfileMerger.CreateMerged(original,
            [new() { ModelId = " qwen3.8-flash ", Name = "Server name" }, new() { ModelId = "qwen3.8-flash" }, new() { ModelId = " " }]);

        Assert.HasCount(1, result);
        Assert.AreSame(existing, result[0]);
        Assert.AreEqual(id, existing.Id);
        Assert.AreEqual("My fast model", existing.DisplayName);
        Assert.AreEqual(2048, existing.AgentOptions.MaxOutputTokens);
        Assert.AreSame(existing, result.CurrentItem);
    }

    [TestMethod]
    public void ReplacedSourcesStopObservingRetainedModelObjects()
    {
        ModelProfile model = new() { ModelId = "qwen3.8-flash" };
        ModelProviderProfile provider = new() { ModelProfiles = new([model]) };
        provider.ModelProfiles.CurrentItem = model;
        ObservableSettingsCollection<ModelProfile, Guid> old = provider.ModelProfiles;
        int oldChanges = 0;
        ((System.ComponentModel.INotifyPropertyChanged)old).PropertyChanged += (_, _) => oldChanges++;

        provider.ModelProfiles = ModelProfileMerger.CreateMerged(old, [new() { ModelId = model.ModelId }]);
        model.Name = "Updated alias";

        Assert.AreEqual(0, oldChanges);
        Assert.AreSame(model, provider.ModelProfiles.CurrentItem);
        Assert.AreEqual("Updated alias", provider.ModelProfiles.CurrentItem!.DisplayName);
    }

    [TestMethod]
    public void RefreshRetainsConfiguredManualModelsWithoutKeepingEmptyPlaceholders()
    {
        ModelProfile manual = new() { ModelId = "private-deployment", Name = "My deployment" };
        ObservableSettingsCollection<ModelProfile, Guid> original = [new(), manual];
        original.CurrentItem = manual;

        var result = ModelProfileMerger.CreateMerged(original, [new() { ModelId = "qwen3.8-flash" }]);

        Assert.HasCount(2, result);
        Assert.AreSame(manual, result.CurrentItem);
        Assert.AreEqual("My deployment", manual.Name);
    }
}
