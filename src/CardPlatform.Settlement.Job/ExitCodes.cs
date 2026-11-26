namespace CardPlatform.Settlement.Job;

/// <summary>Exit codes of the job process.</summary>
public static class ExitCodes
{
    public const int Success = 0;

    /// <summary>The command ran and failed; the reason is logged.</summary>
    public const int Failure = 1;

    /// <summary>The command line is invalid; the usage is written to the standard error.</summary>
    public const int InvalidArguments = 2;
}
