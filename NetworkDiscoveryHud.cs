using System.Collections.Generic;
using UnityEngine;

namespace FishNet.Discovery
{
	/// <summary>
	/// A simple UI for NetworkDiscovery.
	/// </summary>
	public sealed class NetworkDiscoveryHud : MonoBehaviour
	{
		/// <summary>
		/// NetworkDiscovery to use. If not set will attempt to find one.
		/// </summary>
		[SerializeField]
		[Tooltip("NetworkDiscovery to use. If not set will attempt to find one.")]
		private NetworkDiscovery networkDiscovery;

		/// <summary>
		/// A collection of server addresses discovered on the local network.
		/// </summary>
		private readonly HashSet<string> _addresses = new();

		/// <summary>
		/// Vector2 used to manage the scroll position of the servers list in the UI.
		/// </summary>
		private Vector2 _serversListScrollVector;

		private void Start()
		{
			if (networkDiscovery == null || !TryGetComponent(out networkDiscovery)) networkDiscovery = FindAnyObjectByType<NetworkDiscovery>();

			networkDiscovery.ServerFoundCallback += endPoint => _addresses.Add(endPoint.Address.ToString());
		}

		private void OnGUI()
		{
			GUILayoutOption buttonHeight = GUILayout.Height(30.0f);

			GUILayout.BeginArea(new Rect(Screen.width - 240.0f - 10.0f, 10.0f, 240.0f, Screen.height - 20.0f));

			GUILayout.Box("Server");

			GUILayout.BeginHorizontal();

			if (GUILayout.Button("Start", buttonHeight)) InstanceFinder.ServerManager.StartConnection();

			if (GUILayout.Button("Stop", buttonHeight)) InstanceFinder.ServerManager.StopConnection(true);

			GUILayout.EndHorizontal();

			GUILayout.Box("Advertising");

			GUILayout.BeginHorizontal();

			if (networkDiscovery.IsAdvertising)
			{
				if (GUILayout.Button("Stop", buttonHeight)) networkDiscovery.StopSearchingOrAdvertising();
			}
			else
			{
				if (GUILayout.Button("Start", buttonHeight)) networkDiscovery.AdvertiseServer();
			}

			GUILayout.EndHorizontal();

			GUILayout.Box("Searching");

			GUILayout.BeginHorizontal();

			if (networkDiscovery.IsSearching)
			{
				if (GUILayout.Button("Stop", buttonHeight)) networkDiscovery.StopSearchingOrAdvertising();
			}
			else
			{
				if (GUILayout.Button("Start", buttonHeight)) networkDiscovery.SearchForServers();
			}

			GUILayout.EndHorizontal();

			if (_addresses.Count < 1)
			{
				GUILayout.EndArea();

				return;
			}

			GUILayout.Box("Servers");

			_serversListScrollVector = GUILayout.BeginScrollView(_serversListScrollVector);

			foreach (string address in _addresses)
			{
				if (GUILayout.Button(address))
				{
					networkDiscovery.StopSearchingOrAdvertising();

					InstanceFinder.ClientManager.StartConnection(address);
				}
			}

			GUILayout.EndScrollView();

			GUILayout.EndArea();
		}
	}
}
