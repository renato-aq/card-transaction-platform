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
    [InlineData("run", "--metrics-output", "metrics/run.json")]
    [InlineData("run", "--metrics-output=metrics/run.json")]
    [InlineData("--metrics-output", "metrics/run.json")]
    [InlineData("--metrics-output=metrics/run.json")]
    public void Metrics_output_is_read_in_both_forms_with_and_without_the_run_word(params string[] args)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(args, Yesterday, out string? error);

        Assert.Null(error);
        Assert.NotNull(commandLine);
        Assert.Equal(JobCommand.Run, commandLine.Command);
        Assert.Empty(commandLine.HostArgs);
        Assert.Equal(new RunOptions(Yesterday, "BRL", "settlement-files", "metrics/run.json"), commandLine.Run);
    }

    [Fact]
    public void Without_metrics_output_the_run_writes_no_metrics_file()
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(["run", "--date", "2026-11-20"], Yesterday, out _);

        Assert.Null(commandLine?.Run?.MetricsOutputPath);
    }

    [Theory]
    [InlineData("run", "--metrics-output")]
    [InlineData("run", "--metrics-output", " ")]
    [InlineData("--metrics-output=")]
    public void A_missing_or_blank_metrics_output_is_rejected_with_an_error(params string[] args)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(args, Yesterday, out string? error);

        Assert.Null(commandLine);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("run", "--date", "2026-11-20", "--output", "out", "--metrics-output", "out/settlement-20261120-BRL.txt")]
    [InlineData("run", "--metrics-output=out/settlement-20261120-BRL.txt", "--output=out", "--date=2026-11-20")]
    [InlineData("--date", "2026-11-20", "--output", "out", "--metrics-output", "out/settlement-20261120-BRL.txt")]
    [InlineData("run", "--date", "2026-11-20", "--output", "out", "--metrics-output", "./out/../out/./settlement-20261120-BRL.txt")]
    [InlineData("run", "--date", "2026-11-20", "--output", "out/", "--metrics-output", "out/settlement-20261120-BRL.txt")]
    [InlineData("run", "--date", "2026-11-20", "--output", "./x/../out", "--metrics-output", "out/settlement-20261120-BRL.txt")]
    [InlineData("run", "--currency", "USD", "--metrics-output", "settlement-files/settlement-20261125-USD.txt")]
    public void A_metrics_output_that_is_the_settlement_file_is_rejected_with_an_error(params string[] args)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(args, Yesterday, out string? error);

        Assert.Null(commandLine);
        Assert.NotNull(error);
        Assert.Contains("'--metrics-output'", error, StringComparison.Ordinal);
        string settlementFile = args.Contains("USD")
            ? Path.GetFullPath("settlement-files/settlement-20261125-USD.txt")
            : Path.GetFullPath("out/settlement-20261120-BRL.txt");
        Assert.Contains(settlementFile, error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_metrics_output_that_is_the_settlement_file_is_rejected_given_as_relative_or_absolute()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "settlement-out");
        string absolute = Path.Combine(Path.GetTempPath(), "other", "..", "settlement-out", ".", "settlement-20261120-BRL.txt");
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.Combine(outputDirectory, "settlement-20261120-BRL.txt"));

        JobCommandLine? withAbsolute = JobCommandLine.Parse(
            ["run", "--date", "2026-11-20", "--output", outputDirectory, "--metrics-output", absolute], Yesterday, out string? absoluteError);
        JobCommandLine? withRelative = JobCommandLine.Parse(
            ["run", "--date", "2026-11-20", "--output", outputDirectory, "--metrics-output", relative], Yesterday, out string? relativeError);

        Assert.Null(withAbsolute);
        Assert.Contains("'--metrics-output'", absoluteError, StringComparison.Ordinal);
        Assert.Null(withRelative);
        Assert.Contains("'--metrics-output'", relativeError, StringComparison.Ordinal);
    }

    [Fact]
    public void A_metrics_output_differing_from_the_settlement_file_only_in_case_is_rejected_where_file_names_ignore_case()
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(
            ["run", "--date", "2026-11-20", "--output", "out", "--metrics-output", "OUT/Settlement-20261120-BRL.TXT"],
            Yesterday,
            out string? error);

        // The default file systems of Windows and macOS ignore case; those of Linux do not.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Null(commandLine);
            Assert.Contains("'--metrics-output'", error, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(error);
            Assert.Equal("OUT/Settlement-20261120-BRL.TXT", commandLine?.Run?.MetricsOutputPath);
        }
    }

    [Theory]
    [InlineData("out/metrics.json")]
    [InlineData("out/settlement-20261121-BRL.txt")] // the file of another day
    [InlineData("out/settlement-20261120-USD.txt")] // the file of another currency
    [InlineData("out/settlement-20261120-BRL.txt.json")]
    [InlineData("other/settlement-20261120-BRL.txt")]
    public void A_metrics_output_next_to_the_settlement_file_is_accepted(string metricsOutput)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(
            ["run", "--date", "2026-11-20", "--output", "out", "--metrics-output", metricsOutput], Yesterday, out string? error);

        Assert.Null(error);
        Assert.Equal(metricsOutput, commandLine?.Run?.MetricsOutputPath);
    }

    [Fact]
    public async Task A_metrics_output_that_is_the_settlement_file_exits_with_the_invalid_arguments_code()
    {
        int exitCode = await SettlementJobHost.RunAsync(
            ["run", "--date", "2026-11-20", "--output", "out", "--metrics-output", "out/settlement-20261120-BRL.txt"]);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
    }

    [Fact]
    public void A_run_without_strategy_or_page_size_uses_the_optimized_strategy_and_the_default_page_size()
    {
        RunOptions? options = JobCommandLine.Parse(["run"], Yesterday, out _)?.Run;

        Assert.Equal("optimized", options?.Strategy);
        Assert.Equal(10_000, options?.PageSize);
        Assert.Equal(RunOptions.DefaultPageSize, options?.PageSize);
    }

    [Theory]
    [InlineData("run", "--strategy", "naive", "--page-size", "250")]
    [InlineData("run", "--strategy=naive", "--page-size=250")]
    [InlineData("--strategy", "naive", "--page-size", "250")]
    [InlineData("--page-size=250", "--strategy=naive")]
    public void Strategy_and_page_size_are_read_in_both_forms_with_and_without_the_run_word(params string[] args)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(args, Yesterday, out string? error);

        Assert.Null(error);
        Assert.NotNull(commandLine);
        Assert.Equal(JobCommand.Run, commandLine.Command);
        Assert.Empty(commandLine.HostArgs);
        Assert.Equal(
            new RunOptions(Yesterday, "BRL", "settlement-files", Strategy: "naive", PageSize: 250),
            commandLine.Run);
    }

    [Theory]
    [InlineData("naive")]
    [InlineData("optimized")]
    public void Both_strategies_are_accepted(string strategy)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(["run", "--strategy", strategy], Yesterday, out string? error);

        Assert.Null(error);
        Assert.Equal(strategy, commandLine?.Run?.Strategy);
    }

    [Theory]
    [InlineData("run", "--strategy")]
    [InlineData("run", "--strategy", "fast")]
    [InlineData("run", "--strategy", "Naive")]
    [InlineData("run", "--strategy", " ")]
    [InlineData("--strategy=")]
    [InlineData("--strategy", "parallel")]
    [InlineData("run", "--page-size")]
    [InlineData("run", "--page-size", "0")]
    [InlineData("run", "--page-size", "-5")]
    [InlineData("run", "--page-size", "many")]
    [InlineData("run", "--page-size", "1.5")]
    [InlineData("run", "--page-size", "99999999999")]
    [InlineData("--page-size=0")]
    [InlineData("--page-size", " 10")]
    public void Invalid_strategy_or_page_size_is_rejected_with_an_error(params string[] args)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(args, Yesterday, out string? error);

        Assert.Null(commandLine);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("run", "--strategy", "fast")]
    [InlineData("--page-size", "0")]
    public async Task An_invalid_strategy_or_page_size_exits_with_the_invalid_arguments_code(params string[] args)
    {
        int exitCode = await SettlementJobHost.RunAsync(args);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
    }

    [Theory]
    [InlineData("run", "--parallelism", "8")]
    [InlineData("run", "--parallelism=8")]
    [InlineData("--parallelism", "8")]
    [InlineData("--parallelism=8")]
    public void Parallelism_is_read_in_both_forms_with_and_without_the_run_word(params string[] args)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(args, Yesterday, out string? error);

        Assert.Null(error);
        Assert.NotNull(commandLine);
        Assert.Equal(JobCommand.Run, commandLine.Command);
        Assert.Empty(commandLine.HostArgs);
        Assert.Equal(new RunOptions(Yesterday, "BRL", "settlement-files", Parallelism: 8), commandLine.Run);
    }

    [Fact]
    public void A_run_without_parallelism_uses_the_default_parallelism()
    {
        RunOptions? options = JobCommandLine.Parse(["run"], Yesterday, out _)?.Run;

        Assert.Equal(4, options?.Parallelism);
        Assert.Equal(RunOptions.DefaultParallelism, options?.Parallelism);
    }

    [Fact]
    public void A_parallelism_of_1_is_accepted()
    {
        RunOptions? options = JobCommandLine.Parse(["run", "--parallelism", "1"], Yesterday, out string? error)?.Run;

        Assert.Null(error);
        Assert.Equal(1, options?.Parallelism);
    }

    [Theory]
    [InlineData("run", "--parallelism")]
    [InlineData("run", "--parallelism", "0")]
    [InlineData("run", "--parallelism", "-2")]
    [InlineData("run", "--parallelism", "many")]
    [InlineData("run", "--parallelism", "2.5")]
    [InlineData("run", "--parallelism", "99999999999")]
    [InlineData("--parallelism=0")]
    [InlineData("--parallelism", " 4")]
    public void Invalid_parallelism_is_rejected_with_an_error(params string[] args)
    {
        JobCommandLine? commandLine = JobCommandLine.Parse(args, Yesterday, out string? error);

        Assert.Null(commandLine);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("run", "--parallelism", "0")]
    [InlineData("--parallelism", "x")]
    public async Task An_invalid_parallelism_exits_with_the_invalid_arguments_code(params string[] args)
    {
        int exitCode = await SettlementJobHost.RunAsync(args);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
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
