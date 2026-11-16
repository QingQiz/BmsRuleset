namespace RhythmEngine.Profiles.Common;

public enum ScoreGrade
{
    D,
    C,
    B,
    A,
    S,
    Perfect,
}

public sealed record AccuracyGrades(double S, double A, double B, double C)
{
    public ScoreGrade GradeFor(double accuracy, bool allowPerfect = true) => accuracy switch
    {
        >= 1 when allowPerfect => ScoreGrade.Perfect,
        _ when accuracy >= S => ScoreGrade.S,
        _ when accuracy >= A => ScoreGrade.A,
        _ when accuracy >= B => ScoreGrade.B,
        _ when accuracy >= C => ScoreGrade.C,
        _ => ScoreGrade.D,
    };

    public double CutoffFor(ScoreGrade grade) => grade switch
    {
        ScoreGrade.Perfect => 1,
        ScoreGrade.S => S,
        ScoreGrade.A => A,
        ScoreGrade.B => B,
        ScoreGrade.C => C,
        ScoreGrade.D => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(grade)),
    };
}
