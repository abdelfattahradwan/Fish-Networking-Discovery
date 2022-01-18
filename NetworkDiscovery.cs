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
	public sealed class NetworkDiscovery : MonoBehaviour
	{
		[SerializeField]
		private string secret;

		[SerializeField]
		private ushort port;

		[SerializeField]
		private float discoveryInterval;

		[SerializeField]
		private bool automatic;

		private UdpClient _serverUdpClient;
		private UdpClient _clientUdpClient;

		public bool IsAdvertising => _serverUdpClient != null;

		public bool IsSearching => _clientUdpClient != null;

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

			_serverUdpClient = new UdpClient(port)
			{
				EnableBroadcast = true,
				MulticastLoopback = false,
			};

			Task.Run(AdvertiseServerAsync);

			if (NetworkManager.StaticCanLog(LoggingType.Common)) Debug.Log("Started advertising server.", this);
		}

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

				var result = await _serverUdpClient.ReceiveAsync();

				var receivedSecret = Encoding.UTF8.GetString(result.Buffer);

				if (receivedSecret == secret)
				{
					var okBytes = BitConverter.GetBytes(true);

					await _serverUdpClient.SendAsync(okBytes, okBytes.Length, result.RemoteEndPoint);
				}
			}
		}

		#endregion

		#region Client

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

		public void StopSearchingForServers()
		{
			if (_clientUdpClient == null) return;

			_clientUdpClient.Close();

			_clientUdpClient = null;

			if (NetworkManager.StaticCanLog(LoggingType.Common)) Debug.Log("Stopped searching for servers.", this);
		}

		private async void SearchForServersAsync()
		{
			var secretBytes = Encoding.UTF8.GetBytes(secret);

			var endPoint = new IPEndPoint(IPAddress.Broadcast, port);

			while (_clientUdpClient != null)
			{
				await Task.Delay(TimeSpan.FromSeconds(discoveryInterval));

				await _clientUdpClient.SendAsync(secretBytes, secretBytes.Length, endPoint);

				var result = await _clientUdpClient.ReceiveAsync();

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
