using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Game.Graphics.UserInterface;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

internal partial class BmsCourseResultButton : TwoLayerButton
{
    private const float additional_text_width = 140;
    private const double transform_time = 600;

    private Drawable iconContainer = null!;
    private Drawable textContainer = null!;
    private float iconWidthRatio;

    internal BmsCourseResultButton()
    {
        Size = SIZE_RETRACTED + new Vector2(additional_text_width, 0);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        iconContainer = IconLayer.Parent!.Parent!;
        textContainer = TextLayer.Parent!.Parent!;
        iconWidthRatio = iconContainer.Width;
        iconContainer.RelativeSizeAxes = textContainer.RelativeSizeAxes = Axes.Y;
    }

    protected override void Update()
    {
        base.Update();

        // Long course labels should not enlarge the native animated icon segment.
        iconContainer.Width = (DrawWidth - additional_text_width) * iconWidthRatio;
        textContainer.Width = DrawWidth - iconContainer.Width;
    }

    protected override bool OnHover(HoverEvent e)
    {
        var handled = base.OnHover(e);
        this.ResizeTo(SIZE_EXTENDED + new Vector2(additional_text_width, 0), transform_time, Easing.OutElastic);
        return handled;
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        base.OnHoverLost(e);
        this.ResizeTo(SIZE_RETRACTED + new Vector2(additional_text_width, 0), transform_time, Easing.Out);
    }
}
