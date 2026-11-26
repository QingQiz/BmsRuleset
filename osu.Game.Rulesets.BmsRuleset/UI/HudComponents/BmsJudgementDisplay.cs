using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Embedded;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public sealed partial class BmsJudgementDisplay : BmsHudComponent
{
    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.JudgementDisplayShowEmptyPoor), nameof(BmsStrings.JudgementDisplayShowEmptyPoorDescription))]
    public Bindable<bool> ShowEmptyPoor { get; } = new BindableBool(true);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.JudgementDisplayPerfectImage), nameof(BmsStrings.JudgementDisplayImageDescription), SettingControlType = typeof(ImageSelector))]
    public Bindable<string> PerfectImage { get; } = new(string.Empty);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.JudgementDisplayGreatImage), nameof(BmsStrings.JudgementDisplayImageDescription), SettingControlType = typeof(ImageSelector))]
    public Bindable<string> GreatImage { get; } = new(string.Empty);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.JudgementDisplayGoodImage), nameof(BmsStrings.JudgementDisplayImageDescription), SettingControlType = typeof(ImageSelector))]
    public Bindable<string> GoodImage { get; } = new(string.Empty);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.JudgementDisplayOkImage), nameof(BmsStrings.JudgementDisplayImageDescription), SettingControlType = typeof(ImageSelector))]
    public Bindable<string> OkImage { get; } = new(string.Empty);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.JudgementDisplayMehImage), nameof(BmsStrings.JudgementDisplayImageDescription), SettingControlType = typeof(ImageSelector))]
    public Bindable<string> MehImage { get; } = new(string.Empty);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.JudgementDisplayMissImage), nameof(BmsStrings.JudgementDisplayImageDescription), SettingControlType = typeof(ImageSelector))]
    public Bindable<string> MissImage { get; } = new(string.Empty);

    private readonly Dictionary<HitResult, BmsJudgementDrawable> drawableCache = new();
    private readonly Container drawablePool;
    private readonly Container displayArea;
    private HitResult? pendingJudgement;
    private BmsJudgementDrawable? displayedDrawable;

    [Cached(typeof(ISkinSource))]
    private readonly BmsEmbeddedSkinSource activeSkin = new();

    private IBmsGameplayEvents? gameplayEvents;
    private GameplayProfile profile = null!;

    [Resolved]
    private DrawableRuleset drawableRuleset { get; set; } = null!;

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved]
    private ISkinSource parentSkin { get; set; } = null!;

    public BmsJudgementDisplay()
    {
        AlwaysPresent = true;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Anchor = Anchor.TopCentre;
        Origin = Anchor.Centre;

        InternalChildren =
        [
            displayArea = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.Centre,
            },
            drawablePool = new Container { Alpha = 0, RelativeSizeAxes = Axes.Both },
        ];
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        if (gameplayEvents != null)
        {
            gameplayEvents.JudgementDisplayed -= showJudgement;
            gameplayEvents.ReplaySeeked -= resetReplayDisplay;
        }

        parentSkin.SourceChanged -= updateEmbeddedSkinFallback;
        activeSkin.DisposeEmbeddedSkins();

        base.Dispose(isDisposing);
    }

    #endregion

    protected override void LoadComplete()
    {
        base.LoadComplete();

        gameplayEvents = (drawableRuleset as BmsDrawableRuleset)?.GameplayEvents;

        if (gameplayEvents != null)
        {
            gameplayEvents.JudgementDisplayed += showJudgement;
            gameplayEvents.ReplaySeeked += resetReplayDisplay;
        }
    }

    private void resetReplayDisplay()
    {
        pendingJudgement = null;
        displayArea.Hide();
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        profile = GameplayProfile.FromMods(drawableRuleset.Mods);
        parentSkin.SourceChanged += updateEmbeddedSkinFallback;
        updateEmbeddedSkinFallback();

        foreach (var result in BmsRuleset.STATIC_VALID_HIT_RESULTS)
        {
            var image = result switch
            {
                HitResult.Perfect => PerfectImage,
                HitResult.Great => GreatImage,
                HitResult.Good => GoodImage,
                HitResult.Ok => OkImage,
                HitResult.Meh => MehImage,
                HitResult.Miss => MissImage,
                _ => throw new ArgumentOutOfRangeException(nameof(result))
            };

            var drawable = profile.CreateJudgementDrawable(result, image).With(drawable =>
            {
                drawable.Anchor = Anchor.TopCentre;
                drawable.Origin = Anchor.TopCentre;
            });

            drawableCache[result] = drawable;
            drawablePool.Add(drawable);
        }
    }

    internal void ApplyDefaultPosition()
    {
        Y = activeSkin.GetConfig<BmsSkinConfigurationLookup, float>(
            new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ScorePosition)
        )?.Value ?? 300 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR;
    }

    private void updateEmbeddedSkinFallback()
    {
        if (drawableRuleset is BmsDrawableRuleset { Beatmap: BmsBeatmap beatmap })
            activeSkin.SetSources(parentSkin, BmsEmbeddedSkinFallbackFactory.Create(parentSkin.AllSources, beatmap, host.Renderer));
    }

    private void showJudgement(HitResult result)
    {
        if (!ShowEmptyPoor.Value && profile.TimingKindFor(result) == ResultTimingKind.EmptyInput)
            return;

        // Intermediate replay judgements cannot be displayed within the same HUD frame.
        // Keep the final result while scoring and timing observations still receive every event.
        pendingJudgement = result;
    }

    protected override void Update()
    {
        base.Update();
        if (pendingJudgement is not { } result)
            return;

        pendingJudgement = null;
        displayArea.Show();
        if (!drawableCache.TryGetValue(result, out var drawable))
            return;

        if (displayedDrawable != drawable)
        {
            displayArea.Clear(false);
            if (displayedDrawable != null)
                drawablePool.Add(displayedDrawable);
            drawablePool.Remove(drawable, false);
            displayArea.Add(displayedDrawable = drawable);
        }

        drawable.PlayAnimation();
    }

    private sealed partial class ImageSelector : SettingsDropdown<string>
    {
        public ImageSelector()
        {
            AlwaysShowSearchBar = true;
        }

        [BackgroundDependencyLoader]
        private void load(SkinManager skins)
        {
            var display = (BmsJudgementDisplay)SettingSourceObject;
            // Preserve saved choices even when their source files have been removed.
            Items = BmsSkinImageFiles.Enumerate(skins, display.activeSkin).Prepend(string.Empty).Append(Current.Value).Distinct(StringComparer.Ordinal);
        }

        protected override OsuDropdown<string> CreateDropdown() => new ImageDropdown();

        private sealed partial class ImageDropdown : DropdownControl
        {
            protected override LocalisableString GenerateItemText(string item)
                => string.IsNullOrEmpty(item) ? BmsStrings.JudgementDisplayDefaultImage : item;
        }
    }
}
