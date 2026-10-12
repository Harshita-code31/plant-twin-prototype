/// <summary>
/// The conveyor machine-safety quiz content. Answers mirror how the AR conveyor itself behaves
/// (ConveyorMotionController): the E-stop stops the belt in any mode and latches; resetting it
/// does NOT restart the belt (START/jog must be pressed again); STOP is the normal stop; in
/// MANUAL the belt only moves while the green jog button is held; switching AUTO/MANUAL stops
/// the belt first and is blocked while E-stopped. Plus general conveyor safety (pinch points,
/// clearing jams, checking the area before starting).
///
/// Images in Resources/QuizImages are renders of the app's own Conveyor prefab.
/// </summary>
public static class MachineSafetyQuizBank
{
    public static readonly QuizQuestion[] Questions =
    {
        new QuizQuestion("quiz_m_q_estop", "QuizImages/machine_estop",
            new[] { "quiz_m_opt_estop_emergency", "quiz_m_opt_estop_start", "quiz_m_opt_estop_speed", "quiz_m_opt_estop_mode" },
            0, "quiz_m_exp_estop"),

        new QuizQuestion("quiz_m_q_caught", "QuizImages/machine_control_box",
            new[] { "quiz_m_opt_caught_estop", "quiz_m_opt_caught_stop_wait", "quiz_m_opt_caught_pull", "quiz_m_opt_caught_slow" },
            0, "quiz_m_exp_caught"),

        new QuizQuestion("quiz_m_q_after_estop", null,
            new[] { "quiz_m_opt_after_reset_start", "quiz_m_opt_after_auto", "quiz_m_opt_after_manual", "quiz_m_opt_after_speed" },
            0, "quiz_m_exp_after_estop"),

        new QuizQuestion("quiz_m_q_normal_stop", null,
            new[] { "quiz_m_opt_normal_stop", "quiz_m_opt_normal_estop", "quiz_m_opt_normal_mode", "quiz_m_opt_normal_slow" },
            0, "quiz_m_exp_normal_stop"),

        new QuizQuestion("quiz_m_q_manual", null,
            new[] { "quiz_m_opt_manual_hold", "quiz_m_opt_manual_continuous", "quiz_m_opt_manual_fast", "quiz_m_opt_manual_never" },
            0, "quiz_m_exp_manual"),

        new QuizQuestion("quiz_m_q_mode_switch", null,
            new[] { "quiz_m_opt_switch_stops", "quiz_m_opt_switch_keeps", "quiz_m_opt_switch_faster", "quiz_m_opt_switch_reverse" },
            0, "quiz_m_exp_mode_switch"),

        new QuizQuestion("quiz_m_q_mode_estopped", null,
            new[] { "quiz_m_opt_estopped_reset_first", "quiz_m_opt_estopped_anytime", "quiz_m_opt_estopped_manual_only", "quiz_m_opt_estopped_running" },
            0, "quiz_m_exp_mode_estopped"),

        new QuizQuestion("quiz_m_q_hands", "QuizImages/machine_conveyor",
            new[] { "quiz_m_opt_hands_clear", "quiz_m_opt_hands_guide", "quiz_m_opt_hands_rollers", "quiz_m_opt_hands_under" },
            0, "quiz_m_exp_hands"),

        new QuizQuestion("quiz_m_q_jam", "QuizImages/machine_conveyor",
            new[] { "quiz_m_opt_jam_stop_latch", "quiz_m_opt_jam_quick", "quiz_m_opt_jam_faster", "quiz_m_opt_jam_jog" },
            0, "quiz_m_exp_jam"),

        new QuizQuestion("quiz_m_q_before_start", null,
            new[] { "quiz_m_opt_before_clear", "quiz_m_opt_before_fast", "quiz_m_opt_before_nothing", "quiz_m_opt_before_estop" },
            0, "quiz_m_exp_before_start"),
    };
}
