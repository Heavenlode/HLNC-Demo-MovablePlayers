extends Node3D

func _ready() -> void:
	if NetworkRunner.IsServer:
		NetworkRunner.PlayerConnected.connect(_on_player_connect)

func _on_player_connect(peerId):
	var character_scene = load("res://player/player.tscn")
	var character = character_scene.instantiate()
	character.InputAuthority = peerId
	add_child(character)
	character.position = Vector3(randf_range(-4, 4), 0, randf_range(-4, 4))
