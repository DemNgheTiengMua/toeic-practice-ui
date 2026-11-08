namespace WebMVC.Models;

public sealed record AudioEquipmentCheckModel(
    bool ShowInput = true,
    bool ShowOutput = true,
    bool Compact = false,
    string? Title = null,
    /// <summary>
    /// #221 / D18: true on a surface inside a timed or one-way sitting. The permission state is
    /// still shown, because the learner must know why a recording is failing, but no control on
    /// the surface may call getUserMedia: a system dialog over a running clock is an interruption
    /// the learner cannot control. The remedy names the pre-sitting screen instead.
    /// </summary>
    bool Sitting = false);
