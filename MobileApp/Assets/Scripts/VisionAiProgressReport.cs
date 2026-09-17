using UnityEngine;

public readonly struct VisionAiProgressReport
{
    public VisionAiPhase Phase { get; }
    public string Message { get; }
    public float? Progress01 { get; }

    public VisionAiProgressReport(
        VisionAiPhase phase,
        string message,
        float? progress01 = null)
    {
        Phase = phase;
        Message = message ?? string.Empty;
        Progress01 = progress01.HasValue
            ? Mathf.Clamp01(progress01.Value)
            : null;
    }
}
