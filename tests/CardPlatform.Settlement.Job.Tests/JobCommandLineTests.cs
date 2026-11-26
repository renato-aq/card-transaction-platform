using CardPlatform.Settlement.Job.Generation;
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
        Assert.Equal(new RunOptions(Yesterday, "BRL", "settlement-files"), commandLine.Run);
    }

    [Fact]
    public void No_arguments_settle_yesterday_in_brl_into_the_default_directory()
    {
        JobCommandLine? commandLine = JobCommandLine.Parse([], Yesterday, out _);

        Assert.Equal(new RunOptions(Yesterday, "BRL", "settlement-files"), commandLine?.Run);
    }

    [Fact]
    public void Run_without_options_uses_the_defaults()
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(["run"], Yesterday, out string? error);

        Assert.Null(error);
        Assert.NotNull(commandLine);
        Assert.Equal(JobCommand.Run, commandLine.Command);
        Assert.Empty(commandLine.HostArgs);
        Assert.Null(commandLine.Seed);
        Assert.Equal(new RunOptions(Yesterday, "BRL", "settlement-files"), commandLine.Run);
    }

    [Fact]
    public void Run_options_are_read_in_both_forms()
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(
            ["run", "--date", "2026-01-31", "--currency=USD", "--output", "/var/settlement"],
            Yesterday,
            out string? error);

        Assert.Null(error);
        Assert.Equal(new RunOptions(new DateOnly(2026, 1, 31), "USD", "/var/settlement"), commandLine?.Run);
    }

    [Theory]
    [InlineData("--date", "2026-11-20", "--currency", "USD", "--output", "/var/settlement")]
    [InlineData("--date=2026-11-20", "--currency=USD", "--output=/var/settlement")]
    [InlineData("--currency", "USD", "--output=/var/settlement", "--date", "2026-11-20")]
    public void Run_options_without_the_run_word_are_read_as_after_run(params string[] args)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(args, Yesterday, out string? error);
        JobCommandLine? withRunWord = JobCommandLine.Parse(["run", .. args], Yesterday, out _);

        Assert.Null(error);
        Assert.NotNull(commandLine);
        Assert.Equal(JobCommand.Run, commandLine.Command);
        Assert.Empty(commandLine.HostArgs);
        Assert.Null(commandLine.Seed);
        Assert.Equal(new RunOptions(new DateOnly(2026, 11, 20), "USD", "/var/settlement"), commandLine.Run);
        Assert.Equal(withRunWord?.Run, commandLine.Run);
    }

    [Fact]
    public void Run_options_without_the_run_word_are_separated_from_the_host_options()
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(
            ["--environment=Development", "--date", "2026-11-20", "--urls", "http://localhost:5000", "--currency=USD", "--Logging:LogLevel:Default=Warning"],
            Yesterday,
            out string? error);

        Assert.Null(error);
        Assert.NotNull(commandLine);
        Assert.Equal(JobCommand.Run, commandLine.Command);
        Assert.Equal(
            ["--environment=Development", "--urls", "http://localhost:5000", "--Logging:LogLevel:Default=Warning"],
            commandLine.HostArgs);
        Assert.Equal(new RunOptions(new DateOnly(2026, 11, 20), "USD", "settlement-files"), commandLine.Run);
    }

    [Theory]
    [InlineData("--urls", "http://localhost:5000")]
    [InlineData("--Logging:LogLevel:Default=Warning")]
    [InlineData("--environment", "Development")]
    public void Host_options_alone_run_with_the_defaults_and_go_to_the_host(params string[] args)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(args, Yesterday, out string? error);

        Assert.Null(error);
        Assert.NotNull(commandLine);
        Assert.Equal(JobCommand.Run, commandLine.Command);
        Assert.Equal(args, commandLine.HostArgs);
        Assert.Equal(new RunOptions(Yesterday, "BRL", "settlement-files"), commandLine.Run);
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
    [InlineData("run", "--date")]
    [InlineData("run", "--date", "2026-02-30")]
    [InlineData("run", "--date", "20261120")]
    [InlineData("run", "--currency", "brl")]
    [InlineData("run", "--output", " ")]
    [InlineData("run", "--transactions", "10")]
    [InlineData("run", "extra")]
    [InlineData("--date")]
    [InlineData("--date", "2026-02-30")]
    [InlineData("--date=20261120")]
    [InlineData("--currency", "brl")]
    [InlineData("--environment=Development", "--currency=REAL")]
    [InlineData("--output", " ")]
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

    [Fact]
    public async Task An_invalid_run_option_without_the_run_word_exits_with_the_invalid_arguments_code()
    {
        int exitCode = await SettlementJobHost.RunAsync(["--date", "2026-02-30"]);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
    }
}
