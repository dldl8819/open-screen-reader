namespace OpenScreenReader.Prototype;

/// <summary>
/// 포커스 이벤트 한 건과 그 처리 과정의 시각입니다.
/// 시각은 모두 Stopwatch 틱이며, 0은 그 단계에 도달하지 못했다는 뜻입니다.
/// </summary>
public sealed class FocusSample
{
    public required string Name { get; init; }
    public required string Role { get; init; }
    public required int ControlTypeId { get; init; }
    public required int ProcessId { get; init; }
    public DateTime Time { get; } = DateTime.Now;

    /// <summary>UIA 이벤트 처리기에 들어온 시각. 모든 지연의 기준점입니다.</summary>
    public required long EventReceived { get; init; }

    /// <summary>이름과 역할을 다 읽은 시각.</summary>
    public required long PropertiesRead { get; init; }

    /// <summary>이전 음성을 끊고 새 음성을 엔진에 넘긴 시각.</summary>
    public long SpeakRequested { get; set; }

    /// <summary>엔진이 이 문장을 처리하기 시작한 시각 (SpeakStarted).</summary>
    public long SpeakStarted { get; set; }

    /// <summary>첫 낱말 경계 이벤트 시각 (SpeakProgress). 실제 소리가 나기 시작한 시각의 근삿값입니다.</summary>
    public long FirstAudio { get; set; }

    public bool Cancelled { get; set; }

    public string Text => string.IsNullOrWhiteSpace(Name) ? Role : $"{Name}, {Role}";
}
