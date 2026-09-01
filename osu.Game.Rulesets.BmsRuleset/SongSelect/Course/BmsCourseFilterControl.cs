using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect.Course;

internal partial class BmsCourseFilterControl : VisibilityContainer
{
    internal BmsCourseFilterControl(Bindable<string> searchTerm)
    {
        Anchor = Anchor.TopRight;
        Origin = Anchor.TopRight;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Shear = OsuGame.SHEAR;
        Margin = new MarginPadding { Top = -Panel.CORNER_RADIUS, Right = -40 };
        X = 150;

        InternalChildren =
        [
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                CornerRadius = Panel.CORNER_RADIUS,
                Masking = true,
                Child = new BmsCourseWedgeBackground
                {
                    Anchor = Anchor.TopRight,
                    Scale = new Vector2(-1, 1),
                },
            },
            new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Top = Panel.CORNER_RADIUS + 5, Bottom = 7, Right = 40, Left = 2 },
                Child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 40,
                    Shear = -OsuGame.SHEAR,
                    Child = new BmsCourseSearchTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                        Current = searchTerm,
                        HoldFocus = true,
                        PlaceholderText = BmsStrings.CourseSearchPlaceholder,
                    },
                },
            },
        ];
    }

    protected override bool StartHidden => true;

    private partial class BmsCourseSearchTextBox : ShearedSearchTextBox
    {
        protected override InnerSearchTextBox CreateInnerTextBox() => new CourseInnerSearchTextBox();

        private partial class CourseInnerSearchTextBox : InnerSearchTextBox
        {
            public override bool HandleLeftRightArrows => false;

            public override bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
            {
                // These platform text-selection bindings conflict with song select's Shift+Left/Right group navigation.
                if (e.Action == PlatformAction.SelectBackwardChar || e.Action == PlatformAction.SelectForwardChar)
                    return false;

                // Keep parity with SongSelectSearchTextBox: Shift+Delete belongs to song select rather than text editing.
                if (e.Action == PlatformAction.Cut && e.ShiftPressed && e.CurrentState.Keyboard.Keys.IsPressed(Key.Delete))
                    return false;

                return base.OnPressed(e);
            }
        }
    }

    protected override void PopIn()
    {
        this.MoveToX(0, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeIn(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    protected override void PopOut()
    {
        this.MoveToX(150, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeOut(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }
}