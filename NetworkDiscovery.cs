using FishNet.Managing;
using FishNet.Managing.Logging;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
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

		/// <summary>
		/// This list stores all the found <see cref="IPEndPoint"/>s that have been discovered by the worker thread to be processed by the main thread in the Update() function.
		/// </summary>
		private List<IPEndPoint> foundIPEndPoints = new();

		/// <summary>
		/// This list stores all the previously found <see cref="IPEndPoint"/>s, so that they are not considered as "found" again when a new network discovery is run.
		/// </summary>
		private List<IPEndPoint> previouslyFoundIPEndPoints = new();

		private void Start()
		{
			if (automatic)
			{
				InstanceFinder.ServerManager.OnServerConnectionState += ServerConnectionStateChangedHandler;

				InstanceFinder.ClientManager.OnClientConnectionState += ClientConnectionStateChangedHandler;

				StartSearchingForServers();
			}
		}

		private void Update() {
			// See if we have found any IPEndPoints, and if we have, invoke the ServerFoundCallback event.
			if (foundIPEndPoints.Count > 0) {
				// Invoke the ServerFoundCallback with every IP address in the list.
				foreach (IPEndPoint foundIPEndPoint in foundIPEndPoints) {
					// Compare the address of the found endpoint with every previously found endpoint. If it matches any, do not invoke the ServerFoundCallback for it.
					bool previouslyFound = false;

					foreach (IPEndPoint previouslyFoundEndPoint in previouslyFoundIPEndPoints) {
						if (previouslyFoundEndPoint.Address.ToString() == foundIPEndPoint.Address.ToString()) {
                            previouslyFound = true;
							break;
						}
					}

					if (previouslyFound) {
						continue;
					}
					else {
						ServerFoundCallback?.Invoke(foundIPEndPoint);

						previouslyFoundIPEndPoints.Add(foundIPEndPoint);
					}
				}

				// Now clear all the IP addresses.
				foundIPEndPoints.Clear();
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
			if (args.ConnectionState == LocalConnectionState.Starting)
			{
				StopSearchingForServers();
			}
			else if (args.ConnectionState == LocalConnectionState.Started)
			{
				StartAdvertisingServer();
			}
			else if (args.ConnectionState == LocalConnectionState.Stopping)
			{
				StopAdvertisingServer();
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				StartSearchingForServers();
			}
		}

		private void ClientConnectionStateChangedHandler(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Starting)
			{
				StopSearchingForServers();
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
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

			previouslyFoundIPEndPoints.Clear();

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

			previouslyFoundIPEndPoints.Clear();

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
					if (!foundIPEndPoints.Contains(result.RemoteEndPoint)) {
						foundIPEndPoints.Add(result.RemoteEndPoint);
					}
				}
			}
		}

		#endregion
	}
}
