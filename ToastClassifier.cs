namespace TeamsNotificationPOC;

internal enum ToastType { Unknown, Direct, Group, Channel, Meeting, Other }

internal sealed record ToastClassification(ToastType Type, string Label, string Reason, string? Sender = null);

internal static class ToastClassifier
{
    private static readonly (ToastType Type, string Label, string[] Markers)[] Rules =
    [
        (ToastType.Direct, "Directo 1:1 (indicio explícito)", [
            "Direct message", "New direct message", "1:1 chat", "Mensaje directo", "Nuevo mensaje directo", "Chat 1:1"]),
        (ToastType.Group, "Chat grupal (indicio explícito)", [
            "Group chat", "New message in group chat", "Chat grupal", "Chat de grupo", "Nuevo mensaje en chat grupal"]),
        (ToastType.Channel, "Canal (indicio explícito)", [
            "Channel message", "New channel message", "Mensaje de canal", "Nuevo mensaje de canal"]),
        (ToastType.Meeting, "Reunión (indicio explícito)", [
            "Meeting chat", "Meeting reminder", "Meeting started", "Chat de reunión", "Recordatorio de reunión", "Reunión iniciada"]),
        (ToastType.Other, "Otra notificación (indicio explícito)", [
            "Incoming call", "Missed call", "Llamada entrante", "Llamada perdida"])
    ];

    public static ToastClassification Classify(string? title, IEnumerable<string> attributions)
    {
        var evidence = new List<(ToastType Type, string Label, string Field, string? Sender)>();
        AddEvidence(title, "título", allowSenderSuffix: true);
        foreach (var attribution in attributions)
            AddEvidence(attribution, "atribución", allowSenderSuffix: false);

        if (evidence.Count == 0)
            return new(ToastType.Unknown, "Indeterminada", "El título y la atribución carecen de un marcador de tipo reconocido.");
        if (evidence.Any(item => item.Type != evidence[0].Type))
            return new(ToastType.Unknown, "Indeterminada", "El título y la atribución tienen marcadores contradictorios.");

        var match = evidence[0];
        return new(match.Type, match.Label, $"Marcador de tipo encontrado en {match.Field}.",
            match.Sender ?? (match.Type == ToastType.Direct && match.Field == "atribución" ? title : null));

        void AddEvidence(string? value, string field, bool allowSenderSuffix)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var text = value.Trim();
            foreach (var (type, label, markers) in Rules)
            {
                if (markers.Contains(text, StringComparer.OrdinalIgnoreCase))
                {
                    evidence.Add((type, label, field, null));
                    return;
                }

                if (!allowSenderSuffix || type != ToastType.Direct)
                    continue;

                foreach (var marker in markers)
                {
                    if (text.StartsWith(marker + ": ", StringComparison.OrdinalIgnoreCase))
                    {
                        evidence.Add((type, label, field, text[(marker.Length + 2)..]));
                        return;
                    }
                }
            }
        }
    }
}
