using System.Collections.Immutable;
using RhythmEngine.Core;
using osu.Framework.Localisation;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;

internal sealed record GameplayHealthHistoryEvent(double Time, HealthState Health, ImmutableArray<HealthHistoryLayer> Layers, int SelectedLayer);

internal sealed record HealthHistoryLayer(LocalisableString Name, Color4 Colour, double Value, bool Failed, bool Cleared);
