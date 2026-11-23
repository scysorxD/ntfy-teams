# TeamsNotificationPOC

Consola C#/.NET 8 para Windows que observa los nuevos avisos de Microsoft Teams mediante `Windows.UI.Notifications.Management.UserNotificationListener`. Muestra el contenido que expone Windows en la consola. El único aviso que puede enviar a ntfy.sh durante la escucha es `Teams: mensaje directo detectado`.

## Paso 1. Prepará la prueba

Usá Windows 10 versión 2004 (build 19041) o posterior, o Windows 11, con el SDK de .NET 8 o posterior. Abrí PowerShell con tu usuario habitual en la carpeta del repositorio. Para la prueba de escucha, abrí Teams de escritorio en la misma sesión de Windows y coordiná con un compañero el envío de mensajes ficticios por chat privado y grupal.

```powershell
dotnet build -c Release
```

## Paso 2. Confirmá el envío a ntfy.sh

1. Abrí el [topic de prueba en ntfy.sh](https://ntfy.sh/EYTeams-8492af3d63a6cdb237d423119) y dejá la pestaña abierta.
2. Ejecutá el comando de prueba en PowerShell.

   ```powershell
   dotnet run -c Release -- --test-push
   ```

3. Confirmá que la consola dice `ntfy.sh HTTP 200 OK. Envío aceptado.` y que la pestaña muestra un mensaje nuevo con el texto exacto `POC Teams: mensaje de prueba`. Si ves un error HTTP o de conexión, la consola muestra el detalle.

`--test-push` funciona sin instalar un MSIX ni conceder acceso a las notificaciones. Si falta **.NET Desktop Runtime 8**, generá el ejecutable autocontenido y usalo directamente.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o artifacts/layout-x64 -p:BuildMsix=true
& .\artifacts\layout-x64\TeamsNotificationPOC.exe --test-push
```

## Paso 3. Firmá e instalá el MSIX para la escucha

Para instalar la POC necesitás un MSIX firmado con un certificado de firma de código que Windows ya reconozca y que la organización autorice. Si ya tenés un MSIX con esa firma, pasá directamente al punto 3. Si el equipo carece de un certificado autorizado o su política impide la instalación, podés completar la prueba de ntfy.sh del paso 2 y hacer la prueba de escucha en un equipo autorizado. [Firma de paquetes con SignTool](https://learn.microsoft.com/en-us/windows/msix/package/sign-app-package-using-signtool)

1. Buscá la huella del certificado autorizado en el almacén personal del usuario que va a firmar.

   ```powershell
   Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
       Where-Object { $_.HasPrivateKey } |
       Select-Object Subject, Thumbprint, NotAfter
   ```

2. Reemplazá `HUELLA_SHA1_DEL_CERTIFICADO` por el valor de `Thumbprint` y generá el MSIX firmado.

   ```powershell
   $thumbprint = 'HUELLA_SHA1_DEL_CERTIFICADO'
   $certificate = Get-Item "Cert:\CurrentUser\My\$thumbprint" -ErrorAction Stop
   $env:MsixPublisher = $certificate.Subject
   try {
       dotnet publish -c Release -r win-x64 --self-contained true -o artifacts/layout-x64 -p:BuildMsix=true "-p:MsixCertificateThumbprint=$thumbprint"
       if ($LASTEXITCODE -ne 0) { throw 'Falló la generación del MSIX firmado.' }
   } finally {
       $env:MsixPublisher = $null
   }
   ```

3. Instalá el archivo firmado con tu usuario habitual y confirmá que PowerShell lista `TeamsNotificationPOC`. Si recibiste un paquete firmado, usá su ruta en `Add-AppxPackage`.

   ```powershell
   Add-AppxPackage -Path .\artifacts\TeamsNotificationPOC-x64.msix -ErrorAction Stop
   Get-AppxPackage -Name TeamsNotificationPOC | Select-Object Name, Version
   ```

Los comandos usan x64. En ARM64, reemplazá `x64` por `arm64` en el RID, la carpeta de salida y el nombre del paquete.

## Paso 4. Iniciá la escucha y concedé el permiso

```powershell
TeamsNotificationPOC.exe --listen
```

Ejecutá el comando mediante el alias instalado. Si PowerShell no lo encuentra, comprobá que `%LOCALAPPDATA%\Microsoft\WindowsApps` esté en `PATH` y que el alias figure habilitado en **Configuración > Aplicaciones > Configuración avanzada de aplicaciones > Alias de ejecución de aplicaciones**.

Elegí **Permitir** cuando Windows solicite acceso a las notificaciones. Esperá estas dos líneas antes de pedir el primer mensaje.

```text
Acceso a notificaciones de Windows = Allowed.
Escuchando nuevas notificaciones de Microsoft Teams. Ctrl+C para salir.
```

Si aparece `Denied` o no se muestra el cuadro de permiso, ejecutá `Start-Process 'ms-settings:privacy-notifications'`. Si Windows muestra **TeamsNotificationPOC**, concedele el permiso y volvé a ejecutar `--listen`. Si la opción está bloqueada por una directiva, la escucha requiere un equipo autorizado.

## Paso 5. Probá los escenarios en Teams

Dejá abiertas la consola y la pestaña de ntfy.sh. Mantené Teams abierto y cambiá a otra ventana antes de pedir el primer mensaje para que el chat de prueba quede fuera de foco.

| Prueba | Qué hacer | Qué comprobar |
| --- | --- | --- |
| Chat directo | Pedí un mensaje 1:1 con el texto `POC 1:1 01`. | Buscá la fecha, el ID, el posible remitente, el título, el cuerpo y los elementos `Texto[n]`. Si dice `Directo 1:1 (indicio explícito)`, buscá un único aviso nuevo `Teams: mensaje directo detectado` en el topic. Si dice `Indeterminada`, esperá `ntfy.sh omitido.` en la consola. |
| Varios mensajes | Pedí `POC 1:1 02`, `03` y `04`, con unos segundos entre ellos. | Cada par de ID y fecha debe aparecer una sola vez. Compará los avisos nuevos en ntfy.sh con los registros clasificados como directos. Teams puede agrupar varios mensajes en un toast. |
| Chat grupal | Pedí `POC grupo 01` en un chat con tres o más participantes. | Esperá `Chat grupal (indicio explícito)` o `Indeterminada`, según el título y la atribución que entregue Windows. En ambos casos la consola debe indicar `ntfy.sh omitido.` Si aparece como directo, registralo como un falso positivo de la heurística. |
| Teams minimizado | Minimizá Teams desde el botón de la ventana y pedí `POC minimizado 01` por chat 1:1. | Compará el resultado con el primer directo. Si la consola queda vacía, revisá si el aviso apareció en el centro de notificaciones de Windows y si Teams tiene habilitados banners y vistas previas para chats. |

Presioná **Ctrl+C** para cerrar la escucha.

## Cómo funciona la escucha

El manifiesto declara `uap3:Capability Name="userNotificationListener"` y registra el alias de ejecución. `runFullTrust` permite ejecutar la aplicación de escritorio empaquetada con los permisos normales del usuario. `--listen` comprueba que el proceso tiene identidad de paquete. La POC usa un hilo STA con un dispatcher para solicitar el consentimiento de Windows desde el hilo de interfaz requerido por la API. El destino de MSBuild usa MakeAppx y SignTool del paquete NuGet `Microsoft.Windows.SDK.BuildTools`. [API de escucha y consentimiento](https://learn.microsoft.com/en-us/windows/uwp/design/shell/tiles-and-notifications/notification-listener) · [Capability del manifiesto](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap3-capability-manual)

## Qué muestra la consola

- La fecha y hora de creación del aviso en Windows, su ID y la identidad de la app. El ID corresponde al toast de Windows.
- Un posible remitente, un título, un cuerpo y texto adicional. El título y el cuerpo se infieren de las primeras líneas, y el posible remitente puede coincidir con el título. En un grupo, el título podría corresponder al nombre del chat. También se listan, en orden, todos los elementos de texto que entrega la API por plantilla, con sus idiomas y atributos de formato cuando están disponibles.
- El tipo inferido y su motivo. La clasificación se basa en marcadores explícitos en el título o la atribución, en español e inglés. El contenido del mensaje queda fuera de la clasificación. Windows no expone un campo de tipo de chat de Teams ni una identidad verificada del remitente. Un directo y un grupo pueden usar el mismo formato visual. Si falta un marcador reconocido o hay señales contradictorias, la consola indica `Indeterminada` y omite el envío. Los marcadores son heurísticos y pueden variar según la versión y el idioma de Teams.
- El resultado de ntfy.sh cuando se reconoce un marcador de directo. La solicitud solo contiene el texto fijo. El contenido del toast, los nombres, los identificadores y otros datos recuperados quedan en la consola, sin archivos de log.

La POC se suscribe a eventos nuevos y consulta cada aviso por ID cuando Windows lo agrega. Deduplica en memoria por ID y fecha de creación durante la ejecución. Si Teams emite dos avisos con ID o fecha distintos para un mismo mensaje, Windows los presenta como avisos distintos. Solo se puede recuperar el texto que Teams publique en un toast nativo y Windows todavía pueda entregar. La vista previa puede estar oculta o truncada. Un aviso también puede desaparecer antes de la lectura o agrupar varios mensajes. La POC no consulta conversaciones anteriores.

Los dos modos usan el mismo endpoint y hacen un intento de POST por envío con `Content-Type: text/plain; charset=utf-8`.

En este entorno se verificaron la compilación Release, la creación del MSIX x64 y la publicación y recepción del mensaje ficticio en ntfy.sh. El consentimiento, la captura de avisos de Teams y su clasificación quedan pendientes de la prueba con un MSIX firmado y autorizado.
