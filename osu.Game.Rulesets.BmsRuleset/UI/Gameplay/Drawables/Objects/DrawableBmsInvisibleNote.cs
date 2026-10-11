using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public sealed partial class DrawableBmsInvisibleNote : DrawableBmsHitObject
{
    // The marker owns its masking node; this standard outer container can be flattened.
    protected override bool CanBeFlattened => !Masking && !HasProxy && !ForceLocalVertexBatch;

    private readonly Bindable<bool> showInvisibleNotes = new();
    private Container marker = null!;

    protected override void ApplyVisualHeightScale(float scale) => marker.Scale = new Vector2(1, scale);

    protected override bool SkipFurtherUpdates => !showInvisibleNotes.Value || Time.Current > HitObject.StartTime;

    protected override float VisualHeight => 8 * (ParentColumn?.NoteHeightScale ?? 1);

    [BackgroundDependencyLoader(true)]
    private void load(BmsRulesetConfigManager? config)
    {
        config?.BindWith(BmsRulesetSetting.ShowInvisibleNotes, showInvisibleNotes);

        Visuals.Add(marker = new Container
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
