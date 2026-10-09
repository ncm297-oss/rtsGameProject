// Fog of war (M4-V4, docs/03 "Implementation (M4-V4)"): the local player's fog as an R8 texture, one texel per map cell,
// holding the sim's own values (0 unexplored, 1 explored, 2 visible). FogOfWar.cs sets the uniforms on every material
// that includes this file; an unset texture reads white (clamped to visible), so a material without fog draws clear.

uniform sampler2D fog_tex : hint_default_white, filter_linear, repeat_disable;
// The map's size in meters (cells x cell size): world x / z over this is the texture coordinate.
uniform vec2 fog_world_size = vec2(256.0, 256.0);
// Brightness of explored ground (FogView.ExploredBrightness).
uniform float fog_explored = 0.4;

// The fog state (0..2) at a world point, smoothly filtered between cell centres (the terrain's soft edge).
float fog_state_smooth(vec2 xz) {
	return clamp(texture(fog_tex, xz / fog_world_size).r * 255.0, 0.0, 2.0);
}

// The fog state of the cell holding a world point, unfiltered: a prop or marker takes its cell's state whole.
float fog_state_cell(vec2 xz) {
	ivec2 size = textureSize(fog_tex, 0);
	ivec2 c = clamp(ivec2(floor(xz / fog_world_size * vec2(size))), ivec2(0), size - ivec2(1));
	return clamp(texelFetch(fog_tex, c, 0).r * 255.0, 0.0, 2.0);
}

// 0 unexplored (black), fog_explored explored, 1 visible; linear in between.
float fog_brightness(float s) {
	return s <= 1.0 ? s * fog_explored : mix(fog_explored, 1.0, s - 1.0);
}

// A linear colour under the fog: darkened, and half desaturated on explored ground.
vec3 fog_apply(vec3 c, float s) {
	float sat = 0.5 + 0.5 * clamp(s - 1.0, 0.0, 1.0);
	float luma = dot(c, vec3(0.299, 0.587, 0.114));
	return mix(vec3(luma), c, sat) * fog_brightness(s);
}

// sRGB vertex or instance colour to linear (what StandardMaterial3D's vertex_color_is_srgb does).
vec3 fog_srgb_to_linear(vec3 c) {
	return mix(pow((c + vec3(0.055)) * (1.0 / 1.055), vec3(2.4)), c * (1.0 / 12.92), lessThan(c, vec3(0.04045)));
}
