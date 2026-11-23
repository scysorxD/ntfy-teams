using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace TeamsNotificationPOC;

internal static class TeamsListener
{
    public static async Task<int> RunAsync(NtfyClient ntfy, CancellationToken cancellationToken)
    {
        var listener = UserNotificationListener.Current;
        var access = listener.GetAccessStatus();
        if (access == UserNotificationListenerAccessStatus.Unspecified)
        {
            Console.WriteLine("Solicitando permiso de acceso a las notificaciones de Windows.");
            try
            {
                access = await listener.RequestAccessAsync().AsTask(cancellationToken);
            }
            catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Windows no pudo solicitar el permiso (0x{exception.HResult:X8}). {exception.Message}");
                return ExplainAccess();
            }
        }

        Console.WriteLine($"Acceso a notificaciones de Windows = {access}.");
        if (access != UserNotificationListenerAccessStatus.Allowed)
            return ExplainAccess();

        var pending = Channel.CreateUnbounded<TeamsToast>(new UnboundedChannelOptions { SingleReader = true });
        var seen = new HashSet<(uint Id, DateTimeOffset Created)>();
        var start = DateTimeOffset.UtcNow;
        var accessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        accessTimer.Tick += (_, _) =>
        {
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
                pending.Writer.TryComplete();
        };

        listener.NotificationChanged += OnNotificationChanged;
        try
        {
            accessTimer.Start();
            Console.WriteLine("Escuchando nuevas notificaciones de Microsoft Teams. Ctrl+C para salir.");
            await foreach (var toast in pending.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
                    return ExplainAccess();

                if (!seen.Add((toast.Id, toast.Created)))
                    continue;

                var classification = ToastClassifier.Classify(toast.Title, toast.Attributions);
                toast.Print(classification);
                if (classification.Type == ToastType.Direct)
                    await ntfy.SendDirectNotificationAsync(cancellationToken);
                else
                    Console.WriteLine("ntfy.sh omitido.");
            }

            return ExplainAccess();
        }
        finally
        {
            listener.NotificationChanged -= OnNotificationChanged;
            accessTimer.Stop();
            pending.Writer.TryComplete();
        }

        void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
        {
            if (args.ChangeKind != UserNotificationChangedKind.Added || cancellationToken.IsCancellationRequested)
                return;

            try
            {
                if (sender.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
                    return;

                // Leer por ID en el callback reduce el riesgo de que el toast desaparezca
                // mientras la consola procesa otro evento o espera una respuesta HTTP.
                var notification = sender.GetNotification(args.UserNotificationId);
                if (notification is not null && notification.CreationTime >= start &&
                    TeamsToast.IsTeams(notification.AppInfo.AppUserModelId))
                {
                    pending.Writer.TryWrite(TeamsToast.Read(notification));
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Error al leer la notificación {args.UserNotificationId} " +
                    $"(0x{exception.HResult:X8}). {exception.Message}");
            }
        }
    }

    private static int ExplainAccess()
    {
        Console.Error.WriteLine("Acceso no concedido. Revisá Configuración de Windows > Privacidad y seguridad > Notificaciones " +
            "(ms-settings:privacy-notifications) y volvé a ejecutar --listen si Windows permite habilitar la POC.");
        return 1;
    }
}
