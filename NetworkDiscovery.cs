using FishNet.Managing;
using FishNet.Managing.Logging;
using FishNet.Transporting;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FishNet.Discovery
{
	public sealed class NetworkDiscovery : MonoBehaviour
	{
		private static readonly byte[] OkBytes = { 1 };

		private NetworkManager _networkManager;

		[SerializeField]
		private string secret;

		private byte[] _secretBytes;

		[SerializeField]
		private ushort port;

		[SerializeField]
		private float searchTimeout;

		[SerializeField]
		private bool automatic;

		private SynchronizationContext _mainThreadSynchronizationContext;

		private CancellationTokenSource _cancellationTokenSource;

		public event Action<IPEndPoint> ServerFoundCallback;

		public bool IsAdvertising { get; private set; }

		public bool IsSearching { get; private set; }

		private void Awake()
		{
			if (TryGetComponent(out _networkManager))
			{
				LogInformation($"Using NetworkManager on {gameObject.name}.");

				_secretBytes = System.Text.Encoding.UTF8.GetBytes(secret);

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

		private void Update()
		{
			if (Input.GetKeyDown(KeyCode.S)) AdvertiseServer();

			if (Input.GetKeyDown(KeyCode.C)) SearchForServers();
		}

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
			if (_networkManager.IsServer) return;

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				StopSearchingOrAdvertising();
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				SearchForServers();
			}
		}

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

		private async Task AdvertiseServerAsync(CancellationToken cancellationToken)
		{
			UdpClient udpClient = null;

			try
			{
				LogInformation("Started advertising server.");

				IsAdvertising = true;

				while (!cancellationToken.IsCancellationRequested)
				{
					if (udpClient == null) udpClient = new UdpClient(port);

					LogInformation("Waiting for request...");

					Task<UdpReceiveResult> receiveTask = udpClient.ReceiveAsync();

					Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(searchTimeout), cancellationToken);

					Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);

					if (completedTask == receiveTask)
					{
						UdpReceiveResult result = receiveTask.Result;

						string receivedSecret = System.Text.Encoding.UTF8.GetString(result.Buffer);

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
				Debug.LogException(exception);
			}
			finally
			{
				IsAdvertising = false;

				Debug.Log("Closing UDP client...");

				udpClient?.Close();
			}
		}

		private async Task SearchForServersAsync(CancellationToken cancellationToken)
		{
			UdpClient udpClient = null;

			try
			{
				LogInformation("Started searching for servers.");

				IsSearching = true;

				IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, port);

				while (!cancellationToken.IsCancellationRequested)
				{
					if (udpClient == null) udpClient = new UdpClient();

					LogInformation("Sending request...");

					await udpClient.SendAsync(_secretBytes, _secretBytes.Length, broadcastEndPoint);

					LogInformation("Waiting for response...");

					Task<UdpReceiveResult> receiveTask = udpClient.ReceiveAsync();

					Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(searchTimeout), cancellationToken);

					Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);

					if (completedTask == receiveTask)
					{
						UdpReceiveResult result = receiveTask.Result;

						if (result.Buffer.Length == 1 && result.Buffer[0] == 1)
						{
							LogInformation($"Received response from {result.RemoteEndPoint}.");

							_mainThreadSynchronizationContext.Post(delegate { ServerFoundCallback?.Invoke(result.RemoteEndPoint); }, null);
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
					Debug.LogException(socketException);
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
			finally
			{
				IsSearching = false;

				udpClient?.Close();
			}
		}

		private void LogInformation(string message)
		{
			if (_networkManager.CanLog(LoggingType.Common)) Debug.Log($"[{nameof(NetworkDiscovery)}] {message}");
		}

		private void LogWarning(string message)
		{
			if (_networkManager.CanLog(LoggingType.Warning)) Debug.LogWarning($"[{nameof(NetworkDiscovery)}] {message}");
		}

		private void LogError(string message)
		{
			if (_networkManager.CanLog(LoggingType.Error)) Debug.LogError($"[{nameof(NetworkDiscovery)}] {message}");
		}
	}
}
