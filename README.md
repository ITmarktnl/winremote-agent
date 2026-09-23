# WinRemote Agent

Open-source Windows-client voor hulp op afstand, onderdeel van [WinRemote.nl](https://winremote.nl): de
Nederlandse tool om een Windows-pc op afstand te bekijken en te bedienen. De klant installeert WinRemote één
keer, start daarna vanaf winremote.nl met één klik een sessie en geeft de zescijferige code door. De technicus
werkt vanuit de browser en ziet het scherm van de klant en kan muis en toetsenbord bedienen.

*Open-source Windows remote-support agent for [WinRemote.nl](https://winremote.nl). The customer installs
WinRemote once, starts a session from the website with a single click and reads a six-digit code to the
technician, who views and controls the screen from the browser.*

## Hoe het werkt

1. De klant klikt op [winremote.nl/hulp](https://winremote.nl/hulp) op **Sessie starten**. De website vraagt een
   sessiecode aan bij `signaal.winremote.nl` en opent `winremote://join/<code>`.
2. Windows start de geïnstalleerde agent met die code (URL-protocol, geregistreerd door de installer). De agent
   vraagt eerst bevestiging en sluit dan aan op de sessie via een WebSocket.
3. Zodra de technicus dezelfde code invoert, maakt de agent een WebRTC-aanbod (SIPSorcery).
4. Het scherm gaat als VP8-video peer-to-peer naar de browser van de technicus, via TURN als dat nodig is.
5. Muis- en toetsenbordacties komen terug over de datakanalen `invoer` (betrouwbaar) en `muis` (snel) en worden
   met `SendInput` uitgevoerd.
6. De klant sluit het venster en de verbinding is weg. Tussen sessies draait er niets op de achtergrond.

De signaleringsserver stuurt alleen kleine berichten door (SDP en ICE); beeld en invoer lopen nooit via de server.

Zonder argumenten vraagt de agent zelf een nieuwe code aan en toont die in het venster. Met `--code 482917`
sluit hij aan op een bestaande code.

## Bouwen

Vereist de [.NET 10 SDK](https://dotnet.microsoft.com/). De agent bouwt op Windows, macOS en Linux:

```sh
dotnet publish -c Release
# → bin/Release/net10.0-windows/win-x64/publish/WinRemote.exe
```

De installer (`WinRemote.msi`, per gebruiker, geen beheerdersrechten) wordt gebouwd met
[WiX 5](https://wixtoolset.org/) en kan alleen op Windows gemaakt worden:

```sh
dotnet tool install --global wix --version 5.0.2
wix build installer/WinRemote.wxs -arch x64 -d Publish=bin/Release/net10.0-windows/win-x64/publish -d Versie=0.2.0 -o WinRemote.msi
```

GitHub Actions doet dit bij elke push; zie [Actions](https://github.com/ITmarktnl/winremote-agent/actions).

## Structuur

| Bestand | Rol |
| --- | --- |
| `Program.cs` | Start, startargumenten (`winremote://join/…`, `--code`) en bevestiging |
| `MainForm.cs` | Venster met sessiecode en status |
| `Signalering.cs` | HTTP + WebSocket naar de signaleringsserver |
| `Sessie.cs` | WebRTC-verbinding, videostream en datakanalen |
| `SchermOpname.cs` | Schermopname met muiscursor (GDI) |
| `Invoer.cs` | Muis en toetsenbord via `SendInput` |
| `Branding.cs` | Naam en kleur |
| `installer/WinRemote.wxs` | WiX-definitie van de MSI en het URL-protocol |

## Privacy en veiligheid

- Verbinden kan alleen met de actuele sessiecode die de klant zelf doorgeeft.
- Gestart via een link vraagt de agent eerst om bevestiging voordat er een sessie begint.
- De klant ziet in het venster wanneer een technicus verbonden is en beëindigt de sessie zelf.
- Het programma draait als de ingelogde gebruiker, zonder beheerdersrechten, en draait niet op de achtergrond.

## Downloaden

Windows-builds worden gemaakt door GitHub Actions en zijn te vinden onder
[Releases](https://github.com/ITmarktnl/winremote-agent/releases) en bij elke
[build](https://github.com/ITmarktnl/winremote-agent/actions). Klanten downloaden de installer via
[winremote.nl/hulp](https://winremote.nl/hulp).

Code signing van de Windows-builds wordt gratis verzorgd door de
[SignPath Foundation](https://signpath.org/), met een certificaat van SignPath.

*Free code signing for the Windows builds is provided by the [SignPath Foundation](https://signpath.org/),
using a certificate by SignPath.*

## Licentie

[GNU Affero General Public License v3.0](LICENSE). Aanpassingen die je verspreidt of via een netwerk aanbiedt,
moet je onder dezelfde licentie openbaar maken.

De website en de signaleringsserver van WinRemote.nl maken geen deel uit van dit project.
