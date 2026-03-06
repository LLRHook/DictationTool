# DictationTool

A Windows desktop app that reads text aloud using the [Kokoro](https://github.com/hexgrad/kokoro) text-to-speech model. Paste or type text, pick a voice, and hit Play — the app highlights words as they're spoken, like a teleprompter.

## Features

- Offline TTS powered by Kokoro (ONNX, runs on CPU)
- Multiple English voices (US & UK, male & female)
- Adjustable speech speed (0.5x–2.0x)
- Real-time text highlighting during playback
- Play / Pause / Stop controls

## Requirements

- Windows 10+ with .NET 10
- ~310 MB for the Kokoro ONNX model (downloaded automatically by KokoroSharp on first run)

## Build & Run

```
dotnet run
```

## Dependencies

- [KokoroSharp.CPU](https://github.com/Aldaviva/KokoroSharp) — .NET wrapper for the Kokoro TTS model
- [NAudio](https://github.com/naudio/NAudio) — Audio playback (transitive via KokoroSharp)
