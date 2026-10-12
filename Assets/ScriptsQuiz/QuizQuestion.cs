/// <summary>
/// One multiple-choice question. Every string is a LocalizationManager key (see
/// Resources/Localization/{lang}.json, "quiz_*" keys) so quizzes follow the app's language
/// setting like the rest of the UI.
/// </summary>
public class QuizQuestion
{
    public readonly string questionKey;

    /// <summary>Sprite path under Resources/ (e.g. "QuizImages/ext_grey"), or null for a text-only question.</summary>
    public readonly string imagePath;

    /// <summary>Exactly four option keys, authored with the correct answer at <see cref="correctIndex"/>. The UI shuffles display order.</summary>
    public readonly string[] optionKeys;

    public readonly int correctIndex;
    public readonly string explanationKey;

    public QuizQuestion(string questionKey, string imagePath, string[] optionKeys, int correctIndex, string explanationKey)
    {
        this.questionKey = questionKey;
        this.imagePath = imagePath;
        this.optionKeys = optionKeys;
        this.correctIndex = correctIndex;
        this.explanationKey = explanationKey;
    }
}

/// <summary>Which training module's question set a TrainingQuizUI runs.</summary>
public enum QuizBank
{
    FireSafety,
    MachineSafety,
}

public static class QuizBanks
{
    public static QuizQuestion[] Get(QuizBank bank)
    {
        switch (bank)
        {
            case QuizBank.MachineSafety:
                return MachineSafetyQuizBank.Questions;
            default:
                return FireSafetyQuizBank.Questions;
        }
    }
}
