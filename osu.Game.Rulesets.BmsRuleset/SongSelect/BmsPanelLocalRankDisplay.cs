// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osu.Game.Scoring;
using osuTK;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal partial class BmsPanelLocalRankDisplay : PanelLocalRankDisplay
{
    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    [Resolved]
    private RealmAccess realm { get; set; } = null!;

    private readonly IBindable<APIUser> localUser = new Bindable<APIUser>();

    private IDisposable? scoreSubscription;

    private readonly UpdateableRank updateable;
    private readonly BmsLampDisplay lampDisplay;

    internal void AttachLamp(Container host)
    {
        if (lampDisplay.Parent == this)
            RemoveInternal(lampDisplay, false);
        else if (lampDisplay.Parent is Container parent)
            parent.Remove(lampDisplay, false);

        lampDisplay.RelativeSizeAxes = Axes.Both;
        lampDisplay.Width = 1;
        lampDisplay.Height = 1;
        host.Add(lampDisplay);
    }

    internal void DetachLamp()
    {
        if (lampDisplay.Parent is Container parent)
            parent.Remove(lampDisplay, false);

        AddInternal(lampDisplay);
        lampDisplay.RelativeSizeAxes = Axes.None;
        lampDisplay.Size = new Vector2(40, 20);
    }

    [Resolved(CanBeNull = true)]
    private IBindable<IReadOnlyList<Mod>>? selectedMods { get; set; }

    public BmsPanelLocalRankDisplay(BeatmapInfo? beatmap = null)
        : base(beatmap)
    {
        AutoSizeAxes = Axes.Both;

        InternalChildren =
        [
            updateable = new UpdateableRank(animate: false)
            {
                Size = new Vector2(40, 20),
                Alpha = 0,
            },
            lampDisplay = new BmsLampDisplay(BmsLamp.NoPlay)
            {
                Size = new Vector2(40, 20),
                Alpha = 0,
            },
        ];

    }

    internal void RefreshBmsScores() => updateSubscription();

    [BackgroundDependencyLoader]
    private void load(IAPIProvider api)
    {
        localUser.BindTo(api.LocalUser);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        ruleset.BindValueChanged(_ => updateSubscription());
        localUser.BindValueChanged(_ => updateSubscription(), true);
        selectedMods?.BindValueChanged(_ => updateLamp());
    }

    private void updateSubscription()
    {
        scoreSubscription?.Dispose();
        setRankFromScore(null);

        if (Beatmap == null)
            return;

        scoreSubscription = realm.RegisterForNotifications(r => r.All<ScoreInfo>().Where(s => s.BeatmapHash == Beatmap.Hash && !s.DeletePending), localScoresChanged);
    }

    private void localScoresChanged(IRealmCollection<ScoreInfo> sender, ChangeSet? changes)
    {
        // This subscription may fire from changes to linked beatmaps, which we don't care about.
        // It's currently not possible for a score to be modified after insertion, so we can safely ignore callbacks with only modifications.
        if (changes?.HasCollectionChanges() == false)
            return;

        ScoreInfo? topScore = sender
            // doing these post realm filter is most efficient.
            .Where(s => s.UserID == localUser.Value.Id || s.UserID <= 1)
            .Where(s => ruleset.Value.Equals(s.Ruleset))
            .MaxBy(info => (info.TotalScore, -info.Date.UtcDateTime.Ticks));

        if (selectedMods != null && ruleset.Value.ShortName == Constant.SHORT_NAME)
        {
            var scores = BmsSongSelectLampService.GetLocalScores(realm, Beatmap!, localUser, ruleset);
            var selection = BmsSongSelectLampService.Select(scores, selectedMods.Value);
            updateable.Rank = selection.Rank;
            updateable.Alpha = selection.Rank.HasValue ? 1 : 0;
        }
        else
            setRankFromScore(topScore);

        updateLamp();
    }

    private void setRankFromScore(ScoreInfo? topScore)
    {
        updateable.Rank = topScore?.Rank;
        updateable.Alpha = topScore != null ? 1 : 0;
    }

    private void updateLamp()
    {
        if (ruleset.Value.ShortName != Constant.SHORT_NAME || Beatmap == null || selectedMods == null)
        {
            lampDisplay.Alpha = 0;
            return;
        }

        var scores = BmsSongSelectLampService.GetLocalScores(realm, Beatmap, localUser, ruleset);
        var selection = BmsSongSelectLampService.Select(scores, selectedMods.Value);
        updateable.Rank = selection.Rank;
        updateable.Alpha = selection.Rank.HasValue ? 1 : 0;
        lampDisplay.Lamp = BmsLampCalculator.Calculate(selection.Score);
        lampDisplay.Alpha = 1;
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        scoreSubscription?.Dispose();
    }
}