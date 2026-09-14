using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;

namespace Windowshulp.Agent;

/// <summary>
/// Eén supportsessie: WebRTC-verbinding met de technicus. Deze kant (de klant-pc) maakt het aanbod,
/// stuurt het scherm als VP8-video en ontvangt muis/toetsenbord via het datakanaal "invoer".
/// </summary>
public sealed class Sessie : IAsyncDisposable
{
	private const int Fps = 15;
	private const uint RtpDuur = 90000 / Fps;

	private readonly Signalering _sig;
	private readonly SchermOpname _opname = new(maxBreedte: 1920);
	private RTCPeerConnection? _pc;
	private RTCDataChannel? _dc;
	private RTCDataChannel? _dcMuis;
	private VpxVideoEncoder? _enc;
	private CancellationTokenSource? _streamCts;
	private readonly object _slot = new();

	public event Action<string>? Status;
	public event Action<bool>? TechnicusVerbonden;

	public Sessie(Signalering sig)
	{
		_sig = sig;
		_sig.BerichtOntvangen += OnBericht;
	}

	private async void OnBericht(JsonNode n)
	{
		try
		{
			switch (n["type"]?.GetValue<string>())
			{
				case "peer":
					if (n["rol"]?.GetValue<string>() == "tech")
					{
						if (n["aanwezig"]?.GetValue<bool>() == true)
						{
							Status?.Invoke("Technicus gevonden, verbinding opzetten…");
							await StartAanbodAsync();
						}
						else
						{
							StopStream();
							Status?.Invoke("Technicus heeft de sessie verlaten. Wachten…");
							TechnicusVerbonden?.Invoke(false);
						}
					}
					break;

				case "answer":
					if (_pc is null) break;
					var res = _pc.setRemoteDescription(new RTCSessionDescriptionInit
					{
						type = RTCSdpType.answer,
						sdp = n["sdp"]!.GetValue<string>()
					});
					if (res != SetDescriptionResultEnum.OK) Status?.Invoke($"Antwoord van technicus afgewezen: {res}");
					break;

				case "ice":
					if (_pc is null || n["candidate"] is null) break;
					_pc.addIceCandidate(new RTCIceCandidateInit
					{
						candidate = n["candidate"]!.GetValue<string>(),
						sdpMid = n["sdpMid"]?.GetValue<string>() ?? "0",
						sdpMLineIndex = (ushort)(n["sdpMLineIndex"]?.GetValue<int>() ?? 0)
					});
					break;

				case "einde":
					Sluit("Technicus heeft de sessie beëindigd.");
					break;
			}
		}
		catch (Exception ex)
		{
			Status?.Invoke($"Fout: {ex.Message}");
		}
	}

	private async Task StartAanbodAsync()
	{
		Sluit(null);

		var ice = await _sig.IceServersAsync(CancellationToken.None);
		var config = new RTCConfiguration
		{
			iceServers = ice.SelectMany(s => s.Urls.Select(u => new RTCIceServer
			{
				urls = u,
				username = s.Username,
				credential = s.Credential,
				credentialType = RTCIceCredentialType.password
			})).ToList()
		};

		var pc = new RTCPeerConnection(config);
		_pc = pc;
		_enc = new VpxVideoEncoder { TargetKbps = 8000 };

		var track = new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.VP8, 96), MediaStreamStatusEnum.SendOnly);
		pc.addTrack(track);

		pc.onicecandidate += c =>
		{
			if (c is null) return;
			_ = _sig.StuurAsync(new { type = "ice", candidate = c.candidate, sdpMid = c.sdpMid, sdpMLineIndex = (int)c.sdpMLineIndex });
		};

		pc.onconnectionstatechange += state =>
		{
			Status?.Invoke(state switch
			{
				RTCPeerConnectionState.connecting => "Verbinden met technicus…",
				RTCPeerConnectionState.connected => "Verbonden. De technicus ziet uw scherm.",
				RTCPeerConnectionState.disconnected => "Verbinding onderbroken…",
				RTCPeerConnectionState.failed => "Verbinding mislukt.",
				RTCPeerConnectionState.closed => "Verbinding gesloten.",
				_ => state.ToString()
			});
			if (state == RTCPeerConnectionState.connected)
			{
				TechnicusVerbonden?.Invoke(true);
				StartStream(pc);
			}
			else if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
			{
				StopStream();
				TechnicusVerbonden?.Invoke(false);
			}
		};

		void OntvangInvoer(byte[] data)
		{
			try
			{
				var bericht = JsonNode.Parse(Encoding.UTF8.GetString(data));
				if (bericht is not null) Invoer.Verwerk(bericht, _opname.Scherm);
			}
			catch { /* ongeldig bericht */ }
		}

		// Betrouwbaar, geordend kanaal voor klikken en toetsen (mogen niet verloren gaan).
		var dc = await pc.createDataChannel("invoer", null);
		_dc = dc;
		dc.onopen += () => dc.send(JsonNode.Parse($$"""{"t":"info","w":{{_opname.Breedte}},"h":{{_opname.Hoogte}}}""")!.ToJsonString());
		dc.onmessage += (_, _, data) => OntvangInvoer(data);

		// Snel kanaal voor muisbewegingen en scrollen: onbetrouwbaar en ongeordend, zodat verouderde
		// bewegingen worden weggegooid in plaats van opgestapeld. Dat houdt de muis direct responsief.
		var dcMuis = await pc.createDataChannel("muis", new RTCDataChannelInit { ordered = false, maxRetransmits = 0 });
		_dcMuis = dcMuis;
		dcMuis.onmessage += (_, _, data) => OntvangInvoer(data);

		var offer = pc.createOffer(null);
		await pc.setLocalDescription(offer);
		await _sig.StuurAsync(new { type = "offer", sdp = offer.sdp });
	}

	private void StartStream(RTCPeerConnection pc)
	{
		StopStream();
		var cts = new CancellationTokenSource();
		_streamCts = cts;
		var enc = _enc!;
		_ = Task.Run(() =>
		{
			var klok = Stopwatch.StartNew();
			var frameDuur = TimeSpan.FromSeconds(1.0 / Fps);
			var volgende = klok.Elapsed;
			var teller = 0;
			while (!cts.IsCancellationRequested)
			{
				try
				{
					var bgra = _opname.Frame();
					// Elke 3 seconden een keyframe, zodat een technicus die frames mist snel weer beeld heeft.
					if (++teller % (Fps * 3) == 0) enc.ForceKeyFrame();
					var gecodeerd = enc.EncodeVideo(_opname.Breedte, _opname.Hoogte, bgra, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.VP8);
					if (gecodeerd is { Length: > 0 }) pc.SendVideo(RtpDuur, gecodeerd);
				}
				catch (Exception ex) when (!cts.IsCancellationRequested)
				{
					Status?.Invoke($"Schermopname: {ex.Message}");
					Thread.Sleep(500);
				}

				volgende += frameDuur;
				var wacht = volgende - klok.Elapsed;
				if (wacht > TimeSpan.Zero) Thread.Sleep(wacht);
				else volgende = klok.Elapsed;
			}
		}, cts.Token);
	}

	private void StopStream()
	{
		lock (_slot)
		{
			_streamCts?.Cancel();
			_streamCts = null;
		}
	}

	/// <summary>Sluit de huidige verbinding (de sessiecode blijft geldig voor een nieuwe technicus).</summary>
	public void Sluit(string? reden)
	{
		StopStream();
		var pc = _pc;
		_pc = null;
		_dc = null;
		_dcMuis = null;
		try { pc?.close(); } catch { /* al gesloten */ }
		_enc?.Dispose();
		_enc = null;
		if (reden is not null) Status?.Invoke(reden);
	}

	public async ValueTask DisposeAsync()
	{
		_sig.BerichtOntvangen -= OnBericht;
		await _sig.StuurAsync(new { type = "einde" });
		Sluit(null);
		_opname.Dispose();
	}
}
