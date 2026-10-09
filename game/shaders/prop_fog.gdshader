// A lit prop under the fog (M4-V4): trees, mines and rubble. The whole instance takes the fog state of the cell under its
// origin: not drawn on unexplored ground, darkened on explored ground, clear where visible.
shader_type spatial;

#include "res://shaders/fog.gdshaderinc"

uniform vec4 albedo : source_color = vec4(1.0);
uniform float roughness = 0.9;

varying flat float fog_s;

void vertex() {
	fog_s = fog_state_cell((MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xz);
}

void fragment() {
	if (fog_s < 0.5) {
		discard;
	}
	ALBEDO = fog_apply(albedo.rgb, fog_s);
	ROUGHNESS = roughness;
}
