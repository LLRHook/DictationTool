using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
    private RawSourceWaveStream? _audioStream;
    private bool _isPaused;
    private string _originalText = "";
    private CancellationTokenSource? _cts;
    private volatile KokoroJob? _currentJob;
    private readonly DispatcherTimer _highlightTimer;

    public MainWindow()
    {
        InitializeComponent();
        _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _highlightTimer.Tick += UpdateHighlight;
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
        _highlightTimer.Start();
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

        _originalText = text;
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

        try
        {
            var samples = await Task.Run(
                () => SynthesizeAll(normalized, voice, speed, _cts.Token), _cts.Token);

            if (_cts.IsCancellationRequested || samples.Length == 0)
            {
                ResetState();
                return;
            }

            var bytes = KokoroPlayback.GetBytes(samples);
            _audioStream = new RawSourceWaveStream(new MemoryStream(bytes), KokoroPlayback.waveFormat);

            _waveOut?.Dispose();
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioStream);

            PlayButton.Content = "Play";
            PauseButton.IsEnabled = true;

            _waveOut.Play();
            _highlightTimer.Start();
        }
        catch (OperationCanceledException)
        {
            ResetState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Synthesis failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ResetState();
        }
    }

    private float[] SynthesizeAll(string text, KokoroVoice voice, float speed, CancellationToken ct)
    {
        var allSamples = new List<float>();
        var tokens = Tokenizer.Tokenize(text, voice.GetLangCode(), true);
        var segments = SegmentationSystem.SplitToSegments(tokens, new DefaultSegmentationConfig());

        if (segments.Count == 0) return [];

        using var done = new ManualResetEventSlim(false);
        int completed = 0;
        int expected = segments.Count;

        var job = KokoroJob.Create(segments, voice, speed, (float[] samples) =>
        {
            var processed = KokoroPlayback.PostProcessSamples(samples);
            lock (allSamples) { allSamples.AddRange(processed); }
            if (Interlocked.Increment(ref completed) >= expected)
                done.Set();
        });

        _currentJob = job;
        _tts!.EnqueueJob(job);

        try { done.Wait(ct); }
        catch (OperationCanceledException) { job.Cancel(); throw; }

        lock (allSamples) { return [.. allSamples]; }
    }

    private void UpdateHighlight(object? sender, EventArgs e)
    {
        if (_audioStream == null || _waveOut == null) return;

        if (_waveOut.PlaybackState == PlaybackState.Stopped && !_isPaused)
        {
            _highlightTimer.Stop();
            ResetState();
            return;
        }

        if (_audioStream.Length > 0)
        {
            var progress = (double)_audioStream.Position / _audioStream.Length;
            var textPos = Math.Clamp((int)(progress * _originalText.Length), 0, _originalText.Length);
            InputBox.Select(0, textPos);
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
            _highlightTimer.Stop();
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _currentJob?.Cancel();
        _highlightTimer.Stop();
        ResetState();
    }

    private void ResetState()
    {
        _isPaused = false;
        _currentJob = null;

        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;

        _audioStream?.Dispose();
        _audioStream = null;

        InputBox.Select(0, 0);
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
        _highlightTimer.Stop();
        _cts?.Cancel();
        _currentJob?.Cancel();
        ResetState();
        _tts?.Dispose();
        base.OnClosed(e);
    }
}
