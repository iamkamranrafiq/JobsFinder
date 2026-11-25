namespace JobsFinder.Domain.Applying;

/// <summary>How a DryRun apply ended (F0.4-BR2). There is no submitted state in Phase 0.</summary>
public enum ApplyRunStatus
{
    /// <summary>The form is filled up to, and not including, its submit control.</summary>
    Prepared = 0,

    /// <summary>The candidate has to continue; the reason says why.</summary>
    NeedsYou = 1,

    Failed = 2,
}
