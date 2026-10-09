// The terrain under the fog (M4-V4): vertex colours as before (TerrainMeshBuilder, sRGB), darkened per fragment by the
// smoothly filtered fog texture: unexplored black, explored dark and desaturated, visible full colour.
shader_type spatial;

#include "res://shaders/fog.gdshaderinc"

varying vec2 world_xz;

void vertex() {
	world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
	float s = fog_state_smooth(world_xz);
	ALBEDO = fog_apply(fog_srgb_to_linear(COLOR.rgb), s);
	ROUGHNESS = 1.0;
	SPECULAR = 0.5 * fog_brightness(s);
}
