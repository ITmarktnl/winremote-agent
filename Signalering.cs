using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinRemote.Agent;

/// <summary>
/// Verbinding met signaal.winremote.nl: sessiecode ophalen, ICE-servers ophalen en het
/// WebSocket-kanaal waarover offer/answer/ICE met de technicus worden uitgewisseld.
/// </summary>
public sealed class Signalering : IAsyncDisposable
{
	private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
	private ClientWebSocket? _ws;
	private CancellationTokenSource? _cts;

	public string? Code { get; private set; }

	/// <summary>Wordt aangeroepen voor elk JSON-bericht van de technicus (offer, answer, ice, peer, einde…).</summary>
	public event Action<JsonNode>? BerichtOntvangen;

	/// <summary>Verbinding met de signaleringsserver is weggevallen.</summary>
	public event Action<string>? Verbroken;

	public async Task<string> NieuweSessieAsync(CancellationToken ct)
	{
		using var r = await Http.PostAsync($"{Branding.SignaalBasis}/sessie", null, ct);
		r.EnsureSuccessStatusCode();
		var d = await r.Content.ReadFromJsonAsync<JsonNode>(ct);
		Code = d?["code"]?.GetValue<string>() ?? throw new InvalidOperationException("Geen sessiecode ontvangen.");
		return Code;
	}

	/// <summary>
	/// Sluit aan op een code die al bestaat (aangemaakt door de website). Geeft false als de code
	/// onbekend of verlopen is.
	/// </summary>
	public async Task<bool> GebruikCodeAsync(string code, CancellationToken ct)
	{
		using var r = await Http.GetAsync($"{Branding.SignaalBasis}/sessie/{code}", ct);
		if (!r.IsSuccessStatusCode) return false;
		var d = await r.Content.ReadFromJsonAsync<JsonNode>(ct);
		if (d?["bestaat"]?.GetValue<bool>() != true) return false;
		Code = code;
		return true;
	}

	public async Task<List<IceServer>> IceServersAsync(CancellationToken ct)
	{
		var d = await Http.GetFromJsonAsync<JsonNode>($"{Branding.SignaalBasis}/ice", ct);
		var lijst = new List<IceServer>();
		foreach (var s in d?["iceServers"]?.AsArray() ?? [])
		{
			if (s is null) continue;
			var urls = s["urls"] is JsonArray arr
				? arr.Select(u => u!.GetValue<string>()).ToList()
				: [s["urls"]!.GetValue<string>()];
			lijst.Add(new IceServer(urls, s["username"]?.GetValue<string>(), s["credential"]?.GetValue<string>()));
		}
		return lijst;
	}

	public async Task VerbindAsync(CancellationToken ct)
	{
		if (Code is null) throw new InvalidOperationException("Eerst een sessie aanmaken.");
		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_ws = new ClientWebSocket();
		_ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		await _ws.ConnectAsync(new Uri($"{Branding.SignaalWs}/ws/{Code}?rol=agent"), _cts.Token);
		_ = Task.Run(() => OntvangLusAsync(_cts.Token));
	}

	public Task StuurAsync(object bericht)
	{
		if (_ws is not { State: WebSocketState.Open }) return Task.CompletedTask;
		var bytes = JsonSerializer.SerializeToUtf8Bytes(bericht);
		return _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
	}

	private async Task OntvangLusAsync(CancellationToken ct)
	{
		var buffer = new byte[64 * 1024];
		var sb = new StringBuilder();
		try
		{
			while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
			{
				sb.Clear();
				WebSocketReceiveResult r;
				do
				{
					r = await _ws.ReceiveAsync(buffer, ct);
					if (r.MessageType == WebSocketMessageType.Close)
					{
						Verbroken?.Invoke(r.CloseStatusDescription ?? "gesloten");
						return;
					}
					sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
				} while (!r.EndOfMessage);

				try
				{
					var node = JsonNode.Parse(sb.ToString());
					if (node is not null) BerichtOntvangen?.Invoke(node);
				}
				catch (JsonException)
				{
					/* ongeldig bericht negeren */
				}
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			Verbroken?.Invoke(ex.Message);
		}
	}

	public async ValueTask DisposeAsync()
	{
		_cts?.Cancel();
		if (_ws is { State: WebSocketState.Open })
		{
			try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "einde", CancellationToken.None); }
			catch { /* al weg */ }
		}
		_ws?.Dispose();
		_cts?.Dispose();
	}
}

public sealed record IceServer(List<string> Urls, string? Username, string? Credential);
