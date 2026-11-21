using SpriteLoom.Commands;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await SloomCommandLine.RunAsync(args, Console.Out, Console.Error, cancellation.Token);
