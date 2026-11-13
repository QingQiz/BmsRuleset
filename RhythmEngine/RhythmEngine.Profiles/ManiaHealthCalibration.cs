namespace RhythmEngine.Profiles;

/// <summary>Chart-dependent legacy recovery calibration. Actual mania play has no passive drain.</summary>
public static class ManiaHealthCalibration
{
    public static double ComputeRecoveryMultiplier(KeyChart source, double drainRate, long drainStartTime = 0, bool separateHoldJudgements = true)
    {
        ProfileSupport.RequireRange(drainRate, 0, 10, nameof(drainRate));
        var notes = source.Notes.OrderBy(n => n.StartTime).ToArray();
        if (notes.Length == 0)
            return 1;

        var lowestEver = ProfileSupport.DifficultyRange(drainRate, 0.975, 0.8, 0.3);
        var lowestEnd = ProfileSupport.DifficultyRange(drainRate, 0.99, 0.9, 0.4);
        var recoveryRequired = ProfileSupport.DifficultyRange(drainRate, 0.04, 0.02, 0);
        var drop = 0.00025;
        var multiplier = 1.0;
        var perfectGain = 0.0055 - drainRate * 0.0005;

        while (true)
        {
            var hp = 1.0;
            var uncapped = 1.0;
            var lastTime = drainStartTime;
            var breakIndex = 0;
            var failed = false;
            var topLevelCount = 0;

            foreach (var note in notes)
            {
                topLevelCount++;

                while (breakIndex < source.Breaks.Length && source.Breaks[breakIndex].EndTime <= note.StartTime)
                {
                    lastTime = note.StartTime;
                    breakIndex++;
                }

                reduce(drop * Time.ToMilliseconds(note.StartTime - lastTime));
                lastTime = note.EndTime;

                if (hp <= lowestEver)
                {
                    failed = true;
                    drop *= 0.96;
                    break;
                }

                var reduction = drop * Time.ToMilliseconds(note.EndTime - note.StartTime);
                var overkill = Math.Max(0, reduction - hp);
                reduce(reduction);
                increase(perfectGain * multiplier);
                if (separateHoldJudgements && note.Kind == KeyNoteKind.Hold)
                    increase(perfectGain * multiplier);

                if (overkill > 0 && hp - overkill <= lowestEver)
                {
                    failed = true;
                    drop *= 0.96;
                    break;
                }
            }

            if (!failed && hp < lowestEnd)
            {
                failed = true;
                drop *= 0.94;
                multiplier *= 1.01;
            }

            if (!failed && (uncapped - 1) / topLevelCount < recoveryRequired)
            {
                failed = true;
                drop *= 0.96;
                multiplier *= 1.01;
            }

            // This is the upstream algorithm's explicit divergence outcome for unrecoverable calibration.
            if (double.IsInfinity(multiplier))
                return 1;
            if (!failed)
                return multiplier;

            void reduce(double amount)
            {
                hp = Math.Max(0, hp - amount);
                uncapped = Math.Max(0, uncapped - amount);
            }

            void increase(double amount)
            {
                hp = Math.Clamp(hp + amount, 0, 1);
                uncapped += amount;
            }
        }
    }
}
