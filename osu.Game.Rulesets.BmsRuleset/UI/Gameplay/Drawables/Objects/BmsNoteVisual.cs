using System;
using osu.Framework.Graphics;
using osu.Framework.Layout;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Runtime;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

internal sealed partial class BmsNoteVisual : BmsCachedSkinnableDrawable
{
    internal event Action? ComponentChanging;

    internal event Action? ComponentChanged;

    internal event Action? ComponentInvalidated;

    internal void DetachComponent() => RemoveInternal(Drawable, false);

    internal void AttachComponent() => AddInternal(Drawable);

    protected override void SkinChanged(ISkinSource skin)
    {
        ComponentChanging?.Invoke();
        base.SkinChanged(skin);
        ComponentChanged?.Invoke();
    }

    protected override bool OnInvalidate(Invalidation invalidation, InvalidationSource source)
    {
        var anyInvalidated = base.OnInvalidate(invalidation, source);

        // The controller is detached while its static Sprite is mounted directly on the note.
        // Changes made to the controller while detached must restore the composition before
        // the next update can render with stale wrapper state.
        if (Parent == null && source == InvalidationSource.Self)
            ComponentInvalidated?.Invoke();

        return anyInvalidated;
    }

    internal static void ApplyResultEffects(Drawable drawable, ArmedState state, double time)
    {
        switch (state)
        {
            case ArmedState.Hit:
                drawable.FadeOut();
                drawable.LifetimeEnd = time;
                break;

            case ArmedState.Miss:
                drawable.FadeColour(Color4.Red, 80).FadeOut(220).Expire();
                drawable.LifetimeEnd = time + 300;
                break;
        }
    }

    internal BmsNoteVisual(BmsSkinComponents component, BmsLayoutVariant layout, int column)
        : base(new BmsSkinComponentLookup(component, layout, column))
    {
        // The head owns the skin directly so each scroll invalidation traverses one less container.
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        RelativeSizeAxes = Axes.X;
        Height = 0;
        ComponentAnchor = Anchor.BottomCentre;
    }

    internal float VisualHeight
    {
        get
        {
            var drawable = Drawable;
            if (drawable == null) return 0;

            // Absolute skin heights avoid waking width-dependent layout on culled trees.
            return (drawable.RelativeSizeAxes & Axes.Y) == 0 ? drawable.Height : drawable.DrawHeight;
        }
    }
}
