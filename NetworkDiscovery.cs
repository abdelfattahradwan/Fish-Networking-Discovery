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

				UpdateSecretBytes();

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
			if (!automatic || _networkManager == null) return;

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

			UpdateSecretBytes();
		}

		/// <summary>
		/// Advertises the server on the local network.
		/// </summary>
		public void AdvertiseServer()
		{
			if (!CanStartDiscovery()) return;

			if (IsAdvertising)
			{
				LogWarning("Server is already being advertised.");

				return;
			}

			if (IsSearching)
			{
				LogWarning("Cannot advertise server while searching for servers.");

				return;
			}

			CancellationTokenSource cancellationTokenSource = new();

			_cancellationTokenSource = cancellationTokenSource;

			IsAdvertising = true;

			_ = AdvertiseServerAsync(cancellationTokenSource);
		}

		/// <summary>
		/// Searches for servers on the local network.
		/// </summary>
		public void SearchForServers()
		{
			if (!CanStartDiscovery()) return;

			if (IsSearching)
			{
				LogWarning("Already searching for servers.");

				return;
			}

			if (IsAdvertising)
			{
				LogWarning("Cannot search for servers while advertising server.");

				return;
			}

			CancellationTokenSource cancellationTokenSource = new();

			_cancellationTokenSource = cancellationTokenSource;

			IsSearching = true;

			_ = SearchForServersAsync(cancellationTokenSource);
		}

		/// <summary>
		/// Stops searching or advertising.
		/// </summary>
		public void StopSearchingOrAdvertising()
		{
			_cancellationTokenSource?.Cancel();
		}

		/// <summary>
		/// Advertises the server on the local network.
		/// </summary>
		/// <param name="cancellationTokenSource">Used to cancel advertising.</param>
		private async Task AdvertiseServerAsync(CancellationTokenSource cancellationTokenSource)
		{
			UdpClient udpClient = null;

			Task<UdpReceiveResult> receiveTask = null;

			CancellationToken cancellationToken = cancellationTokenSource.Token;

			try
			{
				LogInformation("Started advertising server.");

				udpClient = new UdpClient();

				#if UNITY_EDITOR

				udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

				#endif

				udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));

				receiveTask = udpClient.ReceiveAsync();

				LogInformation("Waiting for request...");

				while (!cancellationToken.IsCancellationRequested)
				{
					Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(SearchTimeout), cancellationToken);

					Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);

					if (completedTask != receiveTask)
					{
						if (cancellationToken.IsCancellationRequested) break;

						continue;
					}

					UdpReceiveResult result = await receiveTask;

					if (result.Buffer.AsSpan().SequenceEqual(_secretBytes))
					{
						LogInformation($"Received request from {result.RemoteEndPoint}.");

						await udpClient.SendAsync(OkBytes, OkBytes.Length, result.RemoteEndPoint);
					}
					else
					{
						LogWarning($"Received invalid request from {result.RemoteEndPoint}.");
					}

					if (cancellationToken.IsCancellationRequested) break;

					receiveTask = udpClient.ReceiveAsync();

					LogInformation("Waiting for request...");
				}

				LogInformation("Stopped advertising server.");
			}
			catch (OperationCanceledException)
			{
				LogInformation("Stopped advertising server.");
			}
			catch (SocketException socketException)
			{
				if (socketException.SocketErrorCode == SocketError.AddressAlreadyInUse)
				{
					LogError($"Unable to advertise server. Port {port} is already in use.");
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
				if (receiveTask is { IsCompleted: false })
				{
					ObserveTask(receiveTask);
				}

				udpClient?.Close();

				IsAdvertising = false;

				CompleteOperation(cancellationTokenSource);
			}
		}

		/// <summary>
		/// Searches for servers on the local network.
		/// </summary>
		/// <param name="cancellationTokenSource">Used to cancel searching.</param>
		private async Task SearchForServersAsync(CancellationTokenSource cancellationTokenSource)
		{
			UdpClient udpClient = null;

			Task<UdpReceiveResult> receiveTask = null;

			CancellationToken cancellationToken = cancellationTokenSource.Token;

			try
			{
				LogInformation("Started searching for servers.");

				IPEndPoint broadcastEndPoint = new(IPAddress.Broadcast, port);

				while (!cancellationToken.IsCancellationRequested)
				{
					udpClient ??= CreateSearchUdpClient();

					LogInformation("Sending request...");

					await udpClient.SendAsync(_secretBytes, _secretBytes.Length, broadcastEndPoint);

					LogInformation("Waiting for response...");

					receiveTask = udpClient.ReceiveAsync();

					Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(SearchTimeout), cancellationToken);

					Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);

					if (completedTask == receiveTask)
					{
						UdpReceiveResult result = await receiveTask;

						receiveTask = null;

						if (result.Buffer.Length == OkBytes.Length && result.Buffer[0] == OkBytes[0])
						{
							LogInformation($"Received response from {result.RemoteEndPoint}.");

							PostServerFound(result.RemoteEndPoint);
						}
						else
						{
							LogWarning($"Received invalid response from {result.RemoteEndPoint}.");
						}
					}
					else
					{
						if (cancellationToken.IsCancellationRequested) break;

						LogInformation("Timed out. Retrying...");

						ObserveTask(receiveTask);

						receiveTask = null;

						udpClient.Close();

						udpClient = null;
					}
				}

				LogInformation("Stopped searching for servers.");
			}
			catch (OperationCanceledException)
			{
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
				if (receiveTask is { IsCompleted: false })
				{
					ObserveTask(receiveTask);
				}

				udpClient?.Close();

				IsSearching = false;

				CompleteOperation(cancellationTokenSource);
			}
		}

		/// <summary>
		/// Creates a UDP client for searching.
		/// </summary>
		/// <returns>Configured UDP client.</returns>
		private static UdpClient CreateSearchUdpClient()
		{
			UdpClient udpClient = new();

			udpClient.EnableBroadcast = true;

			return udpClient;
		}

		/// <summary>
		/// Completes the active operation if it still owns the cancellation token source.
		/// </summary>
		/// <param name="cancellationTokenSource">Cancellation token source to complete.</param>
		private void CompleteOperation(CancellationTokenSource cancellationTokenSource)
		{
			if (ReferenceEquals(_cancellationTokenSource, cancellationTokenSource)) _cancellationTokenSource = null;

			cancellationTokenSource.Dispose();
		}

		/// <summary>
		/// Updates the byte representation of the secret.
		/// </summary>
		private void UpdateSecretBytes()
		{
			_secretBytes = Encoding.UTF8.GetBytes(secret ?? string.Empty);
		}

		/// <summary>
		/// Returns true if discovery can start.
		/// </summary>
		private bool CanStartDiscovery()
		{
			if (port != 0) return true;

			LogError("Port must be greater than 0.");

			return false;
		}

		/// <summary>
		/// Posts a found server to the main thread if possible.
		/// </summary>
		/// <param name="remoteEndPoint">Server endpoint.</param>
		private void PostServerFound(IPEndPoint remoteEndPoint)
		{
			if (_mainThreadSynchronizationContext != null)
			{
				_mainThreadSynchronizationContext.Post(_ => ServerFoundCallback?.Invoke(remoteEndPoint), null);
			}
			else
			{
				ServerFoundCallback?.Invoke(remoteEndPoint);
			}
		}

		/// <summary>
		/// Observes a faulted task to prevent unobserved task exceptions.
		/// </summary>
		/// <param name="task">Task to observe.</param>
		private static void ObserveTask(Task task)
		{
			if (task.IsCompleted)
			{
				_ = task.Exception;

				return;
			}

			task.ContinueWith(static completedTask => _ = completedTask.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
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
