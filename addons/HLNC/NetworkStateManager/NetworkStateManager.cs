using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HLNC
{
	public partial class NetworkStateManager : Node, IGlobalNetworkState
	{
		static byte MAX_NETWORK_NODES = 64;

		// A bit list of all nodes in use by each peer
		// For example, 0 0 0 0 (... etc ...) 0 1 0 1 would mean that the first and third nodes are in use
		private Dictionary<PeerId, long> availablePeerNodes = new Dictionary<PeerId, long>();
		private Dictionary<PeerId, Dictionary<NetworkId, byte>> globalNodeToLocalNodeMap = new Dictionary<PeerId, Dictionary<NetworkId, byte>>();
		private Dictionary<PeerId, Dictionary<byte, NetworkId>> localNodeToGlobalNodeMap = new Dictionary<PeerId, Dictionary<byte, NetworkId>>();
		// Track the last tick number that we received an acknowledgement for each player
		public Dictionary<PeerId, SYNC_STATE> PeerSyncState = new Dictionary<PeerId, SYNC_STATE>();
		public Tick CurrentTick => NetworkRunner.Instance.CurrentTick;
		public PeerId LocalPlayerId => NetworkRunner.Instance.LocalPlayerId;
		public Node CurrentScene => GetTree().CurrentScene;

		public NetworkNode3D GetNetworkNode(NetworkId networkId)
		{
			if (NetworkRunner.Instance.NetworkNodes.ContainsKey(networkId))
			{
				return NetworkRunner.Instance.NetworkNodes[networkId];
			}
			return null;
		}
		public byte GetPeerNodeId(PeerId peer, NetworkNode3D node)
		{
			if (!globalNodeToLocalNodeMap.ContainsKey(peer))
			{
				return 0;
			}
			if (!globalNodeToLocalNodeMap[peer].ContainsKey(node.NetworkId))
			{
				return 0;
			}
			return globalNodeToLocalNodeMap[peer][node.NetworkId];
		}

		public NetworkNode3D GetPeerNode(PeerId peer, byte networkId)
		{
			if (!localNodeToGlobalNodeMap.ContainsKey(peer))
			{
				return null;
			}
			if (!localNodeToGlobalNodeMap[peer].ContainsKey(networkId))
			{
				return null;
			}
			return NetworkRunner.Instance.NetworkNodes[localNodeToGlobalNodeMap[peer][networkId]];
		}

		public void DeregisterPeerNode(NetworkNode3D node, PeerId? peer = null) {
			if (NetworkRunner.Instance.IsServer) {
				if (!peer.HasValue)
				{
					GD.PrintErr("Server must specify a peer when deregistering a node.");
					return;
				}
				if (globalNodeToLocalNodeMap[peer.Value].ContainsKey(node.NetworkId)) {
					availablePeerNodes[peer.Value] &= ~(1 << globalNodeToLocalNodeMap[peer.Value][node.NetworkId]);
					globalNodeToLocalNodeMap[peer.Value].Remove(node.NetworkId);
				}
			} else {
				NetworkRunner.Instance.NetworkNodes.Remove(node.NetworkId);
			}
		}

		public byte TryRegisterPeerNode(NetworkNode3D node, PeerId? peer = null)
		{
			if (NetworkRunner.Instance.IsServer)
			{
				if (!peer.HasValue)
				{
					GD.PrintErr("Server must specify a peer when registering a node.");
					return 0;
				}
				if (globalNodeToLocalNodeMap[peer.Value].ContainsKey(node.NetworkId))
				{
					return globalNodeToLocalNodeMap[peer.Value][node.NetworkId];
				}
				for (byte i = 0; i < MAX_NETWORK_NODES; i++)
				{
					byte localNodeId = (byte)(i+1);
					if ((availablePeerNodes[peer.Value] & ((long)1 << localNodeId)) == 0)
					{
						globalNodeToLocalNodeMap[peer.Value][node.NetworkId] = localNodeId;
						localNodeToGlobalNodeMap[peer.Value][localNodeId] = node.NetworkId;
						availablePeerNodes[peer.Value] |= (long)1 << localNodeId;
						return localNodeId;
					}
				}

				GD.PrintErr("Peer " + peer.Value + " has reached the maximum amount of nodes.");
				return 0;
			}

			if (NetworkRunner.Instance.NetworkNodes.ContainsKey(node.NetworkId))
			{
				return 0;
			}

			NetworkRunner.Instance.NetworkNodes[node.NetworkId] = node;
			return 1;
		}

		public bool is_loading = true;
		public enum SYNC_STATE
		{
			INITIAL,
			LOADING,
			READY
		}

		private static NetworkStateManager _instance;
		public static NetworkStateManager Instance => _instance;
		public override void _EnterTree()
		{
			if (_instance != null)
			{
				QueueFree();
			}
			_instance = this;
		}

		[Signal]
		public delegate void PlayerJoinedEventHandler(long peerId);

		public void RegisterPlayer(long peerId)
		{
			PeerSyncState[peerId] = SYNC_STATE.INITIAL;
			globalNodeToLocalNodeMap[peerId] = new Dictionary<NetworkId, byte>();
			localNodeToGlobalNodeMap[peerId] = new Dictionary<byte, NetworkId>();
			availablePeerNodes[peerId] = 0;
		}

		public Dictionary<PeerId, HLBuffer> ExportState(int[] peers, Tick currentTick)
		{
			Dictionary<PeerId, HLBuffer> peerBuffers = new Dictionary<PeerId, HLBuffer>();
			foreach (PeerId peerId in peers)
			{
				long updatedNodes = 0;
				peerBuffers[peerId] = new HLBuffer();
				var peerNodesBuffers = new Dictionary<long, HLBuffer>();
				var peerNodesSerializersList = new Dictionary<long, byte>();
				foreach (var node in NetworkRunner.Instance.NetworkNodes.Values)
				{
					var serializersBuffer = new HLBuffer();
					byte serializersRun = 0;
					for (var serializerIdx = 0; serializerIdx < node.Serializers.Count; serializerIdx++)
					{
						var serializer = node.Serializers[serializerIdx];
						var serializerResult = serializer.Export(this, peerId);
						if (serializerResult.bytes.Length == 0)
						{
							continue;
						}
						serializersRun |= (byte)(1 << serializerIdx);
						HLBytes.Pack(serializersBuffer, serializerResult.bytes);
					}
					if (serializersRun == 0)
					{
						continue;
					}
					// if (!globalNodeToLocalNodeMap[peerId].TryGetValue(node.NetworkId, out byte localNodeId))
					// {
					// 	continue;
					// }
					byte localNodeId = globalNodeToLocalNodeMap[peerId][node.NetworkId];
					updatedNodes |= localNodeId;
					peerNodesSerializersList[localNodeId] = serializersRun;
					peerNodesBuffers[localNodeId] = new HLBuffer();
					HLBytes.Pack(peerNodesBuffers[localNodeId], serializersBuffer.bytes);
				}

				// 1. Pack a bit list of all nodes which have serialized data
				HLBytes.Pack(peerBuffers[peerId], updatedNodes);

				// 2. Pack what serializers are run for every node 
				var orderedNodeKeys = peerNodesBuffers.OrderBy(x => x.Key).Select(x => x.Key).ToList();
				foreach (var nodeKey in orderedNodeKeys)
				{
					HLBytes.Pack(peerBuffers[peerId], peerNodesSerializersList[nodeKey]);
				}

				// 3. Pack the serialized data for every node
				foreach (var nodeKey in orderedNodeKeys)
				{
					HLBytes.Pack(peerBuffers[peerId], peerNodesBuffers[nodeKey].bytes);
				}
			}

			foreach (var node in NetworkRunner.Instance.NetworkNodes.Values)
			{
				// Finally, cleanup serializers
				foreach (var serializer in node.Serializers)
				{
					serializer.Cleanup();
				}
			} 

			return peerBuffers;
		}

		public void ImportState(HLBuffer stateBytes)
		{
			var affectedNodes = HLBytes.UnpackInt64(stateBytes);
			var seralizers = new Dictionary<byte, byte>();
			for (byte i = 0; i < MAX_NETWORK_NODES; i++)
			{
				if ((affectedNodes & ((long)1 << i)) == 0)
				{
					continue;
				}
				var serializersRun = HLBytes.UnpackInt8(stateBytes);
				seralizers[i] = serializersRun;
			}

			foreach (var serializer in seralizers)
			{
				var localNodeId = serializer.Key + 1;
				NetworkNode3D node;
				NetworkRunner.Instance.NetworkNodes.TryGetValue(localNodeId, out node);
				if (node == null) {
                    node = new NetworkNode3D
                    {
                        NetworkId = localNodeId
                    };
                }
				for (var serializerIdx = 0; serializerIdx < node.Serializers.Count; serializerIdx++)
				{
					if ((serializer.Value & ((long)1 << serializerIdx)) == 0)
					{
						continue;
					}
					var serializerInstance = node.Serializers[serializerIdx];
					NetworkNode3D nodeOut;
					serializerInstance.Import(this, stateBytes, out nodeOut);
					if (node != nodeOut) {
						// The node has been replaced.
						node = nodeOut;
						serializerIdx = 0;
					}
				}
			}
		}

		public void PeerAcknowledge(PeerId peerId, Tick tick)
		{
			foreach (var node in NetworkRunner.Instance.NetworkNodes.Values)
			{
				for (var serializerIdx = 0; serializerIdx < node.Serializers.Count; serializerIdx++)
				{
					var serializer = node.Serializers[serializerIdx];
					serializer.Acknowledge(this, peerId, tick);
				}
			}
		}

		[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferChannel = 3, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
		public void Tick(int incomingTick, byte[] stateBytes)
		{
			if (incomingTick <= NetworkRunner.Instance.CurrentTick)
			{
				return;
			}
			// GD.Print("INCOMING DATA: " + BitConverter.ToString(HLBytes.Decompress(stateBytes)));
			NetworkRunner.Instance.CurrentTick = incomingTick;
			ImportState(new HLBuffer(HLBytes.Decompress(stateBytes)));
			foreach (var net_id in NetworkRunner.Instance.NetworkNodes.Keys)
			{
				var node = NetworkRunner.Instance.NetworkNodes[net_id];
				if (node == null)
					continue;
				if (node.IsQueuedForDeletion())
				{
					NetworkRunner.Instance.NetworkNodes.Remove(net_id);
					continue;
				}
				node._NetworkProcess(CurrentTick);
				foreach (var networkChild in node.NetworkChildren)
				{
					if (networkChild == null || networkChild.IsQueuedForDeletion())
					{
						continue;
					}
					networkChild._NetworkProcess(CurrentTick);
				}
			}
			RpcId(1, "TickAcknowledge", incomingTick);
		}

		[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferChannel = 3, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
		public void TickAcknowledge(int clientCurrentTick)
		{
			if (!NetworkRunner.Instance.IsServer)
			{
				return;
			}
			int peerId = Multiplayer.GetRemoteSenderId();
			PeerAcknowledge(peerId, clientCurrentTick);
		}
	}
}