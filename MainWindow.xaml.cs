using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using NAudio.Wave;

namespace DictationTool;

public partial class MainWindow : Window
{
    private KokoroTTS? _tts;
    private KokoroVoice? _selectedVoice;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _bufferedProvider;
    private bool _isPaused;
    private CancellationTokenSource? _cts;
    private volatile KokoroJob? _currentJob;
    private volatile bool _synthesisComplete;

    public MainWindow()
    {
        InitializeComponent();
        LoadModelAsync();
    }

    private async void LoadModelAsync()
    {
        PlayButton.IsEnabled = false;
        PlayButton.Content = "Loading model...";

        try
        {
            var voices = await Task.Run(() =>
            {
                var tts = KokoroTTS.LoadModel();
                _tts = tts;
                return KokoroVoiceManager.GetVoices(KokoroLanguage.AmericanEnglish)
                    .Concat(KokoroVoiceManager.GetVoices(KokoroLanguage.BritishEnglish))
                    .DistinctBy(v => v.Name)
                    .OrderByDescending(v => v.Name == "af_heart")
                    .ThenBy(v => v.Name)
                    .ToList();
            });

            foreach (var v in voices)
                VoiceCombo.Items.Add(new ComboBoxItem { Content = FormatVoiceName(v.Name), Tag = v.Name });

            if (VoiceCombo.Items.Count > 0)
                VoiceCombo.SelectedIndex = 0;

            PlayButton.Content = "Play";
            PlayButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            PlayButton.Content = "Load failed";
            MessageBox.Show($"Failed to load TTS model: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FormatVoiceName(string name)
    {
        var parts = name.Split('_', 2);
        if (parts.Length != 2) return name;
        var region = parts[0][0] == 'b' ? "UK" : "US";
        var gender = parts[0].Length > 1 && parts[0][1] == 'f' ? "Female" : "Male";
        var displayName = char.ToUpper(parts[1][0]) + parts[1][1..];
        return $"{displayName} ({gender}, {region})";
    }

    private void VoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VoiceCombo.SelectedItem is ComboBoxItem { Tag: string voiceName })
        {
            try { _selectedVoice = KokoroVoiceManager.GetVoice(voiceName); }
            catch { /* voice not found */ }
        }
    }

    private void RateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RateLabel is not null)
            RateLabel.Text = $"{e.NewValue:F1}x";
    }

    private void Resume()
    {
        _waveOut?.Play();
        _isPaused = false;
        PauseButton.Content = "Pause";
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tts == null || _selectedVoice == null) return;

        var text = InputBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        if (_isPaused)
        {
            Resume();
            return;
        }

        InputBox.IsReadOnly = true;
        InputBox.Background = System.Windows.Media.Brushes.White;
        PlayButton.IsEnabled = false;
        PlayButton.Content = "Loading...";
        StopButton.IsEnabled = true;

        var voice = _selectedVoice;
        var speed = (float)RateSlider.Value;
        var normalized = TextNormalizer.Normalize(text);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var provider = new BufferedWaveProvider(KokoroPlayback.waveFormat)
        {
            ReadFully = true,
            BufferDuration = TimeSpan.FromMinutes(10),
        };
        _bufferedProvider = provider;

        _waveOut?.Dispose();
        var player = new WaveOutEvent();
        _waveOut = player;
        player.Init(provider);

        var ct = _cts.Token;

        _ = Task.Run(() =>
        {
            try
            {
                StreamingSynthesize(normalized, voice, speed, provider, onFirstSegment: () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_waveOut != player) return;
                        PlayButton.Content = "Play";
                        PauseButton.IsEnabled = true;
                        player.Play();
                    });
                }, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Synthesis failed: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetState();
                });
            }
        }, ct);

        await WaitForPlaybackComplete(provider, ct);
    }

    private async Task WaitForPlaybackComplete(BufferedWaveProvider provider, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_synthesisComplete && provider.BufferedBytes == 0)
                    break;
                await Task.Delay(50, ct);
            }
        }
        catch (OperationCanceledException) { }

        if (!ct.IsCancellationRequested)
            ResetState();
    }

    private void StreamingSynthesize(string text, KokoroVoice voice, float speed,
        BufferedWaveProvider provider, Action onFirstSegment, CancellationToken ct)
    {
        var tokens = Tokenizer.Tokenize(text, voice.GetLangCode(), true);
        var segments = SegmentationSystem.SplitToSegments(tokens, new DefaultSegmentationConfig());

        Debug.WriteLine($"[StreamingSynthesize] segments.Count = {segments.Count}");

        if (segments.Count == 0)
        {
            _synthesisComplete = true;
            return;
        }

        int completed = 0;
        int expected = segments.Count;
        int firstFired = 0;

        var job = KokoroJob.Create(segments, voice, speed, (float[] samples) =>
        {
            var c = Interlocked.Increment(ref completed);
            Debug.WriteLine($"[StreamingSynthesize] Callback {c}/{expected} ({samples.Length} samples)");

            var processed = KokoroPlayback.PostProcessSamples(samples);
            var bytes = KokoroPlayback.GetBytes(processed);
            provider.AddSamples(bytes, 0, bytes.Length);

            if (Interlocked.Exchange(ref firstFired, 1) == 0)
            {
                onFirstSegment();
            }

            if (c >= expected)
            {
                Debug.WriteLine("[StreamingSynthesize] All callbacks received");
                _synthesisComplete = true;
            }
        });

        _currentJob = job;
        _tts!.EnqueueJob(job);

        // Block until synthesis completes or is cancelled
        while (!_synthesisComplete && !ct.IsCancellationRequested)
        {
            ct.WaitHandle.WaitOne(50);
        }

        if (ct.IsCancellationRequested)
        {
            job.Cancel();
            throw new OperationCanceledException(ct);
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_waveOut == null) return;

        if (_isPaused)
        {
            Resume();
        }
        else
        {
            _waveOut.Pause();
            _isPaused = true;
            PauseButton.Content = "Resume";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _currentJob?.Cancel();
        ResetState();
    }

    private void ResetState()
    {
        _isPaused = false;
        _currentJob = null;

        var waveOut = _waveOut;
        _waveOut = null;
        waveOut?.Stop();
        waveOut?.Dispose();

        _bufferedProvider = null;
        _synthesisComplete = false;

        InputBox.IsReadOnly = false;
        InputBox.ClearValue(BackgroundProperty);
        PlayButton.IsEnabled = _tts != null;
        PlayButton.Content = "Play";
        PauseButton.IsEnabled = false;
        PauseButton.Content = "Pause";
        StopButton.IsEnabled = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _currentJob?.Cancel();
        ResetState();
        _tts?.Dispose();
        base.OnClosed(e);
    }
}
