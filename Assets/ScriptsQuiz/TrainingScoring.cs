using UnityEngine;

/// <summary>
/// Pass rule shared by every training module: the hands-on practice counts for 40% of the
/// final mark and the quiz for 60%, and the training is passed at a total of 80/100 or more.
/// All inputs and outputs are percentages (0-100).
/// </summary>
public static class TrainingScoring
{
    public const float PracticeWeight = 0.4f;
    public const float QuizWeight = 0.6f;
    public const int PassMark = 80;

    public static int Total(int practicePercent, int quizPercent)
    {
        return Mathf.RoundToInt(practicePercent * PracticeWeight + quizPercent * QuizWeight);
    }

    public static bool IsPass(int practicePercent, int quizPercent)
    {
        return Total(practicePercent, quizPercent) >= PassMark;
    }

    public static int Percent(int correct, int total)
    {
        return total > 0 ? Mathf.RoundToInt(correct * 100f / total) : 0;
    }
}
