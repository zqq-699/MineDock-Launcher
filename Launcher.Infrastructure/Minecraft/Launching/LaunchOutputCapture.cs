/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record LaunchCapturedOutput(
    string StdOut,
    string StdErr,
    string? FilePath);

internal sealed class LaunchOutputCapture
{
    private const int TailLineCount = 120;

    private readonly string outputPath;
    private readonly IReadOnlyList<string> sensitiveValues;
    private readonly Channel<CapturedLine> channel = Channel.CreateBounded<CapturedLine>(
        new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    private readonly object tailLock = new();
    private readonly Queue<string> stdoutTail = new();
    private readonly Queue<string> stderrTail = new();
    private Task? completionTask;

    public LaunchOutputCapture(string outputPath, IReadOnlyList<string> sensitiveValues)
    {
        this.outputPath = outputPath;
        this.sensitiveValues = sensitiveValues;
    }

    public void Start(Process process)
    {
        if (completionTask is not null)
            return;

        if (!process.StartInfo.RedirectStandardOutput && !process.StartInfo.RedirectStandardError)
        {
            completionTask = Task.CompletedTask;
            return;
        }

        completionTask = CaptureAsync(process);
    }

    public async Task<LaunchCapturedOutput> CompleteAsync()
    {
        if (completionTask is not null)
            await completionTask.ConfigureAwait(false);

        string stdout;
        string stderr;
        lock (tailLock)
        {
            stdout = string.Join(Environment.NewLine, stdoutTail);
            stderr = string.Join(Environment.NewLine, stderrTail);
        }

        var filePath = TryGetNonEmptyOutputPath();
        return new LaunchCapturedOutput(stdout, stderr, filePath);
    }

    private async Task CaptureAsync(Process process)
    {
        var writerTask = WriteOutputAsync();
        var pumps = new List<Task>(2);
        if (process.StartInfo.RedirectStandardOutput)
            pumps.Add(PumpAsync(process.StandardOutput, "stdout", stdoutTail));
        if (process.StartInfo.RedirectStandardError)
            pumps.Add(PumpAsync(process.StandardError, "stderr", stderrTail));

        try
        {
            await Task.WhenAll(pumps).ConfigureAwait(false);
        }
        finally
        {
            channel.Writer.TryComplete();
        }

        await writerTask.ConfigureAwait(false);
    }

    private async Task PumpAsync(StreamReader reader, string source, Queue<string> tail)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var redacted = LaunchDiagnosticRedactor.Redact(line, sensitiveValues);
                lock (tailLock)
                {
                    tail.Enqueue(redacted);
                    while (tail.Count > TailLineCount)
                        tail.Dequeue();
                }

                await channel.Writer.WriteAsync(new CapturedLine(source, redacted)).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    private async Task WriteOutputAsync()
    {
        StreamWriter? writer = null;
        var writeFailed = false;
        try
        {
            await foreach (var line in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (writeFailed)
                    continue;

                try
                {
                    if (writer is null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                        writer = new StreamWriter(
                            new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read),
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    }

                    await writer.WriteLineAsync($"[{line.Source}] {line.Text}").ConfigureAwait(false);
                }
                catch (IOException)
                {
                    writeFailed = true;
                }
                catch (UnauthorizedAccessException)
                {
                    writeFailed = true;
                }
            }
        }
        finally
        {
            if (writer is not null)
                await writer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private string? TryGetNonEmptyOutputPath()
    {
        try
        {
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0
                ? outputPath
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record CapturedLine(string Source, string Text);
}
