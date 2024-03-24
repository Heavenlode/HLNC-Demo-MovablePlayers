using System.Diagnostics;
using Godot;
using Godot.Collections;
using HLNC;

namespace MovablePlayers {

	[NetworkScenes("res://player/player.tscn")]
	public partial class Player : NetworkNode3D, INetworkInputHandler
	{
		private Dictionary<int, Variant> inputBuffer = new Dictionary<int, Variant>();
		public Dictionary<int, Variant> InputBuffer => inputBuffer;

		public enum InputType {
			MOVE = 0,
		}

        public override void _Process(double delta)
        {
            base._Process(delta);
			if (!IsCurrentOwner || NetworkRunner.Instance.IsServer) return;

			var moveDirection = new Vector2();

			if (Input.IsActionPressed("player_up")) {
				moveDirection.Y -= 1;
			}
			if (Input.IsActionPressed("player_down")) {
				moveDirection.Y += 1;
			}
			if (Input.IsActionPressed("player_left")) {
				moveDirection.X -= 1;
			}
			if (Input.IsActionPressed("player_right")) {
				moveDirection.X += 1;
			}

			inputBuffer[(int)InputType.MOVE] = moveDirection;
        }

        public override void _NetworkProcess(int _tick)
        {
            base._NetworkProcess(_tick);
			
			if (!NetworkRunner.Instance.IsServer) return;

			var input = GetInput();
			if (input == null) return;

			if (input.ContainsKey((int)InputType.MOVE)) {
				var moveDirection = ((Vector2)input[(int)InputType.MOVE]).Normalized();
				Position = new Vector3(
					Mathf.Clamp(Position.X + moveDirection.X, -5, 5),
					Position.Y,
					Mathf.Clamp(Position.Z + moveDirection.Y, -5, 5)
				);
			}
		}
    }
}
