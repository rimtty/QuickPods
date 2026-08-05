using QuickPods.Spike.CoreAudio;

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await CoreAudioCli.RunAsync(
    args,
    Console.Out,
    Console.Error,
    cancellation.Token).ConfigureAwait(false);
