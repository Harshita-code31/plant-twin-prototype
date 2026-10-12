/// <summary>
/// The fire-safety quiz content. Answers deliberately mirror what the AR training itself
/// teaches (ExtinguisherIdentity / FireSource ratings on the prefabs): GREY = Class A,
/// YELLOW = Class BC, RED = ABC; the trash-can scenario is Class A, the office-appliance
/// and gas-furnace scenarios are Class BC; alarm first, PASS, aim at the base, keep 4 ft
/// back, shut off the gas before extinguishing a furnace fire.
///
/// Images in Resources/QuizImages are renders of the app's own extinguisher and fire
/// prefabs, so trainees are quizzed on exactly what they saw in AR.
/// </summary>
public static class FireSafetyQuizBank
{
    private static readonly string[] ExtinguisherClassOptions =
    {
        "quiz_opt_class_a",
        "quiz_opt_class_bc",
        "quiz_opt_class_abc",
        "quiz_opt_gas_only",
    };

    public static readonly QuizQuestion[] Questions =
    {
        new QuizQuestion("quiz_q_identify_ext", "QuizImages/ext_grey", ExtinguisherClassOptions, 0, "quiz_exp_ext_grey"),
        new QuizQuestion("quiz_q_identify_ext", "QuizImages/ext_yellow", ExtinguisherClassOptions, 1, "quiz_exp_ext_yellow"),
        new QuizQuestion("quiz_q_identify_ext", "QuizImages/ext_red", ExtinguisherClassOptions, 2, "quiz_exp_ext_red"),

        new QuizQuestion("quiz_q_trash", "QuizImages/scene_trash_fire",
            new[] { "quiz_opt_grey_ext", "quiz_opt_yellow_ext", "quiz_opt_let_burn", "quiz_opt_carry_outside" },
            0, "quiz_exp_trash"),

        new QuizQuestion("quiz_q_appliance_not_use", "QuizImages/scene_appliance_fire",
            new[] { "quiz_opt_grey_ext", "quiz_opt_yellow_ext", "quiz_opt_red_ext", "quiz_opt_all_safe" },
            0, "quiz_exp_appliance"),

        new QuizQuestion("quiz_q_furnace", "QuizImages/scene_furnace_fire",
            new[] { "quiz_opt_shut_gas", "quiz_opt_open_door", "quiz_opt_spray_top", "quiz_opt_turn_up" },
            0, "quiz_exp_furnace"),

        new QuizQuestion("quiz_q_first_action", null,
            new[] { "quiz_opt_sound_alarm", "quiz_opt_grab_nearest", "quiz_opt_finish_task", "quiz_opt_take_photo" },
            0, "quiz_exp_first_action"),

        new QuizQuestion("quiz_q_pass", null,
            new[] { "quiz_opt_pass_correct", "quiz_opt_pass_wrong1", "quiz_opt_pass_wrong2", "quiz_opt_pass_wrong3" },
            0, "quiz_exp_pass"),

        new QuizQuestion("quiz_q_aim", null,
            new[] { "quiz_opt_aim_base", "quiz_opt_aim_top", "quiz_opt_aim_smoke", "quiz_opt_aim_air" },
            0, "quiz_exp_aim"),

        new QuizQuestion("quiz_q_distance", null,
            new[] { "quiz_opt_dist_4ft", "quiz_opt_dist_touch", "quiz_opt_dist_any", "quiz_opt_dist_next" },
            0, "quiz_exp_distance"),
    };
}
