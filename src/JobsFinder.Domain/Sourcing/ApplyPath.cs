namespace JobsFinder.Domain.Sourcing;

/// <summary>How a posting can be applied to (F0.2-BR2, spec 3.11).</summary>
public enum ApplyPath
{
    /// <summary>No usable apply link, or a link to a site we do not recognise.</summary>
    Unknown = 0,

    /// <summary>A public board of a known applicant tracking system, e.g. Greenhouse.</summary>
    AtsUrl = 1,

    /// <summary>Applications go to an email address.</summary>
    Email = 2,

    /// <summary>LinkedIn Easy Apply or Indeed Apply; never automated.</summary>
    NativeApply = 3,
}
