using Positron.Ipc;

namespace Positron.Tests;

/// <summary>
/// In-memory <see cref="IMessageTransport"/> for exercising <see cref="IpcRouter"/> without a
/// WebView. Captures everything the router posts outbound and lets a test inject inbound frames.
/// Because the router's <c>Dispatch</c> is <c>async void</c>, outbound delivery is signalled with a
/// <see cref="TaskCompletionSource"/> so tests can await the next frame instead of polling.
/// </summary>
internal sealed class FakeTransport : IMessageTransport
{
    private readonly Lock _gate = new();
    private TaskCompletionSource<string> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public event Action<string>? MessageReceived;

    /// <summary>Every raw JSON string the router has posted outbound, in order.</summary>
    public List<string> Sent { get; } = new();

    /// <inheritdoc />
    public void PostMessage(string json)
    {
        TaskCompletionSource<string> toSignal;
        lock (_gate)
        {
            Sent.Add(json);
            toSignal = _next;
            _next = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        toSignal.TrySetResult(json);
    }

    /// <summary>Simulates the web content posting a raw JSON frame inbound.</summary>
    public void Receive(string json) => MessageReceived?.Invoke(json);

    /// <summary>
    /// Awaits the next outbound frame (or returns one already sent since the last wait), failing
    /// after <paramref name="timeoutMs"/>. Non-flaky replacement for sleep-polling.
    /// </summary>
    public async Task<string> WaitForNextAsync(int timeoutMs = 2000)
    {
        Task<string> next;
        lock (_gate)
        {
            if (Sent.Count > 0)
            {
                return Sent[^1];
            }

            next = _next.Task;
        }

        Task winner = await Task.WhenAny(next, Task.Delay(timeoutMs)).ConfigureAwait(false);
        if (winner != next)
        {
            throw new TimeoutException("No outbound frame was posted within the timeout.");
        }

        return await next.ConfigureAwait(false);
    }

    /// <summary>Waits a short grace period and asserts that nothing was posted outbound.</summary>
    public async Task AssertNoneSentAsync(int graceMs = 100)
    {
        await Task.Delay(graceMs).ConfigureAwait(false);
        lock (_gate)
        {
            Assert.Empty(Sent);
        }
    }
}