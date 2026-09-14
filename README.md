# Windowshulp Agent

Open-source Windows-client voor hulp op afstand, onderdeel van [Windowshulp.nl](https://windowshulp.nl): een
white-label supporttool voor IT-bedrijven. De klant start dit programma, ziet een zescijferige sessiecode en
geeft die door aan de technicus. De technicus werkt vanuit de browser en ziet het scherm van de klant en kan
muis en toetsenbord bedienen.

*Open-source Windows remote-support agent for [Windowshulp.nl](https://windowshulp.nl), a white-label support
tool for IT companies. The customer runs this program, reads a six-digit session code to the technician, and the
technician views and controls the screen from the browser.*

## Hoe het werkt

1. De agent vraagt een sessiecode aan bij `signaal.windowshulp.nl` en opent daar een WebSocket.
2. Zodra een technicus dezelfde code invoert, maakt de agent een WebRTC-aanbod (SIPSorcery).
3. Het scherm gaat als VP8-video peer-to-peer naar de browser van de technicus, via TURN als dat nodig is.
4. Muis- en toetsenbordacties komen terug over het datakanaal `invoer` en worden met `SendInput` uitgevoerd.
5. De klant sluit het venster en de verbinding is weg. Er wordt niets geïnstalleerd en niets achtergelaten.

De signaleringsserver stuurt alleen kleine berichten door (SDP en ICE); beeld en invoer lopen nooit via de server.

## Bouwen

Vereist de [.NET 10 SDK](https://dotnet.microsoft.com/). Bouwen kan op Windows, macOS en Linux:

```sh
dotnet publish -c Release
# → bin/Release/net10.0-windows/win-x64/publish/Windowshulp.exe
```

Het resultaat is één zelfstandige `.exe` zonder installatie. De huisstijl van het IT-bedrijf wordt afgeleid uit de
bestandsnaam: `Windowshulp-<bedrijf>.exe`.

## Structuur

| Bestand | Rol |
| --- | --- |
| `Program.cs`, `MainForm.cs` | Venster met huisstijl, sessiecode en status |
| `Signalering.cs` | HTTP + WebSocket naar de signaleringsserver |
| `Sessie.cs` | WebRTC-verbinding, videostream en datakanaal |
| `SchermOpname.cs` | Schermopname met muiscursor (GDI) |
| `Invoer.cs` | Muis en toetsenbord via `SendInput` |
| `Branding.cs` | Naam, kleur en tenant-slug |

## Privacy en veiligheid

- Verbinden kan alleen met de actuele sessiecode die de klant zelf doorgeeft.
- De klant ziet in het venster wanneer een technicus verbonden is en beëindigt de sessie zelf.
- Het programma draait als de ingelogde gebruiker, zonder beheerdersrechten, en installeert niets.

## Licentie

[GNU Affero General Public License v3.0](LICENSE). Aanpassingen die je verspreidt of via een netwerk aanbiedt,
moet je onder dezelfde licentie openbaar maken.

De website, de signaleringsserver en het tenantbeheer van Windowshulp.nl maken geen deel uit van dit project.
