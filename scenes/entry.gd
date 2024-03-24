extends Control

func _on_start_server_pressed() -> void:
	get_tree().change_scene_to_file("res://scenes/main.tscn")
	NetworkRunner.StartServer()


func _on_start_client_pressed() -> void:
	get_tree().change_scene_to_file("res://scenes/main.tscn")
	NetworkRunner.StartClient()
