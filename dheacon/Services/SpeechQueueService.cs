using System.Threading.Channels;
using Dalamud.Plugin.Services;

namespace Dheacon.Services;

public sealed class SpeechQueueService : IDisposable
{
    private readonly IPluginLog log;
    private readonly SpeechCacheService speechCacheService;
    private readonly AudioPlaybackService audioPlaybackService;
    private readonly Channel<CommentaryRequest> channel;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Task workerTask;
    private int pendingCount;

    public SpeechQueueService(
        IPluginLog log,
        SpeechCacheService speechCacheService,
        AudioPlaybackService audioPlaybackService)
    {
        this.log = log;
        this.speechCacheService = speechCacheService;
        this.audioPlaybackService = audioPlaybackService;
        channel = Channel.CreateUnbounded<CommentaryRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        workerTask = Task.Run(RunAsync);
    }

    public int PendingCount => Math.Max(0, Volatile.Read(ref pendingCount));
    public string LastStatus { get; private set; } = "Speech queue ready.";
    public string LastError { get; private set; } = string.Empty;
    public string LastText { get; private set; } = string.Empty;
    public DateTime LastSpokenAtUtc { get; private set; } = DateTime.MinValue;

    public bool TryEnqueue(CommentaryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return false;

        if (!channel.Writer.TryWrite(request))
            return false;

        Interlocked.Increment(ref pendingCount);
        LastStatus = $"Queued {request.Category}: {request.Reason}";
        return true;
    }

    public void Dispose()
    {
        channel.Writer.TryComplete();
        cancellationTokenSource.Cancel();

        try
        {
            workerTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException ex)
        {
            log.Warning(ex.Flatten(), "[Dheacon] Speech worker ended with an observed exception during dispose.");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[Dheacon] Speech worker dispose wait failed.");
        }

        cancellationTokenSource.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var request in channel.Reader.ReadAllAsync(cancellationTokenSource.Token).ConfigureAwait(false))
            {
                Interlocked.Decrement(ref pendingCount);
                ProcessRequest(request, cancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
            LastStatus = "Speech queue stopped.";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastStatus = "Speech queue stopped after an unexpected worker error.";
            log.Error(ex, "[Dheacon] Speech worker failed.");
        }
    }

    private void ProcessRequest(CommentaryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            LastText = request.Text;
            LastStatus = $"Preparing {request.Category}: {request.Reason}";
            var wavPath = speechCacheService.GetOrCreateWav(request.Text, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            audioPlaybackService.PlayWavFileSync(wavPath, $"Reading Roegadyn {request.Category}");

            LastSpokenAtUtc = DateTime.UtcNow;
            LastStatus = $"Spoke {request.Category}: {request.Reason}";
            LastError = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LastStatus = "Speech playback canceled.";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastStatus = $"Failed to speak {request.Category}.";
            log.Error(ex, $"[Dheacon] Failed to process speech request '{request.Category}'.");
        }
    }
}
