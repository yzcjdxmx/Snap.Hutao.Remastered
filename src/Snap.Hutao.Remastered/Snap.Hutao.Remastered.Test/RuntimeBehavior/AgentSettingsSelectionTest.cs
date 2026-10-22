using Snap.Hutao.Remastered.Service.AI;
using Snap.Hutao.Remastered.Service.AI.Models;
using Snap.Hutao.Remastered.Service.AI.Settings;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Snap.Hutao.Remastered.Test.RuntimeBehavior;

[TestClass]
public sealed class AgentSettingsSelectionTest
{
    [TestMethod]
    public void AddingWithAnUnresolvedSavedSelectionPublishesTheItemFirst()
    {
        ObservableSettingsCollection<ModelProfile, Guid> source = new();
        source.CurrentItemId = Guid.NewGuid();
        using SelectorMirror selector = new(source);
        ModelProfile added = new();

        source.Add(added);
        source.CurrentItem = added;

        Assert.AreSame(added, selector.SelectedItem);
        Assert.HasCount(1, selector.Items);
    }

    [TestMethod]
    public void RepeatedRefreshPublishesOneCompleteSourceAndKeepsSelection()
    {
        ModelProviderProfile provider = new();
        provider.ModelProfiles.CurrentItemId = Guid.NewGuid();
        List<int> publishedCounts = [];
        provider.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(provider.ModelProfiles))
            {
                publishedCounts.Add(provider.ModelProfiles.Count);
            }
        };
        ModelProfile[] fetched = Enumerable.Range(0, 178).Select(index => new ModelProfile { ModelId = $"model-{index}" }).ToArray();
        provider.ModelProfiles = ModelProfileMerger.CreateMerged(provider.ModelProfiles, fetched);
        ModelProfile selected = provider.ModelProfiles[75];
        selected.Name = "Custom label";
        selected.AgentOptions.MaxOutputTokens = 2048;
        provider.ModelProfiles.CurrentItem = selected;
        for (int i = 0; i < 3; i++)
        {
            provider.ModelProfiles = ModelProfileMerger.CreateMerged(provider.ModelProfiles, fetched);
        }

        CollectionAssert.AreEqual(new[] { 178, 178, 178, 178 }, publishedCounts);
        Assert.AreSame(selected, provider.ModelProfiles.CurrentItem);
        Assert.AreEqual("Custom label", selected.Name);
        Assert.AreEqual(2048, selected.AgentOptions.MaxOutputTokens);
    }

    [TestMethod]
    public void RemovingTheSelectedModelRestoresAFallbackAfterTheSelectorClearsIt()
    {
        ModelProfile first = new() { ModelId = "first" };
        ModelProfile selected = new() { ModelId = "selected" };
        ObservableSettingsCollection<ModelProfile, Guid> source = [first, selected];
        source.CurrentItem = selected;
        using SelectorMirror selector = new(source);

        source.Remove(selected);

        Assert.AreSame(first, source.CurrentItem);
        Assert.AreSame(first, selector.SelectedItem);
    }

    [TestMethod]
    public void ClearingAndRepeatedlyAddingModelsKeepsTheSelectorUsable()
    {
        ObservableSettingsCollection<ModelProfile, Guid> source = [new()];
        source.MoveCurrentToFirst();
        using SelectorMirror selector = new(source);

        source.Clear();
        Assert.IsNull(source.CurrentItem);
        Assert.IsNull(selector.SelectedItem);
        Assert.HasCount(0, selector.Items);
        for (int i = 0; i < 20; i++)
        {
            ModelProfile added = new();
            source.Add(added);
            source.CurrentItem = added;
            Assert.AreSame(added, selector.SelectedItem);
        }
        Assert.HasCount(20, selector.Items);
    }

    [TestMethod]
    public void ReplacingTheSelectedModelPublishesItsNewInstanceBeforeSelectingIt()
    {
        ModelProfile original = new() { ModelId = "original" };
        ObservableSettingsCollection<ModelProfile, Guid> source = [original];
        source.CurrentItem = original;
        using SelectorMirror selector = new(source);
        ModelProfile replacement = new() { Id = original.Id, ModelId = "replacement" };

        source[0] = replacement;

        Assert.AreSame(replacement, selector.SelectedItem);
        Assert.AreSame(replacement, source.CurrentItem);
    }

    [TestMethod]
    public void CopyingSettingsWithAnUnresolvedSelectionPublishesAllNewItems()
    {
        ObservableSettingsCollection<ModelProfile, Guid> destination = new();
        destination.CurrentItemId = Guid.NewGuid();
        using SelectorMirror selector = new(destination);
        ObservableSettingsCollection<ModelProfile, Guid> source = [new() { ModelId = "first" }, new() { ModelId = "second" }];

        destination.CopyFrom(source);

        Assert.HasCount(2, selector.Items);
        Assert.AreSame(destination[0], selector.SelectedItem);
    }

    // A selector learns membership from CollectionChanged and writes back null
    // when its selected item is removed. It cannot select an unseen item.
    private sealed class SelectorMirror : IDisposable
    {
        private readonly ObservableSettingsCollection<ModelProfile, Guid> source;

        public SelectorMirror(ObservableSettingsCollection<ModelProfile, Guid> source)
        {
            this.source = source;
            Items.AddRange(source);
            SelectedItem = source.CurrentItem;
            source.CollectionChanged += OnCollectionChanged;
            ((INotifyPropertyChanged)source).PropertyChanged += OnPropertyChanged;
        }

        public List<ModelProfile> Items { get; } = [];

        public ModelProfile? SelectedItem { get; private set; }

        public void Dispose()
        {
            source.CollectionChanged -= OnCollectionChanged;
            ((INotifyPropertyChanged)source).PropertyChanged -= OnPropertyChanged;
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Items.Clear();
            Items.AddRange(source);
            if (SelectedItem is not null && !Items.Contains(SelectedItem))
            {
                SelectedItem = null;
                source.CurrentItem = null;
            }
        }

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not nameof(source.CurrentItem))
            {
                return;
            }
            ModelProfile? selected = source.CurrentItem;
            if (selected is not null && !Items.Contains(selected))
            {
                throw new ArgumentException("The selected item has not been published to the selector");
            }
            SelectedItem = selected;
        }
    }
}
