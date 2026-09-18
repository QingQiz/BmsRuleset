using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.Scoring;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public sealed partial class DrawableBmsInvisibleNote : DrawableBmsHitObject
{
    private readonly Bindable<bool> showInvisibleNotes = new();

    protected override bool UsesPassiveResultCheck => false;

    internal override bool RequiresColumnFrameUpdate => false;

    protected override bool SkipFurtherUpdates => !showInvisibleNotes.Value || Time.Current > HitObject.StartTime;

    protected override float VisualHeight => 8 * (ParentColumn?.NoteHeightScale ?? 1);

    public override bool TryHit(HitResult result) => false;

    [BackgroundDependencyLoader(true)]
    private void load(BmsRulesetConfigManager? config)
    {
        config?.BindWith(BmsRulesetSetting.ShowInvisibleNotes, showInvisibleNotes);

        AddInternal(NoteContainer = new Container
        {
            RelativeSizeAxes = Axes.X,
            Child = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.BottomLeft,
                RelativeSizeAxes = Axes.X,
                Height = 8,
                Masking = true,
                BorderThickness = 2,
                BorderColour = Color4.Yellow,
                // A transparent interior keeps simultaneous visible notes readable.
                Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
            },
        });
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
    }
}
