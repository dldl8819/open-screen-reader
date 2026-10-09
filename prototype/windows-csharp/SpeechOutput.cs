using System.Collections.Concurrent;
using System.Diagnostics;
using System.Speech.Synthesis;
using System.Threading.Channels;

namespace OpenScreenReader.Prototype;

/// <summary>P2: 텍스트를 SAPI 5 음성으로 출력하고, 음성이 시작된 시각을 표본에 기록합니다.</summary>
public sealed class SpeechOutput : IDisposable
{
    private readonly SpeechSynthesizer _synth = new();
    private readonly Channel<FocusSample> _channel = Channel.CreateUnbounded<FocusSample>();
    private readonly ConcurrentDictionary<Prompt, FocusSample> _speaking = new();
    private readonly Action<FocusSample> _onFinished;
    private readonly Task _loop;

    public SpeechOutput(string? voiceName, int rate, Action<FocusSample> onFinished)
    {
        _onFinished = onFinished;
        _synth.SetOutputToDefaultAudioDevice();
        _synth.Rate = Math.Clamp(rate, -10, 10);
        SelectVoice(voiceName);

        _synth.SpeakStarted += (_, e) =>
        {
            long now = Stopwatch.GetTimestamp();
            if (_speaking.TryGetValue(e.Prompt, out var sample))
                sample.SpeakStarted = now;
        };
        _synth.SpeakProgress += (_, e) =>
        {
            long now = Stopwatch.GetTimestamp();
            if (_speaking.TryGetValue(e.Prompt, out var sample) && sample.FirstAudio == 0)
                sample.FirstAudio = now;
        };
        _synth.SpeakCompleted += (_, e) => Finish(e.Prompt, e.Cancelled);

        _loop = Task.Run(RunAsync);
    }

    public string VoiceName => _synth.Voice.Name;

    public static void PrintInstalledVoices()
    {
        using var synth = new SpeechSynthesizer();
        foreach (var voice in synth.GetInstalledVoices())
        {
            string disabled = voice.Enabled ? "" : " - 사용 안 함";
            Console.WriteLine($"{voice.VoiceInfo.Name} ({voice.VoiceInfo.Culture.Name}){disabled}");
        }
    }

    /// <summary>끝까지 말한 뒤 돌아옵니다. 시작 안내와 엔진 예열에 씁니다.</summary>
    public void SpeakAndWait(string text) => _synth.Speak(text);

    /// <summary>UIA 스레드를 붙잡지 않도록, 실제 음성 호출은 별도 작업에서 합니다.</summary>
    public void Enqueue(FocusSample sample) => _channel.Writer.TryWrite(sample);

    private async Task RunAsync()
    {
        Prompt? previous = null;
        await foreach (var sample in _channel.Reader.ReadAllAsync())
        {
            // 새 포커스가 오면 이전 음성을 끊습니다. 끊지 않으면 앞 문장을 기다린 시간이 지연에 섞입니다.
            _synth.SpeakAsyncCancelAll();
            if (previous != null)
                Finish(previous, cancelled: true);
            previous = null;

            if (sample.Text.Length == 0)
            {
                _onFinished(sample);
                continue;
            }

            var prompt = new Prompt(sample.Text);
            _speaking[prompt] = sample;
            _synth.SpeakAsync(prompt);
            sample.SpeakRequested = Stopwatch.GetTimestamp();
            previous = prompt;
        }
    }

    // SpeakCompleted와 다음 포커스의 중단 처리 중 먼저 온 쪽만 기록합니다.
    private void Finish(Prompt prompt, bool cancelled)
    {
        if (_speaking.TryRemove(prompt, out var sample))
        {
            sample.Cancelled = cancelled;
            _onFinished(sample);
        }
    }

    private void SelectVoice(string? voiceName)
    {
        if (voiceName != null)
        {
            _synth.SelectVoice(voiceName);
            return;
        }

        var korean = _synth.GetInstalledVoices()
            .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name == "ko-KR");
        if (korean != null)
            _synth.SelectVoice(korean.VoiceInfo.Name);
        else
            Console.Error.WriteLine("경고: 한국어 SAPI 5 음성을 찾지 못해 기본 음성을 사용합니다.");
    }

    public void Dispose()
    {
        _channel.Writer.Complete();
        _loop.Wait();
        _synth.SpeakAsyncCancelAll();
        foreach (var prompt in _speaking.Keys)
            Finish(prompt, cancelled: true);
        _synth.Dispose();
    }
}
