// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics.Containers;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

public partial class BmsBeatmapDetailsArea : VisibilityContainer
{
    private Header header = null!;
    private Container contentContainer = null!;

    public BmsBeatmapDetailsArea()
    {
        RelativeSizeAxes = Axes.X;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        const float header_height = 35f;

        InternalChildren =
        [
            new ShearAligningWrapper(header = new Header
            {
                Shear = -OsuGame.SHEAR,
                RelativeSizeAxes = Axes.X,
                Height = header_height,
            }),
            new ShearAligningWrapper(contentContainer = new Container
            {
                Shear = -OsuGame.SHEAR,
                Padding = new MarginPadding { Top = header_height },
                RelativeSizeAxes = Axes.Both,
            })
            {
                Depth = 1f,
            },
        ];
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        header.Type.BindValueChanged(_ => updateDisplay(), true);
    }

    protected override void PopIn()
    {
        this.MoveToX(0, BmsSongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeIn(BmsSongSelect.ENTER_DURATION / 3, Easing.In);
    }

    protected override void PopOut()
    {
        this.MoveToX(-150, BmsSongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeOut(BmsSongSelect.ENTER_DURATION / 3, Easing.In);
    }

    private Drawable? currentContent;

    private void updateDisplay()
    {
        if (currentContent != null)
        {
            currentContent.Hide();
            currentContent.Expire();
        }

        switch (header.Type.Value)
        {
            default:
            case Header.Selection.Details:
                currentContent = new Screens.Select.BeatmapMetadataWedge();
                break;

            case Header.Selection.Ranking:
                currentContent = new BmsBeatmapLeaderboardWedge
                {
                    Scope = { BindTarget = header.Scope },
                    Sorting = { BindTarget = header.Sorting },
                    FilterBySelectedMods = { BindTarget = header.FilterBySelectedMods },
                };

                break;
        }

        contentContainer.Add(currentContent);
        currentContent.Show();
    }

    public void Refresh()
    {
        if (currentContent is BmsBeatmapLeaderboardWedge leaderboardWedge)
            leaderboardWedge.RefetchScores();
    }
}