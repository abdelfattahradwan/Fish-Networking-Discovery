using FishNet.Managing;
using FishNet.Managing.Logging;
using FishNet.Transporting;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace FishNet.Discovery
{
	/// <summary>
	/// A component that advertises a server or searches for servers.
	/// </summary>
	public sealed class NetworkDiscovery : MonoBehaviour
	{
		/// <summary>
		/// A string that differentiates your application/game from others.
		/// <b>Must not</b> be null, empty, or blank.
		/// </summary>
		[SerializeField]
		[Tooltip("A string that differentiates your application/game from others. Must not be null, empty, or blank.")]
		private string secret;

		/// <summary>
		/// The port number used by this <see cref="NetworkDiscovery"/> component.
		/// <b>Must</b> be different from the one used by the <seealso cref="Transport"/>.
		/// </summary>
		[SerializeField]
		[Tooltip("The port number used by this NetworkDiscovery component. Must be different from the one used by the Transport.")]
		private ushort port;

		/// <summary>
		/// How often does this <see cref="NetworkDiscovery"/> component advertises a server or searches for servers.
		/// </summary>
		[SerializeField]
		[Tooltip("How often does this NetworkDiscovery component advertises a server or searches for servers.")]
		private float discoveryInterval;

		/// <summary>
		/// Whether this <see cref="NetworkDiscovery"/> component will automatically start/stop? <b>Setting this to true is recommended.</b>
		/// </summary>
		[SerializeField]
		[Tooltip("Whether this NetworkDiscovery component will automatically start/stop? Setting this to true is recommended.")]
		private bool automatic;

		/// <summary>
		/// The <see cref="UdpClient"/> used to advertise the server.
		/// </summary>
		private UdpClient _serverUdpClient;

		/// <summary>
		/// The <see cref="UdpClient"/> used to search for servers.
		/// </summary>
		private UdpClient _clientUdpClient;

		/// <summary>
		/// Whether this <see cref="NetworkDiscovery"/> component is currently advertising a server or not.
		/// </summary>
		public bool IsAdvertising => _serverUdpClient != null;

		/// <summary>
		/// Whether this <see cref="NetworkDiscovery"/> component is currently searching for servers or not.
		/// </summary>
		public bool IsSearching => _clientUdpClient != null;

		/// <summary>
		/// An <see cref="Action"/> that is invoked by this <seealso cref="NetworkDiscovery"/> component whenever a server is found.
		/// </summary>
		public event Action<IPEndPoint> ServerFoundCallback;

		private void Start()
		{
			if (automatic)
			{
				InstanceFinder.ServerManager.OnServerConnectionState += ServerConnectionStateChangedHandler;

				InstanceFinder.ClientManager.OnClientConnectionState += ClientConnectionStateChangedHandler;

				StartSearchingForServers();
			}
		}

		private void OnDisable()
		{
			InstanceFinder.ServerManager.OnServerConnectionState -= ServerConnectionStateChangedHandler;

			InstanceFinder.ClientManager.OnClientConnectionState -= ClientConnectionStateChangedHandler;

			StopAdvertisingServer();

			StopSearchingForServers();
		}

		private void OnDestroy()
		{
			InstanceFinder.ServerManager.OnServerConnectionState -= ServerConnectionStateChangedHandler;

			InstanceFinder.ClientManager.OnClientConnectionState -= ClientConnectionStateChangedHandler;

			StopAdvertisingServer();

			StopSearchingForServers();
		}

		private void OnApplicationQuit()
		{
			InstanceFinder.ServerManager.OnServerConnectionState -= ServerConnectionStateChangedHandler;

			InstanceFinder.ClientManager.OnClientConnectionState -= ClientConnectionStateChangedHandler;

			StopAdvertisingServer();

			StopSearchingForServers();
		}

		#region Connection State Handlers

		private void ServerConnectionStateChangedHandler(ServerConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionStates.Starting)
			{
				StopSearchingForServers();
			}
			else if (args.ConnectionState == LocalConnectionStates.Started)
			{
				StartAdvertisingServer();
			}
			else if (args.ConnectionState == LocalConnectionStates.Stopping)
			{
				StopAdvertisingServer();
			}
			else if (args.ConnectionState == LocalConnectionStates.Stopped)
			{
				StartSearchingForServers();
			}
		}

		private void ClientConnectionStateChangedHandler(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionStates.Starting)
			{
				StopSearchingForServers();
			}
			else if (args.ConnectionState == LocalConnectionStates.Stopped)
			{
				StartSearchingForServers();
			}
		}

		#endregion

		#region Server

		/// <summary>
		/// Makes this <see cref="NetworkDiscovery"/> component start advertising a server.
		/// </summary>
		public void StartAdvertisingServer()
		{
			if (!InstanceFinder.IsServer)
			{
				if (NetworkManager.StaticCanLog(LoggingType.Warning)) Debug.LogWarning("Unable to start advertising server. Server is inactive.", this);

				return;
			}

			if (_serverUdpClient != null)
			{
				if (NetworkManager.StaticCanLog(LoggingType.Common)) Debug.Log("Server is already being advertised.", this);

				return;
			}

			if (port == InstanceFinder.TransportManager.Transport.GetPort())
			{
				if (NetworkManager.StaticCanLog(LoggingType.Warning)) Debug.LogWarning("Unable to start advertising server on the same port as the transport.", this);

				return;
			}

			_serverUdpClient = new UdpClient(port)
			{
				EnableBroadcast = true,
				MulticastLoopback = false,
			};

			Task.Run(AdvertiseServerAsync);

			if (NetworkManager.StaticCanLog(LoggingType.Common)) Debug.Log("Started advertising server.", this);
		}

		/// <summary>
		/// Makes this <see cref="NetworkDiscovery"/> component <i>immediately</i> stop advertising the server it is currently advertising.
		/// </summary>
		public void StopAdvertisingServer()
		{
			if (_serverUdpClient == null) return;

			_serverUdpClient.Close();

			_serverUdpClient = null;

			if (NetworkManager.StaticCanLog(LoggingType.Common)) Debug.Log("Stopped advertising server.", this);
		}

		private async void AdvertiseServerAsync()
		{
			while (_serverUdpClient != null)
			{
				await Task.Delay(TimeSpan.FromSeconds(discoveryInterval));

				UdpReceiveResult result = await _serverUdpClient.ReceiveAsync();

				string receivedSecret = Encoding.UTF8.GetString(result.Buffer);

				if (receivedSecret == secret)
				{
					byte[] okBytes = BitConverter.GetBytes(true);

					await _serverUdpClient.SendAsync(okBytes, okBytes.Length, result.RemoteEndPoint);
				}
			}
		}

		#endregion

		#region Client

		/// <summary>
		/// Makes this <see cref="NetworkDiscovery"/> component start searching for servers.
		/// </summary>
		public void StartSearchingForServers()
		{
			if (InstanceFinder.IsServer)
			{
				if (NetworkManager.StaticCanLog(LoggingType.Warning)) Debug.LogWarning("Unable to start searching for servers. Server is active.", this);

				return;
			}

			if (InstanceFinder.IsClient)
			{
				if (NetworkManager.StaticCanLog(LoggingType.Warning)) Debug.LogWarning("Unable to start searching for servers. Client is active.", this);

				return;
			}

			if (_clientUdpClient != null)
			{
				if (NetworkManager.StaticCanLog(LoggingType.Common)) Debug.Log("Already searching for servers.", this);

				return;
			}

			_clientUdpClient = new UdpClient()
			{
				EnableBroadcast = true,
				MulticastLoopback = false,
			};

			Task.Run(SearchForServersAsync);

			if (NetworkManager.StaticCanLog(LoggingType.Common)) Debug.Log("Started searching for servers.", this);
		}

		/// <summary>
		/// Makes this <see cref="NetworkDiscovery"/> component <i>immediately</i> stop searching for servers.
		/// </summary>
		public void StopSearchingForServers()
		{
			if (_clientUdpClient == null) return;

			_clientUdpClient.Close();

			_clientUdpClient = null;

			if (NetworkManager.StaticCanLog(LoggingType.Common)) Debug.Log("Stopped searching for servers.", this);
		}

		private async void SearchForServersAsync()
		{
			byte[] secretBytes = Encoding.UTF8.GetBytes(secret);

			IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, port);

			while (_clientUdpClient != null)
			{
				await Task.Delay(TimeSpan.FromSeconds(discoveryInterval));

				await _clientUdpClient.SendAsync(secretBytes, secretBytes.Length, endPoint);

				UdpReceiveResult result = await _clientUdpClient.ReceiveAsync();

				if (BitConverter.ToBoolean(result.Buffer, 0))
				{
					ServerFoundCallback?.Invoke(result.RemoteEndPoint);

					StopSearchingForServers();
				}
			}
		}

		#endregion
	}
}
