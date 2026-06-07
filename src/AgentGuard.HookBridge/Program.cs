using System.IO.Pipes;
using System.Text;
using AgentGuard.Core.Services;

var payload = await Console.In.ReadToEndAsync();
if (string.IsNullOrWhiteSpace(payload))
{
    return 0;
}

var timeoutMs = int.TryParse(Environment.GetEnvironmentVariable("AGENTGUARD_TIMEOUT_MS"), out var parsed)
    ? parsed
    : 21600 * 1000;

try
{
    await using var pipe = new NamedPipeClientStream(
        ".",
        HookInstaller.PipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous);

    using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    await pipe.ConnectAsync(connectCts.Token);

    await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
    using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

    await writer.WriteLineAsync(payload.Trim());
    using var responseCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
    var response = await reader.ReadLineAsync(responseCts.Token);
    if (!string.IsNullOrWhiteSpace(response))
    {
        Console.Out.WriteLine(response);
    }

    return 0;
}
catch (Exception ex)
{
    var logRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AgentGuard",
        "logs");
    Directory.CreateDirectory(logRoot);
    await File.AppendAllTextAsync(
        Path.Combine(logRoot, "bridge.log"),
        $"{DateTimeOffset.Now:O} AgentGuard bridge failed: {ex.Message}{Environment.NewLine}");
    return 1;
}
