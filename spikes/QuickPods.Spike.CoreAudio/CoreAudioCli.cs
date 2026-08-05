using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace QuickPods.Spike.CoreAudio;

public static class CoreAudioCli
{
    private const int ExerciseWarmupIterations = 10;
    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(2);

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        CoreAudioOptions options;
        try
        {
            options = CoreAudioOptions.Parse(arguments);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            error.WriteLine($"Argument error: {exception.Message}");
            WriteHelp(error);
            return 2;
        }

        try
        {
            return options.Command switch
            {
                CoreAudioCommand.Help => WriteHelpAndReturn(output),
                CoreAudioCommand.Status => RunStatus(output, error),
                CoreAudioCommand.Watch => await RunWatchAsync(
                    options,
                    output,
                    error,
                    cancellationToken).ConfigureAwait(false),
                CoreAudioCommand.Pulse => await RunPulseAsync(
                    options,
                    output,
                    error,
                    cancellationToken).ConfigureAwait(false),
                CoreAudioCommand.Exercise => await RunExerciseAsync(
                    options,
                    output,
                    error,
                    cancellationToken).ConfigureAwait(false),
                CoreAudioCommand.KeyLatency => await RunKeyLatencyAsync(
                    options,
                    output,
                    error,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new UnreachableException(),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine("Operation cancelled after the guarded restoration path completed.");
            return 130;
        }
        catch (CoreAudio.Interop.CoreAudioInteropException exception)
        {
            error.WriteLine($"Core Audio failure: HRESULT 0x{exception.NativeHResult:X8}. {exception.Message}");
            return 1;
        }
        catch (COMException exception)
        {
            error.WriteLine($"Core Audio failure: HRESULT 0x{exception.HResult:X8}. {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            error.WriteLine($"Core Audio diagnostic failed: {exception.Message}");
            return 1;
        }
    }

    private static int RunStatus(TextWriter output, TextWriter error)
    {
        using var client = CreateClient(error);
        WriteSnapshot(output, client.GetSnapshot());
        return 0;
    }

    private static async Task<int> RunWatchAsync(
        CoreAudioOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        TextWriter synchronizedOutput = TextWriter.Synchronized(output);
        using var client = new CoreAudioClient(
            notification => synchronizedOutput.WriteLine(
                FormattableString.Invariant(
                    $"volume generation={notification.Generation} percent={notification.VolumePercent:F1} muted={notification.IsMuted} source={(notification.IsSelfOriginated(CoreAudioClient.EventContext) ? "self" : "external")}")),
            change => synchronizedOutput.WriteLine(
                FormattableString.Invariant(
                    $"default-endpoint generation={change.Generation} endpoint_hash={change.EndpointIdHash} role={change.Role}")),
            exception => error.WriteLine($"Background Core Audio fault: {exception.Message}"));

        WriteSnapshot(output, client.GetSnapshot());
        output.WriteLine($"Watching for {options.Seconds} seconds. Press Ctrl+C to stop.");
        await Task.Delay(TimeSpan.FromSeconds(options.Seconds), cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunPulseAsync(
        CoreAudioOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!options.PlaybackStoppedConfirmed)
        {
            error.WriteLine("Safety check failed: stop audio playback, then pass --confirm-playback-stopped.");
            return 2;
        }

        var notifications = new VolumeNotificationBuffer();
        using var client = CreateClient(error, notifications.Publish);
        using CoreAudioMutationScope mutation = client.BeginMutationScope();
        AudioSnapshot original = mutation.OriginalSnapshot;
        double targetPercent = options.Percent ?? throw new InvalidOperationException("Pulse target is missing.");
        if (Math.Abs(original.VolumePercent - targetPercent) < 0.5d)
        {
            error.WriteLine("Choose a pulse target at least 0.5 percentage points from the current volume.");
            return 2;
        }

        output.WriteLine(
            FormattableString.Invariant(
                $"Pulsing from {original.VolumePercent:F1}% to {targetPercent:F1}% for {options.HoldMilliseconds} ms; restoration is mandatory."));

        bool restored = false;
        Exception? operationFailure = null;
        try
        {
            _ = mutation.MuteForSafety();
            Observation observation = await SetAndObserveAsync(
                mutation,
                notifications,
                targetPercent,
                original.Generation,
                cancellationToken).ConfigureAwait(false);
            output.WriteLine(
                FormattableString.Invariant(
                    $"set_ms={observation.SetMilliseconds:F3} notify_ms={observation.NotificationMilliseconds:F3}"));
            await Task.Delay(options.HoldMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            operationFailure = exception;
        }

        restored = RestoreOriginal(mutation, error);
        CompleteGuardedMutation(operationFailure, restored);
        output.WriteLine($"restored={restored.ToString().ToLowerInvariant()}");
        return restored ? 0 : 1;
    }

    private static async Task<int> RunExerciseAsync(
        CoreAudioOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!options.PlaybackStoppedConfirmed)
        {
            error.WriteLine("Safety check failed: stop audio playback, then pass --confirm-playback-stopped.");
            return 2;
        }

        var notifications = new VolumeNotificationBuffer();
        using var client = CreateClient(error, notifications.Publish);
        using CoreAudioMutationScope mutation = client.BeginMutationScope();
        AudioSnapshot original = mutation.OriginalSnapshot;
        double alternate = SelectBoundedAlternate(original.VolumePercent, options.DeltaPercent);
        output.WriteLine(
            FormattableString.Invariant(
                $"Exercise iterations={options.Iterations} range={Math.Min(original.VolumePercent, alternate):F1}..{Math.Max(original.VolumePercent, alternate):F1}% endpoint_hash={original.EndpointIdHash}"));

        var setMilliseconds = new List<double>(options.Iterations);
        var notificationMilliseconds = new List<double>(options.Iterations);
        var measurements = new List<ExerciseMeasurement>(options.Iterations);
        var resources = new List<ResourceSample>();
        int sampleInterval = Math.Max(1, options.Iterations / 10);
        bool restored = false;
        Exception? operationFailure = null;

        try
        {
            _ = mutation.MuteForSafety();
            for (int iteration = 1; iteration <= ExerciseWarmupIterations; iteration++)
            {
                double target = iteration % 2 == 1 ? alternate : original.VolumePercent;
                _ = await SetAndObserveAsync(
                    mutation,
                    notifications,
                    target,
                    original.Generation,
                    cancellationToken).ConfigureAwait(false);
            }

            output.WriteLine($"warmup={ExerciseWarmupIterations}");
            resources.Add(ResourceSample.CaptureLiveResources(0));
            for (int iteration = 1; iteration <= options.Iterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double target = iteration % 2 == 1 ? alternate : original.VolumePercent;
                Observation observation = await SetAndObserveAsync(
                    mutation,
                    notifications,
                    target,
                    original.Generation,
                    cancellationToken).ConfigureAwait(false);
                setMilliseconds.Add(observation.SetMilliseconds);
                notificationMilliseconds.Add(observation.NotificationMilliseconds);
                measurements.Add(new ExerciseMeasurement(
                    iteration,
                    target,
                    observation.ObservedPercent,
                    observation.SetMilliseconds,
                    observation.NotificationMilliseconds,
                    observation.Generation,
                    original.EndpointIdHash,
                    Math.Max(0, observation.Generation - original.Generation)));

                if (iteration % sampleInterval == 0 || iteration == options.Iterations)
                {
                    resources.Add(ResourceSample.CaptureLiveResources(iteration));
                    output.WriteLine($"progress={iteration}/{options.Iterations}");
                }
            }
        }
        catch (Exception exception)
        {
            operationFailure = exception;
        }

        restored = RestoreOriginal(mutation, error);
        CompleteGuardedMutation(operationFailure, restored);
        double setP95 = Statistics.Percentile(setMilliseconds, 0.95d);
        double notificationP95 = Statistics.Percentile(notificationMilliseconds, 0.95d);
        ResourceSample firstResource = resources[0];
        ResourceSample lastResource = resources[^1];
        long privateBytesDelta = lastResource.PrivateBytes - firstResource.PrivateBytes;
        long handleDelta = lastResource.HandleCount - firstResource.HandleCount;
        long threadDelta = lastResource.ThreadCount - firstResource.ThreadCount;
        bool privateBytesGrowth = Statistics.HasSustainedGrowth(
            resources.Select(sample => sample.PrivateBytes));
        bool handleGrowth = Statistics.HasSustainedGrowth(
            resources.Select(sample => sample.HandleCount));
        bool threadGrowth = Statistics.HasSustainedGrowth(
            resources.Select(sample => sample.ThreadCount));
        AudioSnapshot finalSnapshot = client.GetSnapshot();
        long rebindCount = Math.Max(0, finalSnapshot.Generation - original.Generation);
        bool passed = restored &&
            setP95 <= 100d &&
            notificationP95 <= 250d &&
            !privateBytesGrowth &&
            !handleGrowth &&
            !threadGrowth &&
            rebindCount == 0;

        output.WriteLine(FormattableString.Invariant($"set_p95_ms={setP95:F3}"));
        output.WriteLine(FormattableString.Invariant($"notify_p95_ms={notificationP95:F3}"));
        output.WriteLine($"private_bytes_sustained_growth={privateBytesGrowth.ToString().ToLowerInvariant()}");
        output.WriteLine($"handles_sustained_growth={handleGrowth.ToString().ToLowerInvariant()}");
        output.WriteLine($"threads_sustained_growth={threadGrowth.ToString().ToLowerInvariant()}");
        output.WriteLine($"private_bytes_delta={privateBytesDelta}");
        output.WriteLine($"handle_delta={handleDelta}");
        output.WriteLine($"thread_delta={threadDelta}");
        output.WriteLine($"rebind_count={rebindCount}");
        output.WriteLine($"restored={restored.ToString().ToLowerInvariant()}");
        output.WriteLine($"gate_metrics_passed={passed.ToString().ToLowerInvariant()}");

        if (options.CsvPath is not null)
        {
            await WriteMetricsCsvAsync(
                options.CsvPath,
                measurements,
                resources,
                finalSnapshot,
                original.Generation,
                cancellationToken).ConfigureAwait(false);
            output.WriteLine($"resource_csv={Path.GetFullPath(options.CsvPath)}");
        }

        return passed ? 0 : 1;
    }

    private static async Task<int> RunKeyLatencyAsync(
        CoreAudioOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!options.PlaybackStoppedConfirmed)
        {
            error.WriteLine("Safety check failed: stop audio playback, then pass --confirm-playback-stopped.");
            return 2;
        }

        VolumeNotificationBuffer notifications = new();
        using var client = CreateClient(error, notifications.Publish);
        using CoreAudioMutationScope mutation = client.BeginMutationScope();
        AudioSnapshot original = mutation.OriginalSnapshot;
        SystemVolumeKeySender sender = new();
        List<double> notificationMilliseconds = new(options.Iterations);
        int progressInterval = Math.Max(1, options.Iterations / 10);
        bool restored = false;
        Exception? operationFailure = null;

        output.WriteLine(
            FormattableString.Invariant(
                $"Key latency iterations={options.Iterations} original={original.VolumePercent:F1}% endpoint_hash={original.EndpointIdHash}"));

        try
        {
            _ = mutation.MuteForSafety();
            for (int iteration = 1; iteration <= options.Iterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                notifications.Drain();
                long started = Stopwatch.GetTimestamp();
                sender.SendStep(increase: iteration % 2 == 0);
                VolumeNotification notification = await notifications.WaitForAsync(
                    candidate =>
                        candidate.Generation == original.Generation &&
                        candidate.CallbackTimestamp >= started &&
                        !candidate.IsSelfOriginated(CoreAudioClient.EventContext),
                    NotificationTimeout,
                    cancellationToken).ConfigureAwait(false);
                notificationMilliseconds.Add(
                    Stopwatch.GetElapsedTime(started, notification.CallbackTimestamp).TotalMilliseconds);

                if (iteration % progressInterval == 0 || iteration == options.Iterations)
                {
                    output.WriteLine($"progress={iteration}/{options.Iterations}");
                }
            }
        }
        catch (Exception exception)
        {
            operationFailure = exception;
        }

        restored = RestoreOriginal(mutation, error);
        CompleteGuardedMutation(operationFailure, restored);
        double notificationP95 = Statistics.Percentile(notificationMilliseconds, 0.95d);
        bool passed = restored && notificationP95 <= 250d;
        output.WriteLine(FormattableString.Invariant($"external_notify_p95_ms={notificationP95:F3}"));
        output.WriteLine($"restored={restored.ToString().ToLowerInvariant()}");
        output.WriteLine($"external_latency_gate_passed={passed.ToString().ToLowerInvariant()}");
        return passed ? 0 : 1;
    }

    private static CoreAudioClient CreateClient(
        TextWriter error,
        Action<VolumeNotification>? notificationSink = null)
    {
        return new CoreAudioClient(
            notificationSink,
            defaultEndpointChangeSink: null,
            exception => error.WriteLine($"Background Core Audio fault: {exception.Message}"));
    }

    private static async Task<Observation> SetAndObserveAsync(
        CoreAudioMutationScope mutation,
        VolumeNotificationBuffer notifications,
        double targetPercent,
        long generation,
        CancellationToken cancellationToken)
    {
        notifications.Drain();
        long started = Stopwatch.GetTimestamp();
        NativeCallTiming timing = mutation.SetVolumePercent(targetPercent);
        VolumeNotification notification = await notifications.WaitForAsync(
            candidate =>
                candidate.Generation == generation &&
                candidate.CallbackTimestamp >= started &&
                candidate.IsSelfOriginated(CoreAudioClient.EventContext) &&
                Math.Abs(candidate.VolumePercent - targetPercent) <= 1d,
            NotificationTimeout,
            cancellationToken).ConfigureAwait(false);

        return new Observation(
            timing.Duration.TotalMilliseconds,
            Stopwatch.GetElapsedTime(started, notification.CallbackTimestamp).TotalMilliseconds,
            notification.VolumePercent,
            notification.Generation);
    }

    private static bool RestoreOriginal(
        CoreAudioMutationScope mutation,
        TextWriter error)
    {
        try
        {
            bool restored = mutation.Restore();
            if (!restored)
            {
                error.WriteLine("Mandatory restoration completed without a verifiable match on the original endpoint.");
            }

            return restored;
        }
        catch (Exception exception)
        {
            error.WriteLine($"Mandatory restoration failed: {exception.Message}");
            return false;
        }
    }

    internal static void CompleteGuardedMutation(Exception? operationFailure, bool restored)
    {
        if (!restored)
        {
            var restorationFailure = new AudioRestorationException();
            if (operationFailure is not null)
            {
                throw new AggregateException(
                    "The Core Audio operation failed and the original endpoint state could not be verified after restoration.",
                    operationFailure,
                    restorationFailure);
            }

            throw restorationFailure;
        }

        if (operationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }
    }

    private static double SelectBoundedAlternate(double originalPercent, double deltaPercent)
    {
        return originalPercent <= 50d
            ? Math.Min(100d, originalPercent + deltaPercent)
            : Math.Max(0d, originalPercent - deltaPercent);
    }

    private static async Task WriteMetricsCsvAsync(
        string path,
        List<ExerciseMeasurement> measurements,
        List<ResourceSample> samples,
        AudioSnapshot finalSnapshot,
        long originalGeneration,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        ResourceSample firstSample = samples[0];
        ResourceSample lastSample = samples[^1];
        long rebindCount = Math.Max(0, finalSnapshot.Generation - originalGeneration);
        var lines = new List<string>(measurements.Count + samples.Count + 2)
        {
            "kind,iteration,target_percent,observed_percent,set_ms,notification_ms,generation,endpoint_hash,timestamp,private_bytes,handle_count,thread_count,private_bytes_delta,handle_delta,thread_delta,rebind_count",
        };
        lines.AddRange(measurements.Select(measurement => string.Create(
            CultureInfo.InvariantCulture,
            $"operation,{measurement.Iteration},{measurement.TargetPercent:F3},{measurement.ObservedPercent:F3},{measurement.SetMilliseconds:F3},{measurement.NotificationMilliseconds:F3},{measurement.Generation},{measurement.EndpointIdHash},,,,,,,,{measurement.RebindCount}")));
        lines.AddRange(samples.Select(sample => string.Create(
            CultureInfo.InvariantCulture,
            $"resource,{sample.Iteration},,,,,,,{sample.Timestamp},{sample.PrivateBytes},{sample.HandleCount},{sample.ThreadCount},{sample.PrivateBytes - firstSample.PrivateBytes},{sample.HandleCount - firstSample.HandleCount},{sample.ThreadCount - firstSample.ThreadCount},")));
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"summary,{measurements.Count},,,,,{finalSnapshot.Generation},{finalSnapshot.EndpointIdHash},{lastSample.Timestamp},{lastSample.PrivateBytes},{lastSample.HandleCount},{lastSample.ThreadCount},{lastSample.PrivateBytes - firstSample.PrivateBytes},{lastSample.HandleCount - firstSample.HandleCount},{lastSample.ThreadCount - firstSample.ThreadCount},{rebindCount}"));
        await File.WriteAllLinesAsync(fullPath, lines, cancellationToken).ConfigureAwait(false);
    }

    private static void WriteSnapshot(TextWriter output, AudioSnapshot snapshot)
    {
        output.WriteLine($"available={snapshot.IsAvailable.ToString().ToLowerInvariant()}");
        output.WriteLine($"endpoint_hash={snapshot.EndpointIdHash}");
        output.WriteLine($"role={snapshot.Role}");
        output.WriteLine($"generation={snapshot.Generation}");
        output.WriteLine($"device_state=0x{snapshot.DeviceState:X8}");
        if (snapshot.IsAvailable)
        {
            output.WriteLine(FormattableString.Invariant($"volume_percent={snapshot.VolumePercent:F1}"));
            output.WriteLine($"muted={snapshot.IsMuted.ToString().ToLowerInvariant()}");
        }
        output.WriteLine($"process_architecture={RuntimeInformation.ProcessArchitecture}");
        output.WriteLine($"os={Environment.OSVersion.VersionString}");
    }

    private static int WriteHelpAndReturn(TextWriter output)
    {
        WriteHelp(output);
        return 0;
    }

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("QuickPods Core Audio feasibility spike");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  status");
        output.WriteLine("      Read the default Console render endpoint without changing it.");
        output.WriteLine("  watch [--seconds 30]");
        output.WriteLine("      Observe volume, mute, and default-endpoint notifications.");
        output.WriteLine("  pulse --percent N [--hold-ms 1000] --confirm-playback-stopped");
        output.WriteLine("      Temporarily set volume, observe the callback, and restore the original state.");
        output.WriteLine("  exercise [--iterations 1000] [--delta-percent 1] [--csv PATH] --confirm-playback-stopped");
        output.WriteLine("      Run bounded alternating changes, record latency/resources, and always restore state.");
        output.WriteLine("  key-latency [--iterations 100] --confirm-playback-stopped");
        output.WriteLine("      Send bounded system volume keys, measure external callback latency, and restore state.");
        output.WriteLine();
        output.WriteLine("Output uses a short SHA-256 endpoint hash; full endpoint identifiers are never printed.");
    }

    private readonly record struct Observation(
        double SetMilliseconds,
        double NotificationMilliseconds,
        double ObservedPercent,
        long Generation);

    private readonly record struct ExerciseMeasurement(
        int Iteration,
        double TargetPercent,
        double ObservedPercent,
        double SetMilliseconds,
        double NotificationMilliseconds,
        long Generation,
        string EndpointIdHash,
        long RebindCount);
}
