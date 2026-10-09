// An unshaded marker under the fog (M4-V4): corpse discs and their rims, coloured per instance (sRGB). The instance takes
// the fog state of the cell under its origin: not drawn on unexplored ground, darkened on explored ground.
shader_type spatial;
render_mode unshaded;

#include "res://shaders/fog.gdshaderinc"

varying flat float fog_s;

void vertex() {
	fog_s = fog_state_cell((MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xz);
}

void fragment() {
	if (fog_s < 0.5) {
		discard;
	}
	ALBEDO = fog_apply(fog_srgb_to_linear(COLOR.rgb), fog_s);
}
