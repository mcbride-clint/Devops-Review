using System.Threading.Channels;
using PrLlmReview.Models;

namespace PrLlmReview.BackgroundServices;

/// <summary>
/// In-memory unbounded queue for review jobs backed by System.Threading.Channels.
/// </summary>
public sealed class ReviewQueue
{
    private readonly Channel<ReviewJob> _channel = Channel.CreateUnbounded<ReviewJob>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(ReviewJob job) =>
        _channel.Writer.TryWrite(job);

    public IAsyncEnumerable<ReviewJob> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
