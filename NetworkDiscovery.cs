using FishNet.Managing;
using FishNet.Managing.Logging;
using FishNet.Transporting;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FishNet.Discovery
{
	/// <summary>
	/// Allows clients to find servers on the local network.
	/// </summary>
	public sealed class NetworkDiscovery : MonoBehaviour
	{
		/// <summary>
		/// Used to send a response to a client.
		/// </summary>
		private static readonly byte[] OkBytes = { 1 };

		/// <summary>
		/// The <see cref="FishNet.Managing.NetworkManager"/> to use.
		/// </summary>
		private NetworkManager _networkManager;

		/// <summary>
		/// The secret to use when advertising or searching for servers.
		/// </summary>
		[SerializeField]
		[Tooltip("Secret to use when advertising or searching for servers.")]
		private string secret;

		/// <summary>
		/// A byte-representation of the secret to use when advertising or searching for servers.
		/// </summary>
		private byte[] _secretBytes;

		/// <summary>
		/// Port to use when advertising or searching for servers.
		/// </summary>
		[SerializeField]
		[Tooltip("Port to use when advertising or searching for servers.")]
		private ushort port;

		/// <summary>
		/// How long (in seconds) to wait for a response when advertising or searching for servers.
		/// </summary>
		[SerializeField]
		[Tooltip("How long (in seconds) to wait for a response when advertising or searching for servers.")]
		private float searchTimeout;

		/// <summary>
		/// If true, will automatically start advertising or searching for servers when the NetworkManager starts or stops.
		/// </summary>
		[SerializeField]
		[Tooltip("If true, will automatically start advertising or searching for servers when the NetworkManager starts or stops.")]
		private bool automatic;

		/// <summary>
		/// The synchronizationContext of the main thread.
		/// </summary>
		private SynchronizationContext _mainThreadSynchronizationContext;

		/// <summary>
		/// Used to cancel the search or advertising.
		/// </summary>
		private CancellationTokenSource _cancellationTokenSource;

		/// <summary>
		/// Called when a server is found.
		/// </summary>
		public event Action<IPEndPoint> ServerFoundCallback;

		/// <summary>
		/// True if the server is being advertised.
		/// </summary>
		public bool IsAdvertising { get; private set; }

		/// <summary>
		/// True if the client is searching for servers.
		/// </summary>
		public bool IsSearching { get; private set; }

		/// <summary>
		/// How long (in seconds) to wait for a response when advertising or searching for servers.
		/// </summary>
		private float SearchTimeout
		{
			get => searchTimeout < 1.0f ? 1.0f : searchTimeout;
		}

		private void Awake()
		{
			if (TryGetComponent(out _networkManager))
			{
				LogInformation($"Using NetworkManager on {gameObject.name}.");

				_secretBytes = Encoding.UTF8.GetBytes(secret);

				_mainThreadSynchronizationContext = SynchronizationContext.Current;
			}
			else
			{
				LogError($"No NetworkManager found on {gameObject.name}. Component will be disabled.");

				enabled = false;
			}
		}

		private void OnEnable()
		{
			if (!automatic) return;

			_networkManager.ServerManager.OnServerConnectionState += ServerConnectionStateChangedEventHandler;

			_networkManager.ClientManager.OnClientConnectionState += ClientConnectionStateChangedEventHandler;
		}

		private void OnDisable()
		{
			Shutdown();
		}

		private void OnDestroy()
		{
			Shutdown();
		}

		private void OnApplicationQuit()
		{
			Shutdown();
		}

		/// <summary>
		/// Shuts the NetworkDiscovery.
		/// </summary>
		private void Shutdown()
		{
			if (_networkManager != null)
			{
				_networkManager.ServerManager.OnServerConnectionState -= ServerConnectionStateChangedEventHandler;

				_networkManager.ClientManager.OnClientConnectionState -= ClientConnectionStateChangedEventHandler;
			}

			StopSearchingOrAdvertising();
		}

		private void ServerConnectionStateChangedEventHandler(ServerConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				AdvertiseServer();
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				StopSearchingOrAdvertising();
			}
		}

		private void ClientConnectionStateChangedEventHandler(ClientConnectionStateArgs args)
		{
			if (_networkManager.IsServerStarted) return;

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				StopSearchingOrAdvertising();
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				SearchForServers();
			}
		}

		/// <summary>
		/// Updates the secret.
		/// </summary>
		/// <param name="newSecret">New secret.</param>
		public void UpdateSecret(string newSecret)
		{
			if (secret == newSecret) return;

			secret = newSecret;

			_secretBytes = Encoding.UTF8.GetBytes(secret);
		}

		/// <summary>
		/// Advertises the server on the local network.
		/// </summary>
		public void AdvertiseServer()
		{
			if (IsAdvertising)
			{
				LogWarning("Server is already being advertised.");

				return;
			}

			_cancellationTokenSource = new CancellationTokenSource();

			AdvertiseServerAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
		}

		/// <summary>
		/// Searches for servers on the local network.
		/// </summary>
		public void SearchForServers()
		{
			if (IsSearching)
			{
				LogWarning("Already searching for servers.");

				return;
			}

			_cancellationTokenSource = new CancellationTokenSource();

			SearchForServersAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
		}

		/// <summary>
		/// Stops searching or advertising.
		/// </summary>
		public void StopSearchingOrAdvertising()
		{
			if (_cancellationTokenSource == null)
			{
				LogWarning("Not searching or advertising.");

				return;
			}

			_cancellationTokenSource.Cancel();

			_cancellationTokenSource.Dispose();

			_cancellationTokenSource = null;
		}

		/// <summary>
		/// Advertises the server on the local network.
		/// </summary>
		/// <param name="cancellationToken">Used to cancel advertising.</param>
		private async Task AdvertiseServerAsync(CancellationToken cancellationToken)
		{
			UdpClient udpClient = null;

			try
			{
				LogInformation("Started advertising server.");

				IsAdvertising = true;

				while (!cancellationToken.IsCancellationRequested)
				{
					udpClient ??= new UdpClient(port);

					LogInformation("Waiting for request...");

					Task<UdpReceiveResult> receiveTask = udpClient.ReceiveAsync();

					Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(SearchTimeout), cancellationToken);

					Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);

					if (completedTask == receiveTask)
					{
						UdpReceiveResult result = receiveTask.Result;

						string receivedSecret = Encoding.UTF8.GetString(result.Buffer);

						if (receivedSecret == secret)
						{
							LogInformation($"Received request from {result.RemoteEndPoint}.");

							await udpClient.SendAsync(OkBytes, OkBytes.Length, result.RemoteEndPoint);
						}
						else
						{
							LogWarning($"Received invalid request from {result.RemoteEndPoint}.");
						}
					}
					else
					{
						LogInformation("Timed out. Retrying...");

						udpClient.Close();

						udpClient = null;
					}
				}

				LogInformation("Stopped advertising server.");
			}
			catch (Exception exception)
			{
				Debug.LogException(exception, this);
			}
			finally
			{
				IsAdvertising = false;

				LogInformation("Closing UDP client...");

				udpClient?.Close();
			}
		}

		/// <summary>
		/// Searches for servers on the local network.
		/// </summary>
		/// <param name="cancellationToken">Used to cancel searching.</param>
		private async Task SearchForServersAsync(CancellationToken cancellationToken)
		{
			UdpClient udpClient = null;

			try
			{
				LogInformation("Started searching for servers.");

				IsSearching = true;

				IPEndPoint broadcastEndPoint = new(IPAddress.Broadcast, port);

				while (!cancellationToken.IsCancellationRequested)
				{
					udpClient ??= new UdpClient();

					LogInformation("Sending request...");

					await udpClient.SendAsync(_secretBytes, _secretBytes.Length, broadcastEndPoint);

					LogInformation("Waiting for response...");

					Task<UdpReceiveResult> receiveTask = udpClient.ReceiveAsync();

					Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(SearchTimeout), cancellationToken);

					Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);

					if (completedTask == receiveTask)
					{
						UdpReceiveResult result = receiveTask.Result;

						if (result.Buffer.Length == 1 && result.Buffer[0] == 1)
						{
							LogInformation($"Received response from {result.RemoteEndPoint}.");

							_mainThreadSynchronizationContext.Post(_ => ServerFoundCallback?.Invoke(result.RemoteEndPoint), null);
						}
						else
						{
							LogWarning($"Received invalid response from {result.RemoteEndPoint}.");
						}
					}
					else
					{
						LogInformation("Timed out. Retrying...");

						udpClient.Close();

						udpClient = null;
					}
				}

				LogInformation("Stopped searching for servers.");
			}
			catch (SocketException socketException)
			{
				if (socketException.SocketErrorCode == SocketError.AddressAlreadyInUse)
				{
					LogError($"Unable to search for servers. Port {port} is already in use.");
				}
				else
				{
					Debug.LogException(socketException, this);
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception, this);
			}
			finally
			{
				IsSearching = false;

				udpClient?.Close();
			}
		}

		/// <summary>
		/// Logs a message if the NetworkManager can log.
		/// </summary>
		/// <param name="message">Message to log.</param>
		private void LogInformation(string message)
		{
			if (NetworkManagerExtensions.CanLog(LoggingType.Common)) Debug.Log($"[{nameof(NetworkDiscovery)}] {message}", this);
		}

		/// <summary>
		/// Logs a warning if the NetworkManager can log.
		/// </summary>
		/// <param name="message">Message to log.</param>
		private void LogWarning(string message)
		{
			if (NetworkManagerExtensions.CanLog(LoggingType.Warning)) Debug.LogWarning($"[{nameof(NetworkDiscovery)}] {message}", this);
		}

		/// <summary>
		/// Logs an error if the NetworkManager can log.
		/// </summary>
		/// <param name="message">Message to log.</param>
		private void LogError(string message)
		{
			if (NetworkManagerExtensions.CanLog(LoggingType.Error)) Debug.LogError($"[{nameof(NetworkDiscovery)}] {message}", this);
		}
	}
}
