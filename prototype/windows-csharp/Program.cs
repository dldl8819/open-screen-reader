using System.Text;

namespace OpenScreenReader.Prototype;

public static class Program
{
    private const string Usage = """
        사용법: osr-proto [옵션]
          --list-voices     설치된 SAPI 5 음성 목록을 출력하고 끝냅니다.
          --voice <이름>    사용할 음성 이름. 생략하면 한국어 음성을 찾습니다.
          --rate <-10..10>  말하기 속도. 기본값 0.
          --seconds <초>    지정한 시간이 지나면 스스로 끝냅니다. 생략하면 Ctrl+C까지 실행합니다.
          --log <경로>      지연 기록 CSV 경로. 기본값 logs/latency-<시각>.csv
        """;

    // UIA 이벤트 구독은 UI 스레드가 아닌 MTA 스레드에서 해야 합니다.
    [MTAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        string? voice = null;
        int rate = 0;
        TimeSpan? duration = null;
        string? logPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string? value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--list-voices":
                    SpeechOutput.PrintInstalledVoices();
                    return 0;
                case "--voice" when value != null:
                    voice = value;
                    i++;
                    break;
                case "--rate" when int.TryParse(value, out rate):
                    i++;
                    break;
                case "--seconds" when double.TryParse(value, out double seconds) && seconds > 0:
                    duration = TimeSpan.FromSeconds(seconds);
                    i++;
                    break;
                case "--log" when value != null:
                    logPath = value;
                    i++;
                    break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }

        logPath ??= Path.Combine("logs", $"latency-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        using var stop = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };

        using var log = new LatencyLog(logPath);
        using (var speech = new SpeechOutput(voice, rate, log.Record))
        {
            Console.WriteLine($"음성: {speech.VoiceName}");
            Console.WriteLine($"기록: {Path.GetFullPath(logPath)}");

            // 첫 문장은 엔진 초기화 때문에 느립니다. 측정 전에 한 번 말해 예열합니다.
            speech.SpeakAndWait("화면 읽기 프로토타입을 시작합니다.");

            using (new FocusWatcher(speech.Enqueue))
            {
                Console.WriteLine("포커스를 추적하고 있습니다. 끝내려면 Ctrl+C를 누르세요.");
                if (duration is { } limit)
                    stop.Wait(limit);
                else
                    stop.Wait();
            }
        }

        log.PrintSummary();
        return 0;
    }
}
