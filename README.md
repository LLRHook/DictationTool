# DictationTool

A Windows desktop app that reads text aloud using the [Kokoro](https://github.com/hexgrad/kokoro) text-to-speech model. Paste or type text, pick a voice, and hit Play — audio starts streaming within seconds, even for long documents.

## Features

- Offline TTS powered by Kokoro (ONNX, runs on CPU)
- Streaming playback — audio begins after the first segment is synthesized, not after the entire text is processed
- Multiple English voices (US & UK, male & female)
- Adjustable speech speed (0.5x–2.0x)
- Play / Pause / Stop controls

## Requirements

- Windows 10+ with .NET 10
- ~310 MB for the Kokoro ONNX model (downloaded automatically by KokoroSharp on first run)

## Build & Run

```
dotnet run
```

## Tests

```
dotnet run --project DictationTool.Tests
```

## Dependencies

- [KokoroSharp.CPU](https://github.com/Aldaviva/KokoroSharp) — .NET wrapper for the Kokoro TTS model
- [NAudio](https://github.com/naudio/NAudio) — Audio playback (transitive via KokoroSharp)
