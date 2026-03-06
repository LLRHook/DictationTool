using System.Diagnostics;
using DictationTool;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using NAudio.Wave;

const string TestText = "Hello world. This is a test of the text to speech pipeline.";
int failures = 0;

void Pass(string msg) => Console.WriteLine($"  PASS: {msg}");
void Fail(string msg) { Console.WriteLine($"  FAIL: {msg}"); failures++; }

Console.WriteLine("=== DictationTool TTS Pipeline Test ===\n");

// 1. Model load
Console.WriteLine("[1] Loading model...");
KokoroTTS? tts = null;
try
{
    tts = KokoroTTS.LoadModel();
    if (tts != null) Pass("Model loaded");
    else { Fail("Model is null"); return 1; }
}
catch (Exception ex)
{
    Fail(ex.Message);
    return 1;
}

// 2. Voice load
Console.WriteLine("\n[2] Loading voice af_heart...");
KokoroVoice? voice = null;
try
{
    voice = KokoroVoiceManager.GetVoice("af_heart");
    if (voice != null) Pass("Voice loaded");
    else { Fail("Voice is null"); return 1; }
}
catch (Exception ex)
{
    Fail(ex.Message);
    return 1;
}

// 3. SpeakFast sanity check
Console.WriteLine("\n[3] SpeakFast sanity check (\"Hello world\")...");
try
{
    tts.SpeakFast("Hello world", voice);
    Pass("SpeakFast completed without error");
}
catch (Exception ex)
{
    Fail(ex.Message);
}

// 4. Tokenizer
Console.WriteLine("\n[4] Tokenizer...");
try
{
    var tokens = Tokenizer.Tokenize(TestText, voice.GetLangCode(), true);
    var tokenCount = tokens.Count();
    Console.WriteLine($"  Token count: {tokenCount}");
    if (tokenCount > 0) Pass("Tokens produced");
    else Fail("No tokens produced");
}
catch (Exception ex)
{
    Fail(ex.Message);
}

// 5. Segmentation
Console.WriteLine("\n[5] Segmentation...");
try
{
    var tokens = Tokenizer.Tokenize(TestText, voice.GetLangCode(), true);
    var segs = SegmentationSystem.SplitToSegments(tokens, new DefaultSegmentationConfig());
    Console.WriteLine($"  Segment type: {segs.GetType().FullName}");
    Console.WriteLine($"  Segment count: {segs.Count}");
    if (segs.Count > 0) Pass("Segments produced");
    else Fail("No segments produced");
}
catch (Exception ex)
{
    Fail(ex.Message);
}

// 6. Manual synthesis (replicating SynthesizeAll logic)
Console.WriteLine("\n[6] Manual synthesis (SynthesizeAll logic)...");
float[]? synthSamples = null;
try
{
    var tokens = Tokenizer.Tokenize(TestText, voice.GetLangCode(), true);
    var segs = SegmentationSystem.SplitToSegments(tokens, new DefaultSegmentationConfig());
    var allSamples = new List<float>();

    int expected = segs.Count;
    int completed = 0;
    Console.WriteLine($"  Expected callbacks: {expected}");

    using var done = new ManualResetEventSlim(false);

    var job = KokoroJob.Create(segs, voice, 1.0f, (float[] samples) =>
    {
        var c = Interlocked.Increment(ref completed);
        Console.WriteLine($"  Callback fired: segment {c}/{expected} ({samples.Length} samples)");
        var processed = KokoroPlayback.PostProcessSamples(samples);
        lock (allSamples) { allSamples.AddRange(processed); }
        if (c >= expected)
        {
            Console.WriteLine("  All callbacks received, signaling done");
            done.Set();
        }
    });

    tts.EnqueueJob(job);

    Console.WriteLine("  Waiting for completion (10s timeout)...");
    var sw = Stopwatch.StartNew();
    bool finished = done.Wait(TimeSpan.FromSeconds(10));
    sw.Stop();

    if (finished)
    {
        lock (allSamples) { synthSamples = [.. allSamples]; }
        Pass($"Synthesis completed in {sw.ElapsedMilliseconds}ms, {synthSamples.Length} total samples");
    }
    else
    {
        Fail($"Timed out after {sw.ElapsedMilliseconds}ms. completed={completed}/{expected}");
    }
}
catch (Exception ex)
{
    Fail($"{ex.Message}\n  {ex.StackTrace}");
}

// 7. WAV export
Console.WriteLine("\n[7] WAV export...");
if (synthSamples != null && synthSamples.Length > 0)
{
    try
    {
        var wavPath = Path.Combine(AppContext.BaseDirectory, "test_output.wav");
        using (var writer = new WaveFileWriter(wavPath, KokoroPlayback.waveFormat))
        {
            writer.WriteSamples(synthSamples, 0, synthSamples.Length);
        }
        Pass($"WAV written to {wavPath}");
    }
    catch (Exception ex)
    {
        Fail(ex.Message);
    }
}
else
{
    Console.WriteLine("  SKIP: No samples to export (synthesis failed)");
}

// 8. Streaming synthesis with BufferedWaveProvider (10+ segments)
Console.WriteLine("\n[8] Streaming synthesis with BufferedWaveProvider...");
try
{
    var longText = string.Join(" ", Enumerable.Range(1, 12).Select(i =>
        $"Sentence number {i} is a complete thought that should become its own segment."));
    var normalized = TextNormalizer.Normalize(longText);
    var tokens = Tokenizer.Tokenize(normalized, voice.GetLangCode(), true);
    var segs = SegmentationSystem.SplitToSegments(tokens, new DefaultSegmentationConfig());
    Console.WriteLine($"  Segment count: {segs.Count}");

    var provider = new BufferedWaveProvider(KokoroPlayback.waveFormat)
    {
        ReadFully = true,
        BufferDuration = TimeSpan.FromSeconds(30),
    };

    // Use a WaveOutEvent to drain the buffer (simulates real playback)
    using var drainPlayer = new WaveOutEvent();
    drainPlayer.Init(provider);

    int streamCompleted = 0;
    int streamExpected = segs.Count;
    int firstFired = 0;
    bool streamFirstCallbackOk = false;
    var streamSynthDone = new ManualResetEventSlim(false);
    using var cts = new CancellationTokenSource();
    var ct = cts.Token;

    var streamJob = KokoroJob.Create(segs, voice, 1.0f, (float[] samples) =>
    {
        var c = Interlocked.Increment(ref streamCompleted);
        Console.WriteLine($"  Stream callback {c}/{streamExpected} ({samples.Length} samples)");

        var processed = KokoroPlayback.PostProcessSamples(samples);
        var bytes = KokoroPlayback.GetBytes(processed);

        // Backpressure: wait for buffer space
        while (provider.BufferLength - provider.BufferedBytes < bytes.Length)
        {
            if (ct.IsCancellationRequested) return;
            Thread.Sleep(50);
        }

        provider.AddSamples(bytes, 0, bytes.Length);

        if (Interlocked.Exchange(ref firstFired, 1) == 0)
        {
            streamFirstCallbackOk = true;
            drainPlayer.Play();
            Console.WriteLine("  First segment ready — playback started to drain buffer");
        }

        if (c >= streamExpected)
            streamSynthDone.Set();
    });

    tts.EnqueueJob(streamJob);

    var sw2 = Stopwatch.StartNew();
    bool streamFinished = streamSynthDone.Wait(TimeSpan.FromSeconds(60));
    sw2.Stop();

    if (!streamFinished)
    {
        Fail($"Streaming timed out after {sw2.ElapsedMilliseconds}ms. completed={streamCompleted}/{streamExpected}");
    }
    else if (!streamFirstCallbackOk)
    {
        Fail("First-segment callback never fired");
    }
    else
    {
        Console.WriteLine($"  Total buffered bytes: {provider.BufferedBytes}");
        Pass($"Streaming completed in {sw2.ElapsedMilliseconds}ms, {streamCompleted} segments, buffer={provider.BufferedBytes} bytes");
    }
}
catch (Exception ex)
{
    Fail($"{ex.Message}\n  {ex.StackTrace}");
}

// 9. TextNormalizer
Console.WriteLine("\n[9] TextNormalizer...");
try
{
    var normalized = TextNormalizer.Normalize(TestText);
    Console.WriteLine($"  Input:  \"{TestText}\"");
    Console.WriteLine($"  Output: \"{normalized}\"");
    if (!string.IsNullOrWhiteSpace(normalized)) Pass("Normalized successfully");
    else Fail("Normalized to empty string");
}
catch (Exception ex)
{
    Fail(ex.Message);
}

Console.WriteLine($"\n=== Tests complete: {(failures == 0 ? "ALL PASSED" : $"{failures} FAILED")} ===");
tts?.Dispose();
return failures > 0 ? 1 : 0;
