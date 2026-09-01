// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// Copied from osu.Game.Screens.Select.LeftSideInteractionContainer at osu!
// revision 3c1c96f742e7aae2ff67a7361e058fe91ca3b955.

using System;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input;
using osu.Framework.Input.Events;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal partial class BmsLeftSideInteractionContainer(Action resetCarouselPosition) : Container
{
    private readonly Action? resetCarouselPosition = resetCarouselPosition;
    private bool mouseContained;
    private InputManager inputManager = null!;

    protected override bool OnScroll(ScrollEvent e) => !e.ControlPressed && !e.AltPressed && !e.ShiftPressed && !e.SuperPressed;

    protected override bool OnMouseDown(MouseDownEvent e) => true;

    protected override void LoadComplete()
    {
        inputManager = GetContainingInputManager()!;
        base.LoadComplete();
    }

    protected override void Update()
    {
        base.Update();

        if (Contains(inputManager.CurrentState.Mouse.Position))
        {
            if (!mouseContained)
            {
                mouseContained = true;
                resetCarouselPosition?.Invoke();
            }
        }
        else
            mouseContained = false;
    }
}
