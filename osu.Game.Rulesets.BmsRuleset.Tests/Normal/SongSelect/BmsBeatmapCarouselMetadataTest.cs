using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public partial class BmsBeatmapCarouselMetadataTest
{
    [Test]
    public void TestPlayedUpdatePreservesOrderWithoutRequestingSelection()
    {
        var previous = createSet();
        var updated = cloneSet(previous);
        updated.Beatmaps[1].LastPlayed = DateTimeOffset.UtcNow;
        var unrelated = new BeatmapInfo();
        var selected = previous.Beatmaps[1];
        var selectionRequests = 0;
        var metadataRequests = 0;
        using var carousel = new TestCarousel
        {
            RequestSelection = _ => selectionRequests++,
            BeatmapMetadataUpdated = _ => metadataRequests++,
        };
        carousel.Models.AddRange([previous.Beatmaps[0], unrelated, selected]);
        carousel.CurrentGroupedBeatmap = new GroupedBeatmap(null, selected);
        var notifications = new List<NotifyCollectionChangedEventArgs>();
        carousel.Models.CollectionChanged += (_, change) => notifications.Add(change);

        Assert.That(carousel.TryQueueMetadataUpdate(replace(previous, updated)), Is.True);
        Assert.That(carousel.Models[2], Is.SameAs(selected), "Suspended song select must defer its changes.");
        carousel.ApplyPendingUpdates();

        Assert.Multiple(() =>
        {
            Assert.That(carousel.Models.Select(beatmap => beatmap.ID),
                Is.EqualTo(new[] { previous.Beatmaps[0].ID, unrelated.ID, selected.ID }));
            Assert.That(carousel.Models[2], Is.SameAs(updated.Beatmaps[1]));
            Assert.That(carousel.Models[2].LastPlayed, Is.EqualTo(updated.Beatmaps[1].LastPlayed));
            Assert.That(notifications, Has.Count.EqualTo(2));
            Assert.That(notifications.All(change => change.Action == NotifyCollectionChangedAction.Replace), Is.True);
            Assert.That(selectionRequests, Is.Zero);
            Assert.That(metadataRequests, Is.Zero);
        });
    }

    [Test]
    public void TestRenamedOfflineDifficultiesAreMatchedByIdentity()
    {
        var previous = createSet();
        var updated = cloneSet(previous);
        updated.Beatmaps[0].DifficultyName = "Renamed difficulty";
        BeatmapInfo refreshed = null;
        using var carousel = new TestCarousel { BeatmapMetadataUpdated = beatmap => refreshed = beatmap };
        carousel.Models.AddRange(previous.Beatmaps.Reverse());
        carousel.CurrentGroupedBeatmap = new GroupedBeatmap(null, previous.Beatmaps[0]);

        Assert.That(carousel.TryQueueMetadataUpdate(replace(previous, updated)), Is.True);
        carousel.ApplyPendingUpdates();

        Assert.That(carousel.Models[0], Is.SameAs(updated.Beatmaps[1]));
        Assert.That(carousel.Models[1], Is.SameAs(updated.Beatmaps[0]));
        Assert.That(refreshed, Is.SameAs(updated.Beatmaps[0]));
    }

    [Test]
    public void TestQueuedUpdatesApplyInOrder()
    {
        var previous = createSet();
        var first = cloneSet(previous);
        var latest = cloneSet(previous);
        first.Beatmaps[0].LastPlayed = DateTimeOffset.UtcNow.AddDays(-1);
        latest.Beatmaps[0].LastPlayed = DateTimeOffset.UtcNow;
        using var carousel = new TestCarousel();
        carousel.Models.AddRange(previous.Beatmaps);

        Assert.That(carousel.TryQueueMetadataUpdate(replace(previous, first)), Is.True);
        Assert.That(carousel.TryQueueMetadataUpdate(replace(first, latest)), Is.True);
        carousel.ApplyPendingUpdates();

        Assert.That(carousel.Models[0], Is.SameAs(latest.Beatmaps[0]));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestChangedDifficultyMembershipUsesOriginalHandler(bool sameCount)
    {
        var previous = createSet();
        var updated = cloneSet(previous);
        if (sameCount)
            updated.Beatmaps[0].ID = Guid.NewGuid();
        else
            updated.Beatmaps.RemoveAt(0);

        using var carousel = new TestCarousel();
        Assert.That(carousel.TryQueueMetadataUpdate(replace(previous, updated)), Is.False);
    }

    private static BeatmapSetInfo createSet()
    {
        var set = new BeatmapSetInfo();
        for (var i = 0; i < 2; i++)
            set.Beatmaps.Add(new BeatmapInfo { BeatmapSet = set, DifficultyName = "Same offline difficulty name" });
        return set;
    }

    private static BeatmapSetInfo cloneSet(BeatmapSetInfo previous)
    {
        var updated = new BeatmapSetInfo { ID = previous.ID };
        foreach (var beatmap in previous.Beatmaps)
        {
            var clone = beatmap.Clone();
            clone.BeatmapSet = updated;
            updated.Beatmaps.Add(clone);
        }

        return updated;
    }

    private static NotifyCollectionChangedEventArgs replace(BeatmapSetInfo previous, BeatmapSetInfo updated) =>
        new(NotifyCollectionChangedAction.Replace, updated, previous);

    private partial class TestCarousel : BmsBeatmapCarousel
    {
        [SetsRequiredMembers]
        public TestCarousel()
        {
            RequestSelection = _ => { };
            RequestRecommendedSelection = _ => { };
        }

        internal BindableList<BeatmapInfo> Models => Items;

        internal void ApplyPendingUpdates() => Scheduler.Update();
    }
}
