using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace TeamsNotificationPOC;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        using var stopping = new CancellationTokenSource();
        using var ntfy = new NtfyClient();
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            stopping.Cancel();
        };
        Console.CancelKeyPress += cancel;

        try
        {
            return args switch
            {
                ["--test-push"] => ntfy.SendTestAsync(stopping.Token).GetAwaiter().GetResult() ? 0 : 1,
                ["--listen"] => Listen(ntfy, stopping.Token),
                _ => PrintUsage()
            };
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error (0x{exception.HResult:X8}). {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static int Listen(NtfyClient ntfy, CancellationToken cancellationToken)
    {
        uint length = 0;
        const int errorInsufficientBuffer = 122; // Windows devuelve el tamaño si hay identidad.
        if (GetCurrentPackageFullName(ref length, IntPtr.Zero) != errorInsufficientBuffer)
        {
            Console.Error.WriteLine("--listen requiere identidad MSIX. Instalá el paquete y ejecutá TeamsNotificationPOC.exe --listen desde el alias registrado. Consultá README.md.");
            return 1;
        }

        using var instance = new Mutex(true, @"Local\TeamsNotificationPOC-Listener", out bool isFirst);
        if (!isFirst)
        {
            Console.Error.WriteLine("Ya hay una escucha en esta sesión de Windows.");
            return 1;
        }

        try
        {
            // El dispatcher atiende mensajes de Windows en el hilo STA durante la solicitud
            // de consentimiento. La aplicación sigue siendo de consola y no crea ventanas.
            var dispatcher = Dispatcher.CurrentDispatcher;
            var listenTask = dispatcher.InvokeAsync(() => TeamsListener.RunAsync(ntfy, cancellationToken))
                .Task.Unwrap();
            _ = listenTask.ContinueWith(
                _ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            Dispatcher.Run();
            return listenTask.GetAwaiter().GetResult();
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    private static int PrintUsage()
    {
        Console.WriteLine("TeamsNotificationPOC --test-push | --listen");
        return 2;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr packageFullName);
}
