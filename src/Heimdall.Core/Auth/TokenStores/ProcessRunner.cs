using System.Diagnostics;

namespace Heimdall.Core.Auth.TokenStores;

/// <summary>Runs a console tool (e.g. <c>secret-tool</c>, <c>security</c>) and captures its result.</summary>
internal static class ProcessRunner
{
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string fileName, IReadOnlyList<string> arguments, string? stdin = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        // Read both streams concurrently so a child filling its stderr buffer can't deadlock the read.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
