using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

internal sealed partial class BmsStageHud : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    private readonly Container editHandle;
    private BmsStageHudController? controller;

    [Resolved]
    private DrawableRuleset drawableRuleset { get; set; } = null!;

    [Resolved(CanBeNull = true)]
    private SkinEditorOverlay? skinEditorOverlay { get; set; }

    public BmsStageHud()
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;
        Size = Vector2.Zero;
        AlwaysPresent = true;
        Alpha = 0;

        InternalChild = editHandle = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            BorderThickness = 2,
            BorderColour = new Color4(70, 210, 255, 255),
            Alpha = 0,
            Children =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(20, 40, 55, 255),
                    Alpha = 0.45f,
                },
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    Height = 2,
                    Colour = Color4.White,
                },
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Y,
                    Width = 2,
                    Colour = Color4.White,
                },
            ],
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (drawableRuleset is BmsDrawableRuleset bmsDrawableRuleset)
        {
            controller = bmsDrawableRuleset.StageHudController;
            controller.Register(this, this.FindClosestParent<ISerialisableDrawableContainer>());
        }

        if (skinEditorOverlay != null)
            skinEditorOverlay.State.BindValueChanged(_ => updateEditModeVisibility(), true);
        else
            applyEditModeVisibility(false);
    }

    protected override void Dispose(bool isDisposing)
    {
        controller?.Unregister(this);
        controller = null;

        base.Dispose(isDisposing);
    }

    private void updateEditModeVisibility() => applyEditModeVisibility(skinEditorOverlay?.State.Value == Visibility.Visible);

    private void applyEditModeVisibility(bool isEditing)
    {
        ClearTransforms();

        // Runtime rendering belongs to BmsStage; this shell only exposes a stable skin-editor handle.
        Alpha = isEditing ? 1 : 0;
        editHandle.Alpha = isEditing ? 1 : 0;
    }
}
