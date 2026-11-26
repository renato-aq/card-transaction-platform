using CardPlatform.Settlement.Job.Seeding;

namespace CardPlatform.Settlement.Job.Tests;

public sealed class JobCommandLineTests
{
    private static readonly DateOnly Yesterday = new(2026, 11, 25);

    [Fact]
    public void No_arguments_run_the_job_and_never_seed()
    {
        JobCommandLine? commandLine = JobCommandLine.Parse([], Yesterday, out string? error);

        Assert.Null(error);
        Assert.NotNull(commandLine);
        Assert.Equal(JobCommand.Run, commandLine.Command);
        Assert.Null(commandLine.Seed);
    }

    [Fact]
    public void Leading_host_options_run_the_job_and_go_to_the_host()
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(["--environment=Development"], Yesterday, out _);

        Assert.NotNull(commandLine);
        Assert.Equal(JobCommand.Run, commandLine.Command);
        Assert.Equal(["--environment=Development"], commandLine.HostArgs);
    }

    [Fact]
    public void Seed_without_options_uses_the_defaults()
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(["seed"], Yesterday, out string? error);

        Assert.Null(error);
        Assert.NotNull(commandLine);
        Assert.Equal(JobCommand.Seed, commandLine.Command);
        Assert.Empty(commandLine.HostArgs);
        Assert.Equal(new SeedOptions(5_000_000, 1_000, 100_000, Yesterday, "BRL", 42), commandLine.Seed);
    }

    [Fact]
    public void Seed_options_are_read_in_both_forms()
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(
            ["seed", "--transactions", "1000", "--merchants=10", "--cards", "50", "--date=2026-01-31", "--currency", "USD", "--seed", "-5"],
            Yesterday,
            out string? error);

        Assert.Null(error);
        Assert.Equal(new SeedOptions(1_000, 10, 50, new DateOnly(2026, 1, 31), "USD", -5), commandLine?.Seed);
    }

    [Theory]
    [InlineData("settle")]
    [InlineData("seed", "--transactions")]
    [InlineData("seed", "--transactions", "0")]
    [InlineData("seed", "--transactions", "many")]
    [InlineData("seed", "--cards", "-1")]
    [InlineData("seed", "--date", "20/11/2026")]
    [InlineData("seed", "--currency", "brl")]
    [InlineData("seed", "--currency", "REAL")]
    [InlineData("seed", "--seed", "x")]
    [InlineData("seed", "--unknown", "1")]
    [InlineData("seed", "extra")]
    public void Invalid_command_lines_are_rejected_with_an_error(params string[] args)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(args, Yesterday, out string? error);

        Assert.Null(commandLine);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task An_invalid_command_line_exits_with_the_invalid_arguments_code()
    {
        int exitCode = await SettlementJobHost.RunAsync(["seed", "--transactions", "0"]);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
    }
}
