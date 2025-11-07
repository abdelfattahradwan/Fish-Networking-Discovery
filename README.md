# Fish-Networking-Discovery

A very simple LAN network discovery component for Fish-Networking ([Asset Store](https://assetstore.unity.com/packages/tools/network/fish-net-networking-evolved-207815) | [GitHub](https://github.com/FirstGearGames/FishNet))

### Getting Started (GUI)

1. Download the code in this repo as a zip
2. Extract the code inside your project folder **(FISH-NETWORKING MUST BE ALREADY INSTALLED)**
3. Create an empty game object
4. Add a `NetworkManager` component to the game object you just created
5. Add a `NetworkDiscovery` component to the game object you just created
6. Set the `secret`, `port`, and `searchTimeout` fields
7. Add a `NetworkDiscoveryHud` component
8. Enter play mode
	- If you want to begin advertising a server
		1. Press "Start" under the "Server" group
		2. Press "Start" under the "Advertising" group
	- If you want to stop advertising a server
    	- Press "Stop" under the "Advertising" group
    - If you want to begin searching for servers
    	- Press "Start" under the "Searching" group
	- If you want to stop searching for servers
    	- Press "Stop" under the "Searching" group

### Getting Started (Code)

1. Download the code in this repo as a zip
2. Extract the code inside your project folder **(FISH-NETWORKING MUST BE ALREADY INSTALLED)**
3. Create an empty game object
4. Add a `NetworkManager` component to the game object you just created
5. Add a `NetworkDiscovery` component to the game object you just created
6. Set the `secret`, `port`, and `searchTimeout` fields
7. Enter play mode
	- If you want begin advertising a server
		1. Call `InstanceFinder.ServerManager.StartConnection()`
		2. Call `FindObjectOfType<NetworkDiscovery>().AdvertiseServer()`
  	- If you want to stop advertising a server
    	- Call `FindObjectOfType<NetworkDiscovery>().StopSearchingOrAdvertising()`
  	- If you want to start searching for servers
    	- Call `FindObjectOfType<NetworkDiscovery>().SearchForServers()`
  	- If you want to stop seaching for servers
    	- Call `FindObjectOfType<NetworkDiscovery>().StopSearchingOrAdvertising()`

### Compatibility

- All Unity versions that support .NET Standard 2.0 (or higher) are supported.
- All modern Fish-Networking versions are supported.

### Planned Features

- [x] Automatically start/stop advertising server
- [ ] Automatically remove servers that are no longer alive

### Donating

If you found my project helpful, I would greatly appreciate your support through a [donation](https://ko-fi.com/winterboltgames) to help me continue improving and maintaining it!
