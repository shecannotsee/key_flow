using Gma.System.MouseKeyHook;
using System.Windows.Forms;

namespace KeyFlow.Windows;

// Only keyboard subscriptions are installed. Callback consumers must enqueue work,
// never perform native output or logging on this dedicated message-loop thread.
public sealed class GlobalKeyboardListener : IDisposable
{
    private readonly Thread thread;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Control? dispatch;
    private ApplicationContext? context;
    private volatile bool disposed;
    public event Action<uint, bool>? KeyChanged;
    public Exception? StartupError { get; private set; }
    public bool IsReady => ready.Task.IsCompletedSuccessfully && StartupError is null && !disposed;

    public GlobalKeyboardListener()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "KeyFlow keyboard listener" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Task.Wait(TimeSpan.FromSeconds(5)))
        {
            disposed = true;
            StartupError = new TimeoutException("Keyboard listener startup timed out");
        }
    }

    private void Run()
    {
        try
        {
            using var control = new Control();
            _ = control.Handle;
            using var loop = new ApplicationContext();
            using var events = Hook.GlobalEvents();
            events.KeyDown += (_, e) => Forward(e, true);
            events.KeyUp += (_, e) => Forward(e, false);
            dispatch = control;
            context = loop;
            ready.TrySetResult(true);
            if (!disposed) Application.Run(loop);
        }
        catch (Exception exception)
        {
            StartupError = exception;
            ready.TrySetResult(false);
        }
    }

    private void Forward(KeyEventArgs e, bool down)
    {
        if (!disposed) KeyChanged?.Invoke((uint)e.KeyCode, down);
        // Do not set Handled or SuppressKeyPress; pass keys to other applications.
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var control = dispatch;
        if (control is not null && !control.IsDisposed)
        {
            try { control.BeginInvoke(new Action(() => context?.ExitThread())); }
            catch (InvalidOperationException) { }
        }
        thread.Join(TimeSpan.FromSeconds(2));
    }
}
