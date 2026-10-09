using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OpenScreenReader.Prototype;

/// <summary>P3: 포커스 이벤트부터 음성 시작까지의 구간별 지연을 CSV와 콘솔에 기록합니다.</summary>
public sealed class LatencyLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _csv;
    private readonly Dictionary<int, string> _processNames = new();
    private readonly List<double> _properties = new();
    private readonly List<double> _requested = new();
    private readonly List<double> _started = new();
    private readonly List<double> _firstAudio = new();

    public LatencyLog(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Excel이 한글을 바로 읽도록 BOM을 붙입니다.
        _csv = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
        {
            AutoFlush = true,
        };
        _csv.WriteLine("time,app,control_type_id,role,name,cancelled,properties_ms,speak_requested_ms,speak_started_ms,first_audio_ms");
    }

    public void Record(FocusSample sample)
    {
        // 모든 구간은 이벤트 수신 시각을 기준으로 잽니다.
        double? properties = Milliseconds(sample.EventReceived, sample.PropertiesRead);
        double? requested = Milliseconds(sample.EventReceived, sample.SpeakRequested);
        double? started = Milliseconds(sample.EventReceived, sample.SpeakStarted);
        double? firstAudio = Milliseconds(sample.EventReceived, sample.FirstAudio);

        lock (_gate)
        {
            string app = ProcessName(sample.ProcessId);

            Add(_properties, properties);
            Add(_requested, requested);
            Add(_started, started);
            Add(_firstAudio, firstAudio);

            _csv.WriteLine(string.Join(',',
                sample.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                Quote(app),
                sample.ControlTypeId.ToString(CultureInfo.InvariantCulture),
                Quote(sample.Role),
                Quote(sample.Name),
                sample.Cancelled ? "1" : "0",
                Format(properties),
                Format(requested),
                Format(started),
                Format(firstAudio)));

            string result = firstAudio is { } ms
                ? $"첫 소리 {ms:F1} ms"
                : "첫 소리 전에 중단됨";
            Console.WriteLine($"[{app}] {sample.Role}: \"{sample.Name}\" - {result}");
        }
    }

    public void PrintSummary()
    {
        lock (_gate)
        {
            Console.WriteLine();
            Console.WriteLine("지연 요약 (기준: UIA 이벤트 수신 시각, 단위: ms)");
            PrintStage("속성 읽기 완료", _properties);
            PrintStage("음성 요청 완료", _requested);
            PrintStage("엔진 처리 시작", _started);
            PrintStage("첫 소리(근삿값)", _firstAudio);
        }
    }

    private static void PrintStage(string label, List<double> values)
    {
        if (values.Count == 0)
        {
            Console.WriteLine($"{label}: 0건");
            return;
        }

        values.Sort();
        Console.WriteLine(
            $"{label}: {values.Count}건, 최소 {values[0]:F1}, 중앙값 {Percentile(values, 0.50):F1}, " +
            $"95% {Percentile(values, 0.95):F1}, 최대 {values[^1]:F1}");
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted[Math.Max(0, (int)Math.Ceiling(p * sorted.Count) - 1)];

    private static double? Milliseconds(long from, long to) =>
        to == 0 ? null : Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;

    private static void Add(List<double> values, double? value)
    {
        if (value is { } v)
            values.Add(v);
    }

    private static string Format(double? value) =>
        value?.ToString("F3", CultureInfo.InvariantCulture) ?? "";

    private static string Quote(string field) => "\"" + field.Replace("\"", "\"\"") + "\"";

    private string ProcessName(int processId)
    {
        if (_processNames.TryGetValue(processId, out string? name))
            return name;

        try
        {
            using var process = Process.GetProcessById(processId);
            name = process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            name = $"pid {processId}";
        }

        _processNames[processId] = name;
        return name;
    }

    public void Dispose() => _csv.Dispose();
}
