using Godot;
using Godot.Collections;

namespace HLNC
{
	public partial class NetworkVisibility : Node3D
	{
		[Export]
		public Array<Node> nodes = new Array<Node>();

		public override void _EnterTree()
		{
			var netParent = GetParentOrNull<NetworkNode3D>();
			if (netParent.IsCurrentOwner && !NetworkRunner.Instance.IsServer)
			{
				return;
			}

			while (nodes.Count > 0)
			{
				var node = nodes[0];
				nodes.RemoveAt(0);
				node.ProcessMode = ProcessModeEnum.Disabled;
				foreach (var child in node.GetChildren())
				{
					nodes.Add(child);
				}
				node.QueueFree();
			}
		}
	}
}