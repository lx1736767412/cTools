using System.Threading;

namespace cTools.App.Services;

public static class SingleInstance
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private static Thread? _listener;

    public static bool TryAcquire(Action onShow)
    {
        _mutex = new Mutex(true, @"Local\cTools.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(@"Local\cTools.Show", out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch
            {
                // 第二个实例无法通知第一个实例时直接退出。
            }
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\cTools.Show");
        _listener = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (!_showEvent.WaitOne()) continue;
                    onShow();
                }
                catch
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "cTools.SingleInstance"
        };
        _listener.Start();
        return true;
    }

    public static void Dispose()
    {
        try
        {
            _showEvent?.Set();
            _showEvent?.Dispose();
        }
        catch { }
        _showEvent = null;
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch { }
        _mutex?.Dispose();
        _mutex = null;
    }
}
