using System.Diagnostics;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;

namespace OpenScreenReader.Prototype;

/// <summary>P1: UI Automation 포커스 변경 이벤트를 받아 요소의 이름과 역할을 읽습니다.</summary>
public sealed class FocusWatcher : IUIAutomationFocusChangedEventHandler, IDisposable
{
    private readonly IUIAutomation _automation = new CUIAutomation8();
    private readonly Action<FocusSample> _onFocus;

    public FocusWatcher(Action<FocusSample> onFocus)
    {
        _onFocus = onFocus;

        // 필요한 속성을 이벤트와 함께 미리 받아 둡니다.
        // 이렇게 하면 처리기 안에서 대상 프로세스를 다시 호출하지 않아도 됩니다.
        var cache = _automation.CreateCacheRequest();
        cache.AddProperty(UIA_PropertyIds.UIA_NamePropertyId);
        cache.AddProperty(UIA_PropertyIds.UIA_ControlTypePropertyId);
        cache.AddProperty(UIA_PropertyIds.UIA_LocalizedControlTypePropertyId);
        cache.AddProperty(UIA_PropertyIds.UIA_ProcessIdPropertyId);

        _automation.AddFocusChangedEventHandler(cache, this);
    }

    // UIA가 만든 스레드에서 호출됩니다. 여기서 오래 머물면 다음 이벤트가 밀립니다.
    public void HandleFocusChangedEvent(IUIAutomationElement sender)
    {
        long received = Stopwatch.GetTimestamp();
        try
        {
            string name = sender.CachedName ?? "";
            string role = sender.CachedLocalizedControlType ?? "";
            int controlTypeId = sender.CachedControlType;
            int processId = sender.CachedProcessId;

            _onFocus(new FocusSample
            {
                Name = name,
                Role = role,
                ControlTypeId = controlTypeId,
                ProcessId = processId,
                EventReceived = received,
                PropertiesRead = Stopwatch.GetTimestamp(),
            });
        }
        catch (COMException)
        {
            // 이벤트가 도착하기 전에 요소가 사라진 경우입니다.
        }
    }

    public void Dispose() => _automation.RemoveFocusChangedEventHandler(this);
}
