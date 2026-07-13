using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Configuration;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

internal sealed partial class BmsStageHud : BmsHudComponent
{
    [SettingSource("Judgement line offset", "Moves the judgement line relative to the skin position. Positive values move it upward.")]
    public BindableFloat JudgementLineOffset { get; } = new()
    {
        MinValue = -768,
        MaxValue = 768,
        Precision = 1,
    };

    private readonly Container editHandle;
    private BmsStageHudController? controller;

    [Resolved]
    private DrawableRuleset drawableRuleset { get; set; } = null!;

    public BmsStageHud()
    {
        Anchor = Anchor.BottomCentre;
        Origin = Anchor.BottomCentre;
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
            JudgementLineOffset.ValueChanged += onJudgementLineOffsetChanged;
            controller.SetHitTargetPositionOffset(JudgementLineOffset.Value);
        }

        if (SkinEditor != null)
            SkinEditor.State.BindValueChanged(_ => updateEditModeVisibility(), true);
        else
            applyEditModeVisibility(false);
    }

    protected override void Dispose(bool isDisposing)
    {
        JudgementLineOffset.ValueChanged -= onJudgementLineOffsetChanged;
        controller?.Unregister(this);
        controller = null;

        base.Dispose(isDisposing);
    }

    private void onJudgementLineOffsetChanged(ValueChangedEvent<float> offset) => controller?.SetHitTargetPositionOffset(offset.NewValue);

    internal void SetJudgementLineOffsetRange(float minimum, float maximum)
    {
        JudgementLineOffset.MinValue = minimum;
        JudgementLineOffset.MaxValue = maximum;
    }

    private void updateEditModeVisibility() => applyEditModeVisibility(SkinEditor?.State.Value == Visibility.Visible);

    private void applyEditModeVisibility(bool isEditing)
    {
        ClearTransforms();

        // Runtime rendering belongs to BmsStage; this shell only exposes a stable skin-editor handle.
        Alpha = isEditing ? 1 : 0;
        editHandle.Alpha = isEditing ? 1 : 0;
    }
}
