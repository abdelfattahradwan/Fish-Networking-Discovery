using System.Collections.Generic;
using System.Net;
using UnityEngine;

namespace FishNet.Discovery
{
	public sealed class NetworkDiscoveryHUD : MonoBehaviour
	{
		[SerializeField]
		private NetworkDiscovery networkDiscovery;

		private readonly List<IPEndPoint> _endPoints = new List<IPEndPoint>();

		private Vector2 _serversListScrollVector;

		private void Start()
		{
			if (networkDiscovery == null) networkDiscovery = FindObjectOfType<NetworkDiscovery>();

			networkDiscovery.ServerFoundCallback += (endPoint) =>
			{
				if (!_endPoints.Contains(endPoint)) _endPoints.Add(endPoint);
			};
		}

		private void OnGUI()
		{
			var buttonHeight = GUILayout.Height(30.0f);

			using (new GUILayout.AreaScope(new Rect(Screen.width - 240.0f - 10.0f, 10.0f, 240.0f, Screen.height - 20.0f)))
			{
				GUILayout.Box("Server");

				using (new GUILayout.HorizontalScope())
				{
					if (GUILayout.Button("Start", buttonHeight)) InstanceFinder.ServerManager.StartConnection();

					if (GUILayout.Button("Stop", buttonHeight)) InstanceFinder.ServerManager.StopConnection(true);
				}

				GUILayout.Box("Advertising");

				using (new GUILayout.HorizontalScope())
				{
					if (GUILayout.Button("Start", buttonHeight)) networkDiscovery.StartAdvertisingServer();

					if (GUILayout.Button("Stop", buttonHeight)) networkDiscovery.StopAdvertisingServer();
				}

				GUILayout.Box("Searching");

				using (new GUILayout.HorizontalScope())
				{
					if (GUILayout.Button("Start", buttonHeight)) networkDiscovery.StartSearchingForServers();

					if (GUILayout.Button("Stop", buttonHeight)) networkDiscovery.StopSearchingForServers();
				}

				if (_endPoints.Count > 0)
				{
					GUILayout.Box("Servers");

					using (new GUILayout.ScrollViewScope(_serversListScrollVector))
					{
						for (var i = 0; i < _endPoints.Count; i++)
						{
							var ipAddress = _endPoints[i].Address.ToString();

							if (GUILayout.Button(ipAddress))
							{
								networkDiscovery.StopAdvertisingServer();

								networkDiscovery.StopSearchingForServers();

								InstanceFinder.ClientManager.StartConnection(ipAddress);
							}
						}
					}
				}
			}
		}
	}
}
