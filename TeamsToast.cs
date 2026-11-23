using System.Text.Encodings.Web;
using System.Text.Json;
using Windows.UI.Notifications;

namespace TeamsNotificationPOC;

internal sealed record TeamsToast(
    uint Id, DateTimeOffset Created, string AppId, string AppName, string Language, ToastBinding[] Bindings)
{
    private static readonly JsonSerializerOptions LogJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private ToastBinding? MainBinding =>
        Bindings.FirstOrDefault(binding => binding.Template.Equals("ToastGeneric", StringComparison.OrdinalIgnoreCase))
        ?? Bindings.FirstOrDefault();

    private IEnumerable<ToastText> MainText => MainBinding?.Texts.Where(text => !text.IsAttribution) ?? [];

    public string? Title => MainText.FirstOrDefault()?.Text;
    public IEnumerable<string> Attributions =>
        MainBinding?.Texts.Where(text => text.IsAttribution).Select(text => text.Text) ?? [];

    public static bool IsTeams(string appId) =>
        appId.StartsWith("MSTeams_8wekyb3d8bbwe!", StringComparison.OrdinalIgnoreCase) ||
        appId.StartsWith("MicrosoftTeams_8wekyb3d8bbwe!", StringComparison.OrdinalIgnoreCase) ||
        appId.Equals("com.squirrel.Teams.Teams", StringComparison.OrdinalIgnoreCase);

    public static TeamsToast Read(UserNotification notification)
    {
        var app = notification.AppInfo;
        var visual = notification.Notification.Visual;
        var bindings = visual?.Bindings.Select(binding => new ToastBinding(
            binding.Template, binding.Language, binding.Hints.ToArray(),
            binding.GetTextElements().Select(text => new ToastText(text.Text, text.Language, text.Hints.ToArray())).ToArray()
        )).ToArray() ?? [];
        return new(notification.Id, notification.CreationTime, app.AppUserModelId, app.DisplayInfo.DisplayName,
            visual?.Language ?? "", bindings);
    }

    public void Print(ToastClassification classification)
    {
        var body = MainText.Skip(1).FirstOrDefault()?.Text;
        var sender = classification.Sender ?? (classification.Type == ToastType.Unknown && body is not null ? Title : null);
        Console.WriteLine();
        Console.WriteLine($"Teams | Id {Id} | Fecha/hora {Created.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff zzz}");
        Console.WriteLine($"  App {Quote(AppName)} | AUMID {Quote(AppId)} | Idioma visual {Quote(Language)}");
        Console.WriteLine($"  Posible remitente = {Quote(sender)}");
        Console.WriteLine($"  Título = {Quote(Title)}");
        Console.WriteLine($"  Cuerpo = {Quote(body)}");
        var additional = MainText.Skip(2).Select(text => text.Text).Concat(Attributions).ToArray();
        Console.WriteLine($"  Texto adicional = {JsonSerializer.Serialize(additional, LogJson)}");

        foreach (var binding in Bindings)
        {
            Console.WriteLine($"  Plantilla {Quote(binding.Template)} | Idioma {Quote(binding.Language)}");
            PrintHints(binding.Hints, "    ");
            for (int index = 0; index < binding.Texts.Length; index++)
            {
                var text = binding.Texts[index];
                Console.WriteLine($"    Texto[{index}] = {Quote(text.Text)} | Idioma {Quote(text.Language)}");
                PrintHints(text.Hints, "      ");
            }
        }

        Console.WriteLine($"  Clasificación = {classification.Label}. {classification.Reason}");
    }

    private static string Quote(string? value) => value is null ? "(no disponible)" : JsonSerializer.Serialize(value, LogJson);

    private static void PrintHints(KeyValuePair<string, string>[] hints, string indent)
    {
        foreach (var (key, value) in hints)
            Console.WriteLine($"{indent}Hint {Quote(key)} = {Quote(value)}");
    }
}

internal sealed record ToastBinding(
    string Template, string Language, KeyValuePair<string, string>[] Hints, ToastText[] Texts);

internal sealed record ToastText(string Text, string Language, KeyValuePair<string, string>[] Hints)
{
    public bool IsAttribution => Hints.Any(hint =>
        (hint.Key.Equals("placement", StringComparison.OrdinalIgnoreCase) ||
         hint.Key.Equals("hint-placement", StringComparison.OrdinalIgnoreCase)) &&
        hint.Value.Equals("attribution", StringComparison.OrdinalIgnoreCase));
}
