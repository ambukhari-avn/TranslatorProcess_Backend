namespace TranslationBackend.Domain.Enum
{
    /// <summary>
    /// Authorization role (names are shared with the SupplyChain app so the same frontend auth code works).
    ///   Employee  = default; every Microsoft sign-in starts here. Can translate documents and see their own jobs.
    ///   SuperUser = the dev team ("Developer"): everything, including approving sign-ins, managing employees and the
    ///               history of all users' jobs.
    /// </summary>
    public enum Role
    {
        Employee = 1,
        SuperUser = 2,
    }

    /// <summary>Same names as the extraction service reports.</summary>
    public enum JobStatus
    {
        Queued = 1,
        Running = 2,
        Done = 3,
        Failed = 4,
        Cancelled = 5,
    }
}
