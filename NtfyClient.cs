using System.Net.Http;
using System.Text;

namespace TeamsNotificationPOC;

internal sealed class NtfyClient : IDisposable
{
    private static readonly Uri Endpoint = new("https://ntfy.sh/EYTeams-8492af3d63a6cdb237d423119");
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public Task<bool> SendTestAsync(CancellationToken cancellationToken) =>
        SendAsync("POC Teams: mensaje de prueba", cancellationToken);

    public Task<bool> SendDirectNotificationAsync(CancellationToken cancellationToken) =>
        SendAsync("Teams: mensaje directo detectado", cancellationToken);

    private async Task<bool> SendAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent(message, Encoding.UTF8, "text/plain");
            using var response = await client.PostAsync(Endpoint, content, cancellationToken);
            Console.WriteLine($"ntfy.sh HTTP {(int)response.StatusCode} {response.ReasonPhrase}. " +
                (response.IsSuccessStatusCode ? "Envío aceptado." : "Envío rechazado."));
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException exception)
        {
            Console.Error.WriteLine($"ntfy.sh error de conexión o HTTP. {exception.Message}");
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("ntfy.sh agotó el tiempo de espera del envío.");
            return false;
        }
    }

    public void Dispose() => client.Dispose();
}
