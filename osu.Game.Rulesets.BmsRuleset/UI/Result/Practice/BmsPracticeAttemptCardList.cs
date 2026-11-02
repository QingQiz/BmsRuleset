using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Practice;

internal partial class BmsPracticeAttemptCardList : CompositeDrawable
{
    internal BmsPracticeAttemptCardList(BmsPracticeSession session, Bindable<BmsPracticeAttemptResult?> selectedAttempt)
    {
        RelativeSizeAxes = Axes.Both;
        Padding = new MarginPadding { Top = 4 };
        InternalChild = new OsuScrollContainer(Direction.Vertical)
        {
            RelativeSizeAxes = Axes.Both,
            ScrollbarOverlapsContent = false,
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 4),
                Children = session.AttemptResults.Select(result => new BmsPracticeAttemptCard(result, selectedAttempt)).ToArray(),
            },
        };
    }
}
